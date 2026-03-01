// BiteHitboxTrigger.cs
// A fresh, trigger-based bite hitbox approach.
//
// Key goals:
// 1) Reliable hits only during an "attack window" (BeginBite/EndBite).
// 2) Still supports "instant hit" when BeginBite() is called (if desired).
// 3) Prevents multi-hit spam with per-target cooldown AND one-hit-per-attack option.
// 4) Handles the common overlap edge-case (hitbox already touching player when enabled)
//    with an "arm delay" + optional "require exit before hit".
//
// Setup (Unity):
// - Create a child GameObject under your animal's head/jaw called "MouthHitbox".
// - Add a SphereCollider (or BoxCollider) to MouthHitbox.
//     - Check "Is Trigger" = true
//     - Set radius/size small (start 0.35 - 0.6)
// - Add a Rigidbody to MouthHitbox OR to the animal root (recommended on root):
//     - isKinematic = true
//     - Use Gravity = false
//   (Unity requires at least one Rigidbody in the trigger pair for trigger callbacks.)
// - Put this script on MouthHitbox.
// - Call BeginBite() and EndBite() from animation events (recommended) or from code.
//
// Notes:
// - Make sure the player's capsule collider is on a layer included in targetLayers.
// - If your enemy uses a NavMeshAgent and hugs the player, consider increasing stoppingDistance.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class BiteHitboxTrigger : MonoBehaviour
{
    [Header("Damage")]
    public int damage = 5;

    [Tooltip("Seconds between repeat hits on the same target while overlapping (if multi-hit is enabled).")]
    public float perTargetCooldown = 0.5f;

    [Tooltip("If true, each target can only be hit once per BeginBite() window.")]
    public bool oneHitPerAttackWindow = true;

    [Header("Targets")]
    public LayerMask targetLayers = ~0;

    [Tooltip("Optional tag filter. Leave empty to ignore tags.")]
    public string requiredTag = "Player";

    [Header("Attack Window")]
    [Tooltip("If true, we try to hit immediately on BeginBite() by checking current overlaps.")]
    public bool instantHitOnBegin = true;

    [Tooltip("Delay (in physics steps) before the hitbox is considered 'armed'. 1 is a good default.")]
    [Range(0, 3)]
    public int armAfterFixedSteps = 1;

    [Tooltip("If true, targets already overlapping when the bite begins must exit once before they can be hit.")]
    public bool requireExitBeforeHit = false;

    [Header("Debug")]
    public bool logHits = false;

    private bool _active;
    private bool _armed;

    // Targets that were overlapping at bite start (only used when requireExitBeforeHit is true)
    private readonly HashSet<int> _mustExit = new HashSet<int>();

    // Per-attack tracking
    private readonly HashSet<int> _hitThisWindow = new HashSet<int>();

    // Per-target cooldown tracking
    private readonly Dictionary<int, float> _nextHitTime = new Dictionary<int, float>(32);

    private Collider _hitbox;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        _hitbox = GetComponent<Collider>();
        if (_hitbox != null && !_hitbox.isTrigger)
        {
            Debug.LogWarning("[BiteHitboxTrigger] Collider is not set to Trigger. Please enable Is Trigger.", this);
        }
    }

    /// <summary>Call at the beginning of the bite attack window.</summary>
    public void BeginBite()
    {
        _active = true;
        _armed = false;

        _hitThisWindow.Clear();
        _nextHitTime.Clear();
        _mustExit.Clear();

        // Record overlaps at start if we require an exit first.
        if (requireExitBeforeHit)
        {
            foreach (var col in OverlapsNow())
            {
                int id = GetTargetKey(col);
                if (id != 0) _mustExit.Add(id);
            }
        }

        // Optionally attempt an instant hit (requested behavior),
        // but still respect armAfterFixedSteps and requireExitBeforeHit.
        if (instantHitOnBegin && armAfterFixedSteps == 0)
        {
            _armed = true;
            TryHitOverlapsNow();
        }

        if (armAfterFixedSteps > 0)
            StartCoroutine(ArmAfterFixed());
    }

    /// <summary>Call at the end of the bite attack window.</summary>
    public void EndBite()
    {
        _active = false;
        _armed = false;

        _mustExit.Clear();
        _hitThisWindow.Clear();
        _nextHitTime.Clear();
    }

    private IEnumerator ArmAfterFixed()
    {
        for (int i = 0; i < armAfterFixedSteps; i++)
            yield return new WaitForFixedUpdate();

        if (!_active) yield break;

        _armed = true;

        // If instantHitOnBegin is true, we can apply the first hit as soon as we arm.
        if (instantHitOnBegin)
            TryHitOverlapsNow();
    }

    private void TryHitOverlapsNow()
    {
        foreach (var col in OverlapsNow())
        {
            TryHit(col);
        }
    }

    private List<Collider> OverlapsNow()
    {
        // Use the hitbox's bounds to find overlaps in a physics-friendly way.
        // For SphereCollider/BoxCollider this is approximate but very reliable.
        var results = new List<Collider>(8);
        if (_hitbox == null) return results;

        Bounds b = _hitbox.bounds;
        Vector3 center = b.center;
        Vector3 halfExtents = b.extents;

        Collider[] cols = Physics.OverlapBox(center, halfExtents, transform.rotation, targetLayers, QueryTriggerInteraction.Ignore);
        if (cols != null && cols.Length > 0)
            results.AddRange(cols);

        return results;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_active || !_armed) return;
        TryHit(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!_active || !_armed) return;

        // If we require exit-first, stay shouldn't hit until OnTriggerExit clears the gate.
        if (requireExitBeforeHit)
        {
            int id = GetTargetKey(other);
            if (id != 0 && _mustExit.Contains(id)) return;
        }

        // Stay is useful when the bite window begins after the overlap started,
        // or if the player doesn't "enter" due to already overlapping.
        TryHit(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!requireExitBeforeHit) return;

        int id = GetTargetKey(other);
        if (id != 0) _mustExit.Remove(id);
    }

    private void TryHit(Collider other)
    {
        if (other == null) return;

        // Layer check (fast)
        int otherLayerMask = 1 << other.gameObject.layer;
        if ((targetLayers.value & otherLayerMask) == 0) return;

        // Optional tag filter (checks root too)
        if (!string.IsNullOrEmpty(requiredTag))
        {
            if (!(other.CompareTag(requiredTag) || (other.transform.root != null && other.transform.root.CompareTag(requiredTag))))
                return;
        }

        int key = GetTargetKey(other);
        if (key == 0) return;

        if (requireExitBeforeHit && _mustExit.Contains(key))
            return;

        float now = Time.time;

        if (oneHitPerAttackWindow && _hitThisWindow.Contains(key))
            return;

        if (_nextHitTime.TryGetValue(key, out float nextTime) && now < nextTime)
            return;

        if (ApplyDamage(other))
        {
            _hitThisWindow.Add(key);
            _nextHitTime[key] = now + Mathf.Max(0.05f, perTargetCooldown);

            if (logHits)
                Debug.Log($"[BiteHitboxTrigger] Hit '{other.transform.root.name}' for {damage}.", this);
        }
    }

    private int GetTargetKey(Collider c)
    {
        if (c == null) return 0;
        GameObject go = (c.transform.root != null) ? c.transform.root.gameObject : c.gameObject;
        return go != null ? go.GetInstanceID() : 0;
    }

    private bool ApplyDamage(Collider c)
    {
        // Preferred: your project's IDamageable interface.
        var dmg = c.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(damage);
            return true;
        }

        // Fallback: reflection on common method names.
        MonoBehaviour[] monos = c.GetComponentsInParent<MonoBehaviour>(true);
        if (monos == null || monos.Length == 0) return false;

        foreach (var mb in monos)
        {
            if (mb == null) continue;
            if (TryInvokeDamageMethod(mb, "TakeDamage", damage)) return true;
            if (TryInvokeDamageMethod(mb, "ApplyDamage", damage)) return true;
            if (TryInvokeDamageMethod(mb, "Damage", damage)) return true;
            if (TryInvokeDamageMethod(mb, "ReceiveDamage", damage)) return true;
            if (TryInvokeDamageMethod(mb, "Hurt", damage)) return true;
        }

        return false;
    }

    private bool TryInvokeDamageMethod(MonoBehaviour target, string methodName, int dmgAmount)
    {
        Type t = target.GetType();

        MethodInfo mInt = t.GetMethod(methodName, BF, null, new Type[] { typeof(int) }, null);
        if (mInt != null)
        {
            mInt.Invoke(target, new object[] { dmgAmount });
            return true;
        }

        MethodInfo mFloat = t.GetMethod(methodName, BF, null, new Type[] { typeof(float) }, null);
        if (mFloat != null)
        {
            mFloat.Invoke(target, new object[] { (float)dmgAmount });
            return true;
        }

        return false;
    }
}
