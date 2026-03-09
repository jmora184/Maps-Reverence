// 2026-03-07
// NPCProximityGreeting.cs
//
// Behavior modes:
// - Auto-talk (NPC style): if player is in range, dialog auto-opens; leaving range closes.
// - Press-key (Ally style): if player is in range, shows prompt "Press P to talk"; press key toggles dialog.
//
// Added one-time intro behavior for allies:
// - If an ally changes from prisoner -> freed, dialog auto-opens ONE time.
// - If another script activates/recruits the ally, call TriggerOneTimeIntroDialog().
// - After that first auto-open, normal behavior resumes (press P unless auto-show mode is enabled).

using UnityEngine;

[DisallowMultipleComponent]
public class NPCProximityGreeting : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NPCDialogBoxUI dialogUI;      // optional; auto-finds
    [SerializeField] private Transform playerOverride;     // optional; drag Player
    [SerializeField] private string playerTag = "Player";

    [Header("Mode")]
    [Tooltip("If true: dialog auto-opens when in range (NPC style). If false: requires talkKey (Ally style).")]
    public bool autoOpenDialogOnProximity = false;

    [Header("Talk Input (Press-key mode)")]
    [Tooltip("Press this key while in range to open/close the dialog box (when autoOpenDialogOnProximity=false).")]
    public KeyCode talkKey = KeyCode.P;

    [Tooltip("Prompt text shown while in range and dialog is closed (press-key mode).")]
    public string talkPromptMessage = "Press P to talk";

    [Tooltip("Use RecruitPromptUI for the prompt (recommended). If false, no prompt will show.")]
    public bool useRecruitPromptUI = true;

    [Header("One-Time Intro (Ally Recruit / Free)")]
    [Tooltip("If true, the dialog box auto-opens one time when this ally is first freed from prisoner mode.")]
    public bool autoOpenOnceWhenFreed = true;

    [Tooltip("If true, closing after the one-time auto-open will fall back to normal behavior (Press P unless auto-show mode is enabled).")]
    public bool oneTimeIntroOnlyOnce = true;

    [Header("Optional: talk animation")]
    [Tooltip("If present, we call npcController.SetTalking(...) while dialog is open.")]
    [SerializeField] private NPCController npcController;

    [Tooltip("If npcController is not present, we can drive an Animator bool directly.")]
    [SerializeField] private Animator talkAnimator;

    [Tooltip("If present and IsPrisoner is true, we can suppress talk and/or UI (for Allies).")]
    [SerializeField] private AllyPrisonerState allyPrisonerState;

    [Tooltip("Animator bool parameter to drive while dialog is open.")]
    public string talkingBoolParam = "Talking";

    [Tooltip("If true, sets Talking bool to true while dialog is open and false when closed.")]
    public bool driveTalkAnimation = true;

    [Header("Prisoner/Hostage Behavior (Allies)")]
    [Tooltip("If true, do NOT play talk animation while the ally is a prisoner.")]
    public bool suppressTalkWhenPrisoner = true;

    [Tooltip("If true, do NOT show the dialog box / prompt while the ally is a prisoner.")]
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

    [Header("Dialog Message")]
    [TextArea] public string greeting = "Hello Captain";

    [Header("Range")]
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

    private bool _promptShown;
    private bool _dialogOpen;
    private bool _oneTimeIntroShown;
    private bool _wasPrisoner;

    // Animator param caches
    private int _talkingHash;
    private bool _hasTalkingParam;

    private int _speedHash;
    private bool _hasSpeedParam;

    private int _walkHash;
    private bool _hasWalkParam;

    private int _runHash;
    private bool _hasRunParam;

    private void Start()
    {
        _player = playerOverride ? playerOverride : FindPlayerByTag();

        if (!dialogUI) dialogUI = FindFirstObjectByType<NPCDialogBoxUI>();

        if (!npcController) npcController = GetComponent<NPCController>();

        if (!allyPrisonerState) allyPrisonerState = GetComponent<AllyPrisonerState>();

        if (!talkAnimator)
            talkAnimator = GetComponentInChildren<Animator>(true);

        CacheAnimatorParams();

        _selfCollider = selfColliderOverride ? selfColliderOverride : GetComponentInChildren<Collider>();
        _wasPrisoner = IsAllyPrisoner();

        // Ensure hidden at start
        CloseDialog();
        HidePrompt();

        if (debugLogs)
        {
            Debug.Log($"[NPCProximityGreeting] mode={(autoOpenDialogOnProximity ? "AUTO" : "PRESS")} player={(_player ? _player.name : "NULL")} dialogUI={(dialogUI ? dialogUI.name : "NULL")} prisoner={_wasPrisoner}", this);
        }
    }

    private void CacheAnimatorParams()
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

    private Transform FindPlayerByTag()
    {
        var go = GameObject.FindGameObjectWithTag(playerTag);
        return go ? go.transform : null;
    }

    private void Update()
    {
        if (!_player || !dialogUI) return;

        // Weapon-active hide (common for NPCs going hostile)
        if (hideWhenWeaponActive && weaponObject != null && weaponObject.activeInHierarchy)
        {
            ForceHideAll();
            return;
        }

        bool isPrisoner = IsAllyPrisoner();

        // One-time auto-open when prisoner becomes freed.
        if (autoOpenOnceWhenFreed && !_oneTimeIntroShown && _wasPrisoner && !isPrisoner)
        {
            TriggerOneTimeIntroDialog();
        }
        _wasPrisoner = isPrisoner;

        // If prisoner and we suppress UI, keep everything hidden (no flicker)
        if (isPrisoner && suppressUIWhenPrisoner)
        {
            ForceHideAll();
            return;
        }

        float d = ComputeDistance();
        bool inRange = d <= (range + rangeBuffer);

        if (!inRange)
        {
            // Leaving range: close everything
            if (_dialogOpen || _promptShown)
                ForceHideAll();
            return;
        }

        // In range
        if (autoOpenDialogOnProximity)
        {
            // NPC-style: auto-open once, keep open while in range
            if (!_dialogOpen)
            {
                OpenDialog();
                HidePrompt();
                if (debugLogs) Debug.Log($"[NPCProximityGreeting] AUTO OPEN d={d:F2}", this);
            }

            // While open, enforce "no talk animation while prisoner/moving/shooting"
            if (_dialogOpen && ShouldBlockTalkingNow())
                SetTalking(false);

            return;
        }

        // Ally-style: press key to toggle dialog. Show prompt while closed.
        if (!_dialogOpen)
            ShowPrompt();

        if (Input.GetKeyDown(talkKey))
        {
            if (_dialogOpen)
            {
                CloseDialog();
                ShowPrompt(); // still in range
                if (debugLogs) Debug.Log($"[NPCProximityGreeting] CLOSE dialog (key) d={d:F2}", this);
            }
            else
            {
                OpenDialog();
                HidePrompt();
                if (debugLogs) Debug.Log($"[NPCProximityGreeting] OPEN dialog (key) d={d:F2}", this);
            }
        }

        // While dialog is open, enforce "no talk animation while prisoner/moving/shooting"
        if (_dialogOpen && ShouldBlockTalkingNow())
            SetTalking(false);
    }

    private float ComputeDistance()
    {
        Vector3 p = _player.position;

        if (useColliderSurfaceDistance && _selfCollider != null)
        {
            Vector3 closest = _selfCollider.ClosestPoint(p);
            return Vector3.Distance(p, closest);
        }

        return Vector3.Distance(p, transform.position);
    }

    private bool IsAllyPrisoner()
    {
        return allyPrisonerState != null && allyPrisonerState.IsPrisoner;
    }

    private void ShowPrompt()
    {
        if (!_promptShown && useRecruitPromptUI)
        {
            RecruitPromptUI.Show(talkPromptMessage);
            _promptShown = true;
        }
    }

    private void HidePrompt()
    {
        if (_promptShown && useRecruitPromptUI)
        {
            RecruitPromptUI.Hide();
            _promptShown = false;
        }
    }

    private void OpenDialog()
    {
        dialogUI.Show(greeting);
        _dialogOpen = true;
        SetTalking(true);
    }

    private void CloseDialog()
    {
        // Only hide the shared dialog UI if THIS NPC actually opened it.
        if (_dialogOpen && dialogUI != null)
            dialogUI.Hide();

        _dialogOpen = false;

        // Always stop talking when closing
        if (driveTalkAnimation) SetTalking(false);
    }

    private void ForceHideAll()
    {
        CloseDialog();
        HidePrompt();
    }

    private bool IsMoving()
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

    private bool IsInBlockedState()
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

    private bool ShouldBlockTalkingNow()
    {
        if (suppressTalkWhenPrisoner && IsAllyPrisoner())
            return true;

        if (suppressTalkWhenMoving && IsMoving())
            return true;

        if (IsInBlockedState())
            return true;

        return false;
    }

    private void SetTalking(bool talking)
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

    public void TriggerOneTimeIntroDialog()
    {
        if (_oneTimeIntroShown && oneTimeIntroOnlyOnce)
            return;

        _oneTimeIntroShown = true;
        OpenDialog();
        HidePrompt();

        if (debugLogs)
            Debug.Log("[NPCProximityGreeting] ONE-TIME INTRO OPEN", this);
    }

    private void OnDisable()
    {
        ForceHideAll();
    }
}
