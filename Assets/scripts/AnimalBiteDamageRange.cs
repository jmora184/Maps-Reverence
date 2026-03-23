// AnimalBiteDamageRange.cs
// "Good enough" bite damage with a sanity range check.
// When the Animator ENTERS the attack state, the player takes damage ONLY if within biteRange.
//
// Why this approach:
// - Your attack uses an Animator TRIGGER (not a bool), so we can't read "trigger true" directly.
// - Instead, we detect the Animator entering the attack animation state and apply damage once.
// - Adds a distance gate so the player only gets damaged when actually near the animal.
//
// Setup:
// 1) Add this script to the animal/insect root (or any object that can find the Animator).
// 2) Set attackStateName to the EXACT state name in your Animator (Layer 0).
// 3) Ensure the player GameObject is tagged "Player" (or change playerTag).
// 4) Set biteRange (requested: 10).
// 5) Optional: assign playerTransform if you don't want Tag lookup.
//
// Notes:
// - Deals damage once per state-enter by default.
// - If your attack animation can loop or stay in the state, keep tickDamageWhileInState = false
//   so it doesn't drain the player just for being nearby.

using System;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class AnimalBiteDamageRange : MonoBehaviour
{
    [Header("Animator")]
    public Animator animator;

    [Tooltip("Exact Animator state name on Layer 0 that represents the attack (case-sensitive).")]
    public string attackStateName = "Attack";

    [Tooltip("Animator layer index to watch (0 is default).")]
    public int layerIndex = 0;

    [Header("Attack SFX")]
    [Tooltip("Sound played when the animal enters its attack state.")]
    public AudioClip attackSfx;
    [Tooltip("Optional dedicated audio source. If empty, the script will try to find one on this object.")]
    public AudioSource attackAudioSource;
    [Range(0f, 1f)] public float attackSfxVolume = 1f;
    [Tooltip("If true, plays the attack sound when the animator enters the attack state.")]
    public bool playAttackSfxOnStateEnter = true;

    [Header("Player")]
    [Tooltip("Optional explicit reference. If null, we find by playerTag.")]
    public Transform playerTransform;

    public string playerTag = "Player";

    [Header("Range Gate")]
    [Tooltip("Player must be within this distance from the animal to take damage.")]
    public float biteRange = 10f;

    [Tooltip("Where range is measured from. If null, uses this transform.")]
    public Transform rangeOrigin;

    [Header("Multi-Target (Optional)")]
    [Tooltip("If false (default), this script behaves like before: it only damages the Player. If true, it can damage Player/Allies/Enemies based on toggles + tags within biteRange.")]
    public bool enableMultiTarget = false;

    [Tooltip("Which layers are considered damageable targets when Multi-Target is enabled.")]
    public LayerMask targetLayers = ~0;

    [Tooltip("If true, multi-target uses QueryTriggerInteraction.Collide so trigger hitboxes are included.")]
    public bool includeTriggerColliders = true;

    [Header("Target Factions (used when Multi-Target is enabled)")]
    public bool damagePlayer = true;
    public bool damageAllies = true;
    public bool damageEnemies = true;

    [Tooltip("Tag used for Ally targets.")]
    public string allyTag = "Ally";

    [Tooltip("Tag used for Enemy targets.")]
    public string enemyTag = "Enemy";

    [Header("Optional Tag Filter (Multi-Target)")]
    [Tooltip("If empty, tags are decided by faction toggles above. If set, only objects with ANY of these tags will be damaged.")]
    public string[] requiredTags;

    [Header("Damage")]
    public int damage = 5;

    [Tooltip("Minimum seconds between damage applications (prevents spam).")]
    public float damageCooldown = 0.75f;

    [Tooltip("If true, will apply damage repeatedly while staying in the attack state (using cooldown).")]
    public bool tickDamageWhileInState = false;

    [Header("Debug")]
    public bool logDamage = false;

    private bool _wasInAttackState;
    private float _nextDamageTime;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!rangeOrigin) rangeOrigin = transform;
        if (!attackAudioSource) attackAudioSource = GetComponent<AudioSource>();
    }

    private void Start()
    {
        if (!playerTransform) playerTransform = FindPlayerByTag();
        _wasInAttackState = IsInAttackState();
    }

    private void Update()
    {
        bool inAttack = IsInAttackState();

        // Enter event: false -> true
        if (inAttack && !_wasInAttackState)
        {
            if (playAttackSfxOnStateEnter)
                PlayAttackSfx();

            DealDamage("enter");
        }
        else if (inAttack && tickDamageWhileInState)
        {
            DealDamage("tick");
        }

        _wasInAttackState = inAttack;
    }

    public void PlayAttackSfx()
    {
        if (!attackSfx) return;

        if (!attackAudioSource) attackAudioSource = GetComponent<AudioSource>();

        if (attackAudioSource)
            attackAudioSource.PlayOneShot(attackSfx, attackSfxVolume);
        else
            AudioSource.PlayClipAtPoint(attackSfx, transform.position, attackSfxVolume);
    }

    private bool IsInAttackState()
    {
        if (!animator) return false;
        if (string.IsNullOrWhiteSpace(attackStateName)) return false;

        int li = Mathf.Clamp(layerIndex, 0, animator.layerCount - 1);
        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(li);
        return st.IsName(attackStateName);
    }

    private Transform FindPlayerByTag()
    {
        if (string.IsNullOrEmpty(playerTag)) return null;
        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        return go ? go.transform : null;
    }

    private void DealDamage(string reason)
    {
        if (Time.time < _nextDamageTime) return;

        Vector3 originPos = rangeOrigin ? rangeOrigin.position : transform.position;

        // Multi-target mode: damage allowed targets within biteRange
        if (enableMultiTarget)
        {
            Collider[] hits = Physics.OverlapSphere(originPos, biteRange, targetLayers, includeTriggerColliders ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return;

            bool hitAny = false;

            foreach (var c in hits)
            {
                if (!c) continue;

                // Don't damage ourselves
                if (c.transform == transform || c.transform.IsChildOf(transform))
                    continue;

                if (!IsAllowedTarget(c))
                    continue;

                if (ApplyDamage(c))
                    hitAny = true;
            }

            if (hitAny)
            {
                _nextDamageTime = Time.time + Mathf.Max(0.05f, damageCooldown);
                if (logDamage)
                    Debug.Log($"[AnimalBiteDamageRange] Dealt {damage} ({reason}) to targets within {biteRange}.", this);
            }

            return;
        }

        // Default (legacy) mode: Player-only
        if (!playerTransform) playerTransform = FindPlayerByTag();
        if (!playerTransform)
        {
            if (logDamage)
                Debug.LogWarning("[AnimalBiteDamageRange] Player not found (check Player tag or assign playerTransform).", this);
            return;
        }

        // Range check
        float dist = Vector3.Distance(originPos, playerTransform.position);
        if (dist > biteRange)
        {
            if (logDamage)
                Debug.Log($"[AnimalBiteDamageRange] Attack state '{attackStateName}' but player out of range ({dist:F2} > {biteRange}).", this);
            return;
        }

        if (ApplyDamage(playerTransform))
        {
            _nextDamageTime = Time.time + Mathf.Max(0.05f, damageCooldown);
            if (logDamage)
                Debug.Log($"[AnimalBiteDamageRange] Dealt {damage} to player ({reason}) at distance {dist:F2}.", this);
        }
        else
        {
            if (logDamage)
                Debug.LogWarning("[AnimalBiteDamageRange] Player found, but no damage receiver found (IDamageable/TakeDamage/etc).", this);
        }
    }

    private bool HasTagInParents(Transform t, string tag)
    {
        if (t == null || string.IsNullOrEmpty(tag)) return false;
        while (t != null)
        {
            if (string.Equals(t.tag, tag, System.StringComparison.OrdinalIgnoreCase))
                return true;
            t = t.parent;
        }
        return false;
    }

    bool IsAllowedTarget(Collider c)
    {
        if (!c) return false;

        // If Required Tags is set, it overrides toggles
        if (requiredTags != null && requiredTags.Length > 0)
        {
            string t1 = c.tag;
            string t2 = c.transform.root != null ? c.transform.root.tag : t1;
            for (int i = 0; i < requiredTags.Length; i++)
            {
                string req = requiredTags[i];
                if (string.IsNullOrEmpty(req)) continue;
                if (string.Equals(t1, req, System.StringComparison.OrdinalIgnoreCase) || string.Equals(t2, req, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // IMPORTANT: some prefabs have an untagged root with tagged children.
        // We therefore detect faction tags anywhere up the parent chain (starting from the collider hit).
        Transform t = c.transform;

        if (damagePlayer && HasTagInParents(t, playerTag)) return true;
        if (damageAllies && HasTagInParents(t, allyTag)) return true;
        if (damageEnemies && HasTagInParents(t, enemyTag)) return true;

        return false;
    }

    private void TryNotifyEnemyAggro(Collider c)
    {
        if (!c) return;

        // If the target has an enemy AI controller, notify it so it can aggro back onto THIS animal.
        // We prefer calling GetShot(attacker) if available because your Enemy2Controller uses it
        // to open a short-range "damage aggro" window and set combat target.
        MonoBehaviour[] monos = c.GetComponentsInParent<MonoBehaviour>(true);
        if (monos == null) return;

        for (int i = 0; i < monos.Length; i++)
        {
            var mb = monos[i];
            if (!mb) continue;

            var t = mb.GetType();

            // GetShot(Transform attacker)
            MethodInfo mGetShot = t.GetMethod("GetShot", BF, null, new Type[] { typeof(Transform) }, null);
            if (mGetShot != null)
            {
                mGetShot.Invoke(mb, new object[] { this.transform });
                return;
            }

            // Fallback: SetCombatTarget(Transform t) or SetTarget(Transform t)
            MethodInfo mSetCombatTarget = t.GetMethod("SetCombatTarget", BF, null, new Type[] { typeof(Transform) }, null);
            if (mSetCombatTarget != null)
            {
                mSetCombatTarget.Invoke(mb, new object[] { this.transform });
                return;
            }

            MethodInfo mSetTarget = t.GetMethod("SetTarget", BF, null, new Type[] { typeof(Transform) }, null);
            if (mSetTarget != null)
            {
                mSetTarget.Invoke(mb, new object[] { this.transform });
                return;
            }
        }
    }

    private bool ApplyDamage(Collider c)
    {
        TryNotifyEnemyAggro(c);

        if (!c) return false;

        // Preferred: IDamageable
        var dmg = c.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(damage);
            return true;
        }

        // Reflection fallback
        MonoBehaviour[] monos = c.GetComponentsInParent<MonoBehaviour>(true);
        if (monos != null)
        {
            foreach (var mb in monos)
            {
                if (!mb) continue;
                if (TryInvokeDamageMethod(mb, "TakeDamage", damage)) return true;
                if (TryInvokeDamageMethod(mb, "ApplyDamage", damage)) return true;
                if (TryInvokeDamageMethod(mb, "Damage", damage)) return true;
                if (TryInvokeDamageMethod(mb, "ReceiveDamage", damage)) return true;
                if (TryInvokeDamageMethod(mb, "Hurt", damage)) return true;
            }
        }

        // Last-ditch: SendMessage (won't throw if missing)
        c.gameObject.SendMessage("TakeDamage", (float)damage, SendMessageOptions.DontRequireReceiver);
        c.gameObject.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        return true;
    }

    private bool ApplyDamage(Transform player)
    {
        if (player == null) return false;

        // Preferred: IDamageable
        var dmg = player.GetComponentInParent<IDamageable>();
        if (dmg != null)
        {
            dmg.TakeDamage(damage);
            return true;
        }

        // Reflection fallback
        MonoBehaviour[] monos = player.GetComponentsInParent<MonoBehaviour>(true);
        if (monos != null)
        {
            foreach (var mb in monos)
            {
                if (!mb) continue;
                if (TryInvokeDamageMethod(mb, "TakeDamage", damage)) return true;
                if (TryInvokeDamageMethod(mb, "ApplyDamage", damage)) return true;
                if (TryInvokeDamageMethod(mb, "Damage", damage)) return true;
                if (TryInvokeDamageMethod(mb, "ReceiveDamage", damage)) return true;
                if (TryInvokeDamageMethod(mb, "Hurt", damage)) return true;
            }
        }

        // Last-ditch: SendMessage (won't throw if missing)
        player.gameObject.SendMessage("TakeDamage", (float)damage, SendMessageOptions.DontRequireReceiver);
        player.gameObject.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
        return true;
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

    private void OnDrawGizmosSelected()
    {
        Transform o = rangeOrigin != null ? rangeOrigin : transform;
        Gizmos.DrawWireSphere(o.position, biteRange);
    }
}
