using UnityEngine;

/// Proximity greeting UI that works on BOTH NPCs and Allies.
/// - Shows a dialog message while player is near (collider-aware distance + buffer)
/// - Optionally drives a talk animation while visible:
///     A) If an NPCController is present, it calls npcController.SetTalking(true/false)
///     B) Otherwise, it can directly set an Animator bool (default: "Talking")
///
/// EXTRA RULES:
/// - Prisoner/hostage allies can suppress talk and/or UI (via AllyPrisonerState.IsPrisoner)
/// - If the character is RUNNING/WALKING (moving) or in a SHOOT state, we do NOT allow talk animation.
///   (The greeting UI can still show, we just stop the talk animation.)
public class NPCProximityGreeting : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NPCDialogBoxUI dialogUI;      // optional; auto-finds
    [SerializeField] private Transform playerOverride;     // optional; drag Player
    [SerializeField] private string playerTag = "Player";

    [Header("Optional: talk animation")]
    [Tooltip("If present, we call npcController.SetTalking(...) while greeting is visible.")]
    [SerializeField] private NPCController npcController;

    [Tooltip("If npcController is not present, we can drive an Animator bool directly.")]
    [SerializeField] private Animator talkAnimator;

    [Tooltip("If present and IsPrisoner is true, we can suppress talk and/or UI (for Allies).")]
    [SerializeField] private AllyPrisonerState allyPrisonerState;

    [Tooltip("Animator bool parameter to drive while greeting is visible.")]
    public string talkingBoolParam = "Talking";

    [Tooltip("If true, sets Talking bool to true while visible and false when hidden.")]
    public bool driveTalkAnimation = true;

    [Header("Prisoner/Hostage Behavior (Allies)")]
    [Tooltip("If true, do NOT play talk animation while the ally is a prisoner.")]
    public bool suppressTalkWhenPrisoner = true;

    [Tooltip("If true, do NOT show the dialog box / greeting text while the ally is a prisoner.")]
    public bool suppressUIWhenPrisoner = true;

    [Header("Movement / Combat suppression")]
    [Tooltip("If true, don't talk while moving (walking/running).")]
    public bool suppressTalkWhenMoving = true;

    [Tooltip("If you have a Speed float param, we'll use it to detect movement. Leave empty to skip.")]
    public string speedFloatParam = "Speed";

    [Tooltip("If Speed param exists, values above this mean 'moving'.")]
    public float movingSpeedThreshold = 0.15f;

    [Tooltip("If these bool params exist and are true, we treat as moving.")]
    public string isWalkingBoolParam = "isWalking";
    public string isRunningBoolParam = "isRunning";

    [Tooltip("If true, don't talk while in these animator state names (exact match).")]
    public bool suppressTalkInStates = true;

    [Tooltip("Common: m_weapon_shoot. Add more if needed.")]
    public string[] stateNamesThatBlockTalk = new string[] { "m_weapon_shoot" };

    [Header("Prompt")]
    [TextArea] public string greeting = "Hello Captain";
    public float range = 2.5f;
    public float rangeBuffer = 0.75f;

    [Header("Distance Mode")]
    public bool useColliderSurfaceDistance = true;
    public Collider selfColliderOverride;

    [Header("Optional: hide when hostile / weapon drawn")]
    public bool hideWhenWeaponActive = true;

    [Tooltip("Drag the character's weapon GameObject here (e.g., NPC w_usp45). DO NOT set this to Player.")]
    public GameObject weaponObject;

    [Header("Debug")]
    public bool debugLogs = false;

    private Transform _player;
    private Collider _selfCollider;
    private bool _shown;

    // Animator param caches
    private int _talkingHash;
    private bool _hasTalkingParam;

    private int _speedHash;
    private bool _hasSpeedParam;

    private int _walkHash;
    private bool _hasWalkParam;

    private int _runHash;
    private bool _hasRunParam;

    void Start()
    {
        _player = playerOverride ? playerOverride : FindPlayerByTag();

        if (!dialogUI) dialogUI = FindFirstObjectByType<NPCDialogBoxUI>();

        if (!npcController) npcController = GetComponent<NPCController>();

        if (!allyPrisonerState) allyPrisonerState = GetComponent<AllyPrisonerState>();

        if (!talkAnimator)
            talkAnimator = GetComponentInChildren<Animator>(true);

        CacheAnimatorParams();

        _selfCollider = selfColliderOverride ? selfColliderOverride : GetComponentInChildren<Collider>();

        if (debugLogs)
        {
            Debug.Log($"[NPCProximityGreeting] player={(_player ? _player.name : "NULL")} dialogUI={(dialogUI ? dialogUI.name : "NULL")} " +
                      $"npcController={(npcController ? npcController.name : "NULL")} allyPrisoner={(allyPrisonerState ? allyPrisonerState.IsPrisoner.ToString() : "NULL")} " +
                      $"talkAnimator={(talkAnimator ? talkAnimator.name : "NULL")} hasTalkingParam={_hasTalkingParam} " +
                      $"speedParam={_hasSpeedParam} walkParam={_hasWalkParam} runParam={_hasRunParam} collider={(_selfCollider ? _selfCollider.name : "NULL")}",
                      this);
        }
    }

    void CacheAnimatorParams()
    {
        _talkingHash = Animator.StringToHash(talkingBoolParam);
        _speedHash = Animator.StringToHash(speedFloatParam);
        _walkHash = Animator.StringToHash(isWalkingBoolParam);
        _runHash = Animator.StringToHash(isRunningBoolParam);

        _hasTalkingParam = _hasSpeedParam = _hasWalkParam = _hasRunParam = false;

        if (talkAnimator == null) return;

        foreach (var p in talkAnimator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && p.name == talkingBoolParam) _hasTalkingParam = true;
            if (!string.IsNullOrWhiteSpace(speedFloatParam) && p.type == AnimatorControllerParameterType.Float && p.name == speedFloatParam) _hasSpeedParam = true;
            if (!string.IsNullOrWhiteSpace(isWalkingBoolParam) && p.type == AnimatorControllerParameterType.Bool && p.name == isWalkingBoolParam) _hasWalkParam = true;
            if (!string.IsNullOrWhiteSpace(isRunningBoolParam) && p.type == AnimatorControllerParameterType.Bool && p.name == isRunningBoolParam) _hasRunParam = true;
        }
    }

    Transform FindPlayerByTag()
    {
        var go = GameObject.FindGameObjectWithTag(playerTag);
        return go ? go.transform : null;
    }

    void Update()
    {
        if (!_player || !dialogUI) return;

        // Weapon-active hide (common for NPCs going hostile)
        if (hideWhenWeaponActive && weaponObject != null && weaponObject.activeInHierarchy)
        {
            HideIfShown();
            return;
        }

        bool isPrisoner = IsAllyPrisoner();

        // If prisoner and we suppress UI, keep everything hidden (no flicker)
        if (isPrisoner && suppressUIWhenPrisoner)
        {
            HideIfShown();
            return;
        }

        float d = ComputeDistance();
        bool inRange = d <= (range + rangeBuffer);

        if (inRange && !_shown)
        {
            dialogUI.Show(greeting);
            _shown = true;
            SetTalking(true);

            if (debugLogs) Debug.Log($"[NPCProximityGreeting] SHOW '{greeting}' d={d:F2} thr={(range + rangeBuffer):F2}", this);
        }
        else if (!inRange && _shown)
        {
            HideIfShown();

            if (debugLogs) Debug.Log($"[NPCProximityGreeting] HIDE d={d:F2} thr={(range + rangeBuffer):F2}", this);
        }
        else if (_shown)
        {
            // While visible, enforce "no talk while prisoner/moving/shooting"
            if (ShouldBlockTalkingNow())
                SetTalking(false);
        }
    }

    float ComputeDistance()
    {
        Vector3 p = _player.position;

        if (useColliderSurfaceDistance && _selfCollider != null)
        {
            Vector3 closest = _selfCollider.ClosestPoint(p);
            return Vector3.Distance(p, closest);
        }

        return Vector3.Distance(p, transform.position);
    }

    bool IsAllyPrisoner()
    {
        return allyPrisonerState != null && allyPrisonerState.IsPrisoner;
    }

    bool IsMoving()
    {
        if (talkAnimator == null) return false;

        if (_hasSpeedParam)
        {
            float spd = talkAnimator.GetFloat(_speedHash);
            if (spd > movingSpeedThreshold) return true;
        }

        if (_hasWalkParam && talkAnimator.GetBool(_walkHash)) return true;
        if (_hasRunParam && talkAnimator.GetBool(_runHash)) return true;

        return false;
    }

    bool IsInBlockedState()
    {
        if (!suppressTalkInStates) return false;
        if (talkAnimator == null) return false;
        if (stateNamesThatBlockTalk == null || stateNamesThatBlockTalk.Length == 0) return false;

        AnimatorStateInfo st = talkAnimator.GetCurrentAnimatorStateInfo(0);
        for (int i = 0; i < stateNamesThatBlockTalk.Length; i++)
        {
            string name = stateNamesThatBlockTalk[i];
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (st.IsName(name)) return true;
        }

        return false;
    }

    bool ShouldBlockTalkingNow()
    {
        // Prisoner rule
        if (suppressTalkWhenPrisoner && IsAllyPrisoner())
            return true;

        // Movement rule
        if (suppressTalkWhenMoving && IsMoving())
            return true;

        // Shooting/state rule
        if (IsInBlockedState())
            return true;

        return false;
    }

    void SetTalking(bool talking)
    {
        if (!driveTalkAnimation) return;

        // If we should block talking now, never turn it on.
        if (talking && ShouldBlockTalkingNow())
            return;

        // Preferred: NPCController (enforces "no talk while hostile" on NPCs)
        if (npcController != null)
        {
            npcController.SetTalking(talking);
            return;
        }

        // Fallback: set animator bool directly (works for Allies)
        if (talkAnimator != null && _hasTalkingParam)
        {
            talkAnimator.SetBool(_talkingHash, talking);
        }
    }

    void HideIfShown()
    {
        if (!_shown) return;
        dialogUI.Hide();
        _shown = false;

        // Always stop talking when hiding
        if (driveTalkAnimation) SetTalking(false);
    }

    void OnDisable()
    {
        HideIfShown();
    }
}
