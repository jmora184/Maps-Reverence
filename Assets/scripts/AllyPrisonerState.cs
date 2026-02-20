using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Drops an ally into a non-combat "prisoner/hostage" state until they are recruited (AllyActivationGate activates).
/// Goals:
/// 1) Play a hostage animation (eg. prisoner_kneel / surrender_handUp)
/// 2) Disable the ally weapon object
/// 3) Prevent hostility: ally will not fight, enemies will ignore (Enemy2Controller patch also skips inactive/prisoner allies)
///
/// Designed to avoid editing AllyController: we simply disable relevant behaviours while prisoner.
/// </summary>
[DisallowMultipleComponent]
public class AllyPrisonerState : MonoBehaviour
{
    [Header("Start State")]
    [Tooltip("If true, this ally starts as a prisoner at runtime.")]
    public bool startAsPrisoner = true;

    [Header("Recruit Integration")]
    [Tooltip("If null, auto-finds AllyActivationGate on this GameObject.")]
    public AllyActivationGate activationGate;

    [Header("Animation")]
    [Tooltip("Animator to drive hostage animation. If null, auto-finds in children.")]
    public Animator animator;

    [Tooltip("Animator state name to play while prisoner (eg. prisoner_kneel, surrender_handUp, panic_idle).")]
    public string prisonerStateName = "prisoner_kneel";

    [Tooltip("Animator state to return to when released (eg. m_weapon_idle_A). Leave blank to not force a state.")]
    public string releasedStateName = "m_weapon_idle_A";

    [Range(0f, 0.5f)]
    public float crossFadeSeconds = 0.08f;

    [Header("Weapon")]
    [Tooltip("Weapon root object to disable while prisoner (eg. w_ak47). If null, we attempt to auto-find a 'w_*' object under the right hand container.")]
    public GameObject weaponRoot;

    [Header("Movement")]
    [Tooltip("If true, stops the NavMeshAgent while prisoner.")]
    public bool freezeNavMeshAgent = true;

    [Header("Behaviour Disable While Prisoner")]
    [Tooltip("If empty, we auto-disable common ally scripts (AllyController, AllyMuzzleFlash, patrol scripts).")]
    public MonoBehaviour[] disableBehavioursWhilePrisoner;

    public bool IsPrisoner { get; private set; }

    private NavMeshAgent _agent;

    private void Awake()
    {
        if (activationGate == null)
            activationGate = GetComponent<AllyActivationGate>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null) _agent = GetComponentInChildren<NavMeshAgent>(true);

        if (weaponRoot == null)
            weaponRoot = TryAutoFindWeaponRoot();

        if (disableBehavioursWhilePrisoner == null || disableBehavioursWhilePrisoner.Length == 0)
            disableBehavioursWhilePrisoner = AutoCollectBehaviours();
    }

    private void Start()
    {
        if (startAsPrisoner)
            EnterPrisonerMode();
    }

    private void Update()
    {
        // If the player recruits/activates this ally (your existing J system), we release prisoner mode.
        if (IsPrisoner && activationGate != null && activationGate.IsActive)
        {
            ReleasePrisonerMode();
        }
    }

    [ContextMenu("Enter Prisoner Mode")]
    public void EnterPrisonerMode()
    {
        if (IsPrisoner) return;
        IsPrisoner = true;

        // Ensure the ally starts inactive for your recruit-by-J flow.
        if (activationGate != null && activationGate.IsActive)
            activationGate.Deactivate();

        // Freeze nav to keep them in place (optional).
        if (freezeNavMeshAgent && _agent != null && _agent.isActiveAndEnabled)
        {
            _agent.isStopped = true;
            if (_agent.hasPath) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
        }

        // Disable weapon visibility/usage.
        if (weaponRoot != null)
            weaponRoot.SetActive(false);

        // Disable behaviours that could make them hostile or animate like a combatant.
        if (disableBehavioursWhilePrisoner != null)
        {
            for (int i = 0; i < disableBehavioursWhilePrisoner.Length; i++)
            {
                var b = disableBehavioursWhilePrisoner[i];
                if (b != null) b.enabled = false;
            }
        }

        // Force prisoner animation state.
        PlayState(prisonerStateName);
    }

    [ContextMenu("Release Prisoner Mode")]
    public void ReleasePrisonerMode()
    {
        if (!IsPrisoner) return;
        IsPrisoner = false;

        // Re-enable behaviours.
        if (disableBehavioursWhilePrisoner != null)
        {
            for (int i = 0; i < disableBehavioursWhilePrisoner.Length; i++)
            {
                var b = disableBehavioursWhilePrisoner[i];
                if (b != null) b.enabled = true;
            }
        }

        // Re-enable nav agent (do not set a destination here; your normal logic will handle movement).
        if (_agent != null && _agent.isActiveAndEnabled)
            _agent.isStopped = false;

        // Re-enable weapon.
        if (weaponRoot != null)
            weaponRoot.SetActive(true);

        // Return to normal idle (optional).
        if (!string.IsNullOrEmpty(releasedStateName))
            PlayState(releasedStateName);
    }

    private void PlayState(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;

        // CrossFade is safer than Play (handles blending + avoids resetting layers harshly).
        animator.CrossFadeInFixedTime(stateName, crossFadeSeconds);
    }

    private MonoBehaviour[] AutoCollectBehaviours()
    {
        // Keep it conservative: only disable the core combat + muzzle flash + patrol.
        // Add/remove scripts in the inspector if you need more control.
        var list = new System.Collections.Generic.List<MonoBehaviour>();

        var allyController = GetComponent<AllyController>();
        if (allyController != null) list.Add(allyController);

        var muzzle = GetComponentInChildren<AllyMuzzleFlash>(true);
        if (muzzle != null) list.Add(muzzle);

        var patrolAuto = GetComponent<AllyPatrolAuto>();
        if (patrolAuto != null) list.Add(patrolAuto);

        var patrolPingPong = GetComponent<AllyPatrolPingPong>();
        if (patrolPingPong != null) list.Add(patrolPingPong);

        // You can add other scripts here later if needed.

        return list.ToArray();
    }

    private GameObject TryAutoFindWeaponRoot()
    {
        // Heuristic: find a child whose name starts with "w_" (your screenshot shows w_ak47).
        // Prefer searching under a right-hand container if it exists.
        Transform handContainer = FindChildByNameContains(transform, "hand_container");
        if (handContainer == null)
            handContainer = FindChildByNameContains(transform, "R Hand");

        Transform best = null;

        if (handContainer != null)
            best = FindFirstChildStartsWith(handContainer, "w_");

        if (best == null)
            best = FindFirstChildStartsWith(transform, "w_");

        return best != null ? best.gameObject : null;
    }

    private static Transform FindFirstChildStartsWith(Transform root, string prefix)
    {
        if (root == null) return null;

        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name.StartsWith(prefix))
                return c;

            var deep = FindFirstChildStartsWith(c, prefix);
            if (deep != null) return deep;
        }

        return null;
    }

    private static Transform FindChildByNameContains(Transform root, string contains)
    {
        if (root == null) return null;

        string needle = contains.ToLowerInvariant();

        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name != null && c.name.ToLowerInvariant().Contains(needle))
                return c;

            var deep = FindChildByNameContains(c, contains);
            if (deep != null) return deep;
        }

        return null;
    }
}
