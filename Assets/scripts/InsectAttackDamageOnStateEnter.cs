// InsectAttackDamageOnStateEnter.cs
// Works when your Animator uses a TRIGGER (not a bool).
//
// Why:
// - Animator TRIGGERs are not readable like a bool ("true/false").
// - So instead, we detect when the Animator ENTERS the attack state (which the trigger causes)
//   and apply damage at that moment.
//
// What it does:
// - Each time layer 0 enters `attackStateName`, deal `damage` to the Player once.
// - Optional: allow repeated damage while staying in the state (tickDamageWhileInState).
//
// Setup:
// 1) Add this script to the insect/ant root (or any object that can find the Animator).
// 2) Set `attackStateName` to the EXACT state name in your Animator (layer 0).
//    (Example: "Attack", "Bite", "InsectAttack", etc.)
// 3) Make sure the player GameObject is tagged "Player" (or change playerTag).
// 4) Test with logDamage = true.
//
// No hitboxes, no range — this is just "attack animation happened => damage".

using System;
using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class InsectAttackDamageOnStateEnter : MonoBehaviour
{
    [Header("Animator")]
    public Animator animator;

    [Tooltip("Exact Animator state name on Layer 0 that represents the attack (case-sensitive).")]
    public string attackStateName = "Attack";

    [Tooltip("Animator layer index to watch (0 is default).")]
    public int layerIndex = 0;

    [Header("Player")]
    public string playerTag = "Player";

    [Header("Damage")]
    public int damage = 5;

    [Tooltip("Minimum seconds between damage applications (prevents spam).")]
    public float damageCooldown = 0.75f;

    [Tooltip("If true, will apply damage repeatedly while staying in the attack state (using cooldown).")]
    public bool tickDamageWhileInState = false;

    [Header("Debug")]
    public bool logDamage = false;

    private Transform _player;
    private bool _wasInAttackState;
    private float _nextDamageTime;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        CachePlayer();
        _wasInAttackState = IsInAttackState();
    }

    private void Update()
    {
        bool inAttack = IsInAttackState();

        // Enter event: false -> true
        if (inAttack && !_wasInAttackState)
        {
            DealDamage("enter");
        }
        else if (inAttack && tickDamageWhileInState)
        {
            DealDamage("tick");
        }

        _wasInAttackState = inAttack;
    }

    private bool IsInAttackState()
    {
        if (!animator) return false;
        if (string.IsNullOrWhiteSpace(attackStateName)) return false;

        // Safety: if layer index out of range, Unity throws. We'll clamp.
        int li = Mathf.Clamp(layerIndex, 0, animator.layerCount - 1);

        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(li);
        return st.IsName(attackStateName);
    }

    private void CachePlayer()
    {
        if (string.IsNullOrEmpty(playerTag)) return;
        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        _player = go ? go.transform : null;
    }

    private void DealDamage(string reason)
    {
        if (Time.time < _nextDamageTime) return;

        if (!_player) CachePlayer();
        if (!_player)
        {
            if (logDamage)
                Debug.LogWarning("[InsectAttackDamageOnStateEnter] Player not found (check Player tag).", this);
            return;
        }

        if (ApplyDamage(_player))
        {
            _nextDamageTime = Time.time + Mathf.Max(0.05f, damageCooldown);
            if (logDamage)
                Debug.Log($"[InsectAttackDamageOnStateEnter] Damage {damage} ({reason}) while in state '{attackStateName}'.", this);
        }
        else
        {
            if (logDamage)
                Debug.LogWarning("[InsectAttackDamageOnStateEnter] Player found, but no damage receiver found (IDamageable/TakeDamage/etc).", this);
        }
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

        // Reflection fallback: scan MonoBehaviours on the player root chain.
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

        // Last-ditch: SendMessage (won't error if absent with DontRequireReceiver)
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
}
