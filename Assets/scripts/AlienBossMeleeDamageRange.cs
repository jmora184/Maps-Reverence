using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// AlienBossMeleeDamageRange
/// Damage system like AnimalBiteDamageRange (range gated, no hitbox colliders).
///
/// How it works:
/// - Watches the Animator for entering an attack animation state (pound/swing by default).
/// - When entering attack state, it damages ONLY the boss's current target if within attackRange.
/// - Also notifies the victim AI to aggro back onto the boss (GetShot/SetCombatTarget), similar to your animal script.
///
/// Setup:
/// 1) Add this to the AlienBoss root (same object as AlienBossController, or child that can find it).
/// 2) Assign animator (or it auto-finds).
/// 3) Ensure attackStateNames match EXACT Animator state names (case-sensitive).
/// 4) Set attackRange + damage.
/// </summary>
[DisallowMultipleComponent]
public class AlienBossMeleeDamageRange : MonoBehaviour
{
    [Header("References")]
    public AlienBossController boss;
    public Animator animator;

    [Header("Attack State Detection")]
    [Tooltip("Exact Animator state names on Layer 0 that represent attacks (case-sensitive).")]
    public string[] attackStateNames = new string[] { "pound", "swing" };

    [Tooltip("Animator layer index to watch (0 is default).")]
    public int layerIndex = 0;

    [Header("Range Gate")]
    [Tooltip("Target must be within this distance from the boss to take damage.")]
    public float attackRange = 2.2f;

    [Tooltip("Where range is measured from. If null, uses this transform.")]
    public Transform rangeOrigin;

    [Header("Damage")]
    public int damage = 12;

    [Tooltip("Minimum seconds between damage applications (prevents double-hits when transitions are weird).")]
    public float damageCooldown = 0.75f;

    [Header("Attack Audio")]
    public AudioClip poundSfx;
    public AudioClip swingSfx;
    public AudioClip attackSfxFallback;
    public AudioSource attackAudioSource;
    [Range(0f, 1f)] public float attackSfxVolume = 1f;
    public bool playAttackSfxOnStateEnter = true;

    [Header("Debug")]
    public bool logDamage = false;

    private bool _wasInAttack;
    private float _nextDamageTime;

    private static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        if (!boss) boss = GetComponentInParent<AlienBossController>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!rangeOrigin) rangeOrigin = transform;
        if (!attackAudioSource) attackAudioSource = GetComponent<AudioSource>();
        if (!attackAudioSource) attackAudioSource = GetComponentInChildren<AudioSource>();
    }

    private void Start()
    {
        _wasInAttack = IsInAttackState();
    }

    private void Update()
    {
        bool inAttack = IsInAttackState();

        if (inAttack && !_wasInAttack)
        {
            if (playAttackSfxOnStateEnter)
                PlayAttackSfxForCurrentState();

            DealDamageOnce("enter");
        }

        _wasInAttack = inAttack;
    }

    private bool IsInAttackState()
    {
        return !string.IsNullOrEmpty(GetCurrentAttackStateName());
    }

    private string GetCurrentAttackStateName()
    {
        if (!animator) return null;
        if (attackStateNames == null || attackStateNames.Length == 0) return null;

        int li = Mathf.Clamp(layerIndex, 0, animator.layerCount - 1);
        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(li);

        for (int i = 0; i < attackStateNames.Length; i++)
        {
            string n = attackStateNames[i];
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (st.IsName(n)) return n;
        }

        return null;
    }

    public void PlayAttackSfxForCurrentState()
    {
        string stateName = GetCurrentAttackStateName();
        AudioClip clip = GetAttackClipForState(stateName);
        PlayAttackSfx(clip);
    }

    public void PlayAttackSfx(AudioClip clip)
    {
        if (!clip) return;

        if (!attackAudioSource)
        {
            attackAudioSource = GetComponent<AudioSource>();
            if (!attackAudioSource) attackAudioSource = GetComponentInChildren<AudioSource>();
        }

        if (attackAudioSource)
            attackAudioSource.PlayOneShot(clip, Mathf.Clamp01(attackSfxVolume));
    }

    private AudioClip GetAttackClipForState(string stateName)
    {
        if (!string.IsNullOrWhiteSpace(stateName))
        {
            string lower = stateName.ToLowerInvariant();
            if (lower.Contains("pound") && poundSfx) return poundSfx;
            if (lower.Contains("swing") && swingSfx) return swingSfx;
        }

        if (poundSfx) return poundSfx;
        if (swingSfx) return swingSfx;
        return attackSfxFallback;
    }

    private void DealDamageOnce(string reason)
    {
        if (Time.time < _nextDamageTime) return;
        if (!boss || !boss.IsAlive) return;

        Transform target = boss.CurrentTarget;
        if (!target) return;

        Vector3 origin = rangeOrigin ? rangeOrigin.position : transform.position;
        float dist = Vector3.Distance(origin, target.position);

        if (dist > attackRange)
        {
            if (logDamage)
                Debug.Log($"[AlienBossMeleeDamageRange] Attack '{reason}' but target out of range ({dist:F2} > {attackRange}).", this);
            return;
        }

        bool applied = ApplyDamage(target, damage);
        if (!applied)
        {
            if (logDamage)
                Debug.LogWarning("[AlienBossMeleeDamageRange] Target found, but no damage receiver found (IDamageable/TakeDamage/etc).", this);
            return;
        }

        _nextDamageTime = Time.time + Mathf.Max(0.05f, damageCooldown);

        if (logDamage)
            Debug.Log($"[AlienBossMeleeDamageRange] Dealt {damage} to '{target.name}' ({reason}) at distance {dist:F2}.", this);
    }

    private bool ApplyDamage(Transform target, int dmgAmount)
    {
        if (!target) return false;

        // Notify victim AI to aggro back onto THIS boss
        TryNotifyAggroBack(target);

        // Preferred: IDamageable (search parent OR children)
        var dmg = target.GetComponentInParent<IDamageable>();
        if (dmg == null) dmg = target.GetComponentInChildren<IDamageable>(true);

        if (dmg != null)
        {
            dmg.TakeDamage(dmgAmount);
            return true;
        }

        // Reflection fallback (search parent OR children)
        MonoBehaviour[] parentMonos = target.GetComponentsInParent<MonoBehaviour>(true);
        if (parentMonos != null)
        {
            foreach (var mb in parentMonos)
            {
                if (!mb) continue;
                if (TryInvokeDamageMethod(mb, "TakeDamage", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "ApplyDamage", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "Damage", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "ReceiveDamage", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "Hurt", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "DamageAlly", dmgAmount)) return true;
            }
        }

        MonoBehaviour[] childMonos = target.GetComponentsInChildren<MonoBehaviour>(true);
        if (childMonos != null)
        {
            foreach (var mb in childMonos)
            {
                if (!mb) continue;
                if (TryInvokeDamageMethod(mb, "TakeDamage", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "ApplyDamage", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "Damage", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "ReceiveDamage", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "Hurt", dmgAmount)) return true;
                if (TryInvokeDamageMethod(mb, "DamageAlly", dmgAmount)) return true;
            }
        }

        // Last-ditch: SendMessage (root + children)
        target.gameObject.SendMessage("TakeDamage", (float)dmgAmount, SendMessageOptions.DontRequireReceiver);
        target.gameObject.SendMessage("TakeDamage", dmgAmount, SendMessageOptions.DontRequireReceiver);
        target.gameObject.SendMessage("DamageAlly", dmgAmount, SendMessageOptions.DontRequireReceiver);
        target.gameObject.SendMessage("DamageAlly", (float)dmgAmount, SendMessageOptions.DontRequireReceiver);

        var childTransforms = target.GetComponentsInChildren<Transform>(true);
        if (childTransforms != null)
        {
            foreach (var ct in childTransforms)
            {
                if (!ct) continue;
                ct.gameObject.SendMessage("TakeDamage", (float)dmgAmount, SendMessageOptions.DontRequireReceiver);
                ct.gameObject.SendMessage("TakeDamage", dmgAmount, SendMessageOptions.DontRequireReceiver);
                ct.gameObject.SendMessage("DamageAlly", dmgAmount, SendMessageOptions.DontRequireReceiver);
                ct.gameObject.SendMessage("DamageAlly", (float)dmgAmount, SendMessageOptions.DontRequireReceiver);
            }
        }

        return true;
    }

    private void TryNotifyAggroBack(Transform victim)
    {
        if (!victim) return;

        MonoBehaviour[] monos = victim.GetComponentsInParent<MonoBehaviour>(true);
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

            // SetCombatTarget(Transform t)
            MethodInfo mSetCombatTarget = t.GetMethod("SetCombatTarget", BF, null, new Type[] { typeof(Transform) }, null);
            if (mSetCombatTarget != null)
            {
                mSetCombatTarget.Invoke(mb, new object[] { this.transform });
                return;
            }

            // SetTarget(Transform t)
            MethodInfo mSetTarget = t.GetMethod("SetTarget", BF, null, new Type[] { typeof(Transform) }, null);
            if (mSetTarget != null)
            {
                mSetTarget.Invoke(mb, new object[] { this.transform });
                return;
            }
        }
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

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform o = rangeOrigin != null ? rangeOrigin : transform;
        Gizmos.DrawWireSphere(o.position, attackRange);
    }
#endif
}
