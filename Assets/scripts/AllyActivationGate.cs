using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Per-ally activation gate:
/// - If inactive: ally can optionally ignore player-command movement but still allow AI/patrol movement.
/// - Player can activate by proximity + key (default J).
/// - While inactive and in range, shows RecruitPromptUI.
/// - While active and in range, can optionally show a combined talk/follow prompt and add this ally to PlayerSquadFollowSystem.
/// </summary>
[DisallowMultipleComponent]
public class AllyActivationGate : MonoBehaviour
{
    [Header("Start State")]
    [SerializeField] private bool startActive = true;

    [Header("Activation Input")]
    [SerializeField] private KeyCode activationKey = KeyCode.J;
    [SerializeField] private float activationRange = 2.5f;

    [Tooltip("Optional explicit player Transform. If null, we search by tag.")]
    [SerializeField] private Transform playerOverride;
    [SerializeField] private string playerTag = "Player";

    [Header("Inactive Behavior")]
    [Tooltip("If true, sets this GameObject layer to Ignore Raycast while inactive (prevents selection).")]
    [SerializeField] private bool blockSelectionWhileInactive = true;

    [Tooltip("If true, freezes the NavMeshAgent while inactive (prevents ALL movement, including patrol).")]
    [SerializeField] private bool freezeNavMeshAgentWhileInactive = false;

    [Header("Recruit Prompt UI")]
    [SerializeField] private bool showRecruitPrompt = true;
    [SerializeField] private string recruitPromptText = "Press {KEY} to recruit";
    [Tooltip("If > 0, uses this range for showing the prompt. If <= 0, uses activationRange.")]
    [SerializeField] private float recruitPromptRangeOverride = 9f;

    [Header("Follow Prompt")]
    [SerializeField] private bool showFollowPrompt = true;
    [SerializeField] private KeyCode followKey = KeyCode.N;
    [SerializeField] private string followPromptText = "Press {KEY} to Follow";
    [Tooltip("Max followers allowed at one time. Keep this in sync with PlayerSquadFollowSystem.")]
    [SerializeField] private int maxFollowerSlots = 6;
    [Tooltip("If > 0, uses this range for showing the follow prompt. If <= 0, uses activationRange.")]
    [SerializeField] private float followPromptRangeOverride = 9f;

    [Header("Talk Prompt Integration")]
    [SerializeField] private bool includeTalkPromptWhenActive = true;
    [Tooltip("If true, this script takes over the shared RecruitPromptUI for active allies so Talk + Follow can appear together.")]
    [SerializeField] private bool suppressNPCGreetingPromptUI = true;

    private int _originalLayer;
    private NavMeshAgent _agent;
    private Transform _player;
    private bool _promptShowing;
    private NPCProximityGreeting _greeting;

    public bool IsActive { get; private set; }

    private void Awake()
    {
        IsActive = startActive;

        _originalLayer = gameObject.layer;
        _agent = GetComponent<NavMeshAgent>();
        _greeting = GetComponent<NPCProximityGreeting>();

        ResolvePlayer();
        ApplyInactiveState();
        ApplySharedPromptOwnership();
    }

    private void OnEnable()
    {
        ResolvePlayer();
        ApplySharedPromptOwnership();
    }

    private void OnDisable()
    {
        HidePrompt();
    }

    private void Update()
    {
        if (_player == null) ResolvePlayer();

        if (!IsActive)
        {
            UpdateInactiveState();
            return;
        }

        UpdateActivePromptAndFollowState();
    }

    private void UpdateInactiveState()
    {
        if (freezeNavMeshAgentWhileInactive && _agent != null && _agent.isActiveAndEnabled)
        {
            if (!_agent.isStopped) _agent.isStopped = true;
            if (_agent.hasPath) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
        }

        bool inPromptRange = showRecruitPrompt && _player != null && IsPlayerInRange(GetRecruitPromptRange());
        if (inPromptRange)
        {
            string msg = string.IsNullOrEmpty(recruitPromptText) ? "Press {KEY} to recruit" : recruitPromptText;
            msg = msg.Replace("{KEY}", activationKey.ToString());
            RecruitPromptUI.Show(msg);
            _promptShowing = true;
        }
        else
        {
            HidePrompt();
        }

        if (_player != null && IsPlayerInRange(activationRange) && Input.GetKeyDown(activationKey))
            Activate();
    }

    private void UpdateActivePromptAndFollowState()
    {
        var followSystem = PlayerSquadFollowSystem.Instance != null
            ? PlayerSquadFollowSystem.Instance
            : PlayerSquadFollowSystem.EnsureExists();

        bool isInTalkRange = ShouldShowTalkPromptNow();
        bool isAlreadyFollowing = followSystem != null && followSystem.IsFollowing(transform);

        bool isInFollowRange = showFollowPrompt
                               && _player != null
                               && IsPlayerInRange(GetFollowPromptRange());

        bool hasFollowerRoom = followSystem != null
                               && followSystem.FollowerCount < Mathf.Max(1, maxFollowerSlots)
                               && followSystem.HasFollowerCapacity();

        bool canShowFollow = isInFollowRange && !isAlreadyFollowing && hasFollowerRoom;

        string combinedPrompt = BuildCombinedPrompt(isInTalkRange, canShowFollow);

        if (!string.IsNullOrWhiteSpace(combinedPrompt))
        {
            RecruitPromptUI.Show(combinedPrompt);
            _promptShowing = true;
        }
        else
        {
            HidePrompt();
        }

        if (canShowFollow && Input.GetKeyDown(followKey))
        {
            bool added = followSystem.TryAddFollowerDirect(transform, true);
            if (added)
            {
                string talkOnly = BuildCombinedPrompt(isInTalkRange, false);
                if (!string.IsNullOrWhiteSpace(talkOnly))
                {
                    RecruitPromptUI.Show(talkOnly);
                    _promptShowing = true;
                }
                else
                {
                    HidePrompt();
                }
            }
        }
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;

        if (blockSelectionWhileInactive)
            gameObject.layer = _originalLayer;

        if (_agent != null && _agent.isActiveAndEnabled)
            _agent.isStopped = false;

        ApplySharedPromptOwnership();
        HidePrompt();
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;

        ApplyInactiveState();
        ApplySharedPromptOwnership();
        HidePrompt();
    }

    private void ApplyInactiveState()
    {
        if (IsActive) return;

        if (blockSelectionWhileInactive)
        {
            int ignore = LayerMask.NameToLayer("Ignore Raycast");
            if (ignore >= 0) gameObject.layer = ignore;
        }

        if (freezeNavMeshAgentWhileInactive && _agent != null && _agent.isActiveAndEnabled)
        {
            _agent.isStopped = true;
            if (_agent.hasPath) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
        }
    }

    private float GetRecruitPromptRange()
    {
        return (recruitPromptRangeOverride > 0f) ? recruitPromptRangeOverride : activationRange;
    }

    private float GetFollowPromptRange()
    {
        return (followPromptRangeOverride > 0f) ? followPromptRangeOverride : activationRange;
    }

    private bool IsPlayerInRange(float range)
    {
        if (_player == null) return false;
        return Vector3.Distance(_player.position, transform.position) <= range;
    }

    private void ResolvePlayer()
    {
        if (playerOverride != null)
        {
            _player = playerOverride;
            return;
        }

        GameObject playerObj = null;
        try { playerObj = GameObject.FindGameObjectWithTag(playerTag); } catch { }

        _player = (playerObj != null) ? playerObj.transform : null;
    }

    private void HidePrompt()
    {
        if (!_promptShowing) return;
        RecruitPromptUI.Hide();
        _promptShowing = false;
    }

    private void ApplySharedPromptOwnership()
    {
        if (_greeting == null || !suppressNPCGreetingPromptUI) return;

        if (IsActive && includeTalkPromptWhenActive)
            _greeting.useRecruitPromptUI = false;
        else
            _greeting.useRecruitPromptUI = true;
    }

    private bool ShouldShowTalkPromptNow()
    {
        if (!includeTalkPromptWhenActive) return false;
        if (_greeting == null) return false;
        if (_player == null) return false;
        if (_greeting.autoOpenDialogOnProximity) return false;
        if (!_greeting.useRecruitPromptUI && !suppressNPCGreetingPromptUI) return false;

        float talkRange = Mathf.Max(0.01f, _greeting.range + _greeting.rangeBuffer);
        return IsPlayerInRange(talkRange);
    }

    private string BuildCombinedPrompt(bool showTalk, bool showFollow)
    {
        string msg = string.Empty;

        if (showTalk && _greeting != null)
        {
            msg = string.IsNullOrWhiteSpace(_greeting.talkPromptMessage)
                ? $"Press {_greeting.talkKey} to talk"
                : _greeting.talkPromptMessage;

            msg = msg.Replace("{KEY}", _greeting.talkKey.ToString());
        }

        if (showFollow)
        {
            string followMsg = string.IsNullOrWhiteSpace(followPromptText)
                ? "Press {KEY} to Follow"
                : followPromptText;

            followMsg = followMsg.Replace("{KEY}", followKey.ToString());

            if (string.IsNullOrWhiteSpace(msg))
                msg = followMsg;
            else
                msg += "\n" + followMsg;
        }

        return msg;
    }
}
