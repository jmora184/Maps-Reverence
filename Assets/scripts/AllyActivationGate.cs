using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Per-ally activation gate:
/// - If inactive: ally can optionally ignore player-command movement but still allow AI/patrol movement.
/// - Player can activate by proximity + key (default J).
/// - While inactive and in range, shows RecruitPromptUI.
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
    [SerializeField] private float recruitPromptRangeOverride = 0f;

    private int _originalLayer;
    private NavMeshAgent _agent;
    private Transform _player;

    public bool IsActive { get; private set; }

    private void Awake()
    {
        IsActive = startActive;

        _originalLayer = gameObject.layer;
        _agent = GetComponent<NavMeshAgent>();

        ResolvePlayer();
        ApplyInactiveState();
    }

    private void OnEnable()
    {
        ResolvePlayer();
    }

    private void Update()
    {
        if (IsActive) return;

        if (_player == null) ResolvePlayer();

        // IMPORTANT: When inactive, we default to NOT freezing the agent so patrol/AI can still move.
        // Commands are blocked elsewhere (CommandExecutor/selection rules) by checking IsActive.
        if (freezeNavMeshAgentWhileInactive && _agent != null && _agent.isActiveAndEnabled)
        {
            if (!_agent.isStopped) _agent.isStopped = true;
            if (_agent.hasPath) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
        }

        bool inPromptRange = showRecruitPrompt && _player != null && IsPlayerInRange(GetPromptRange());
        if (inPromptRange)
        {
            string msg = string.IsNullOrEmpty(recruitPromptText) ? "Press {KEY} to recruit" : recruitPromptText;
            msg = msg.Replace("{KEY}", activationKey.ToString());
            RecruitPromptUI.Show(msg);
        }
        else
        {
            RecruitPromptUI.Hide();
        }

        if (_player != null && IsPlayerInRange(activationRange) && Input.GetKeyDown(activationKey))
            Activate();
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;

        if (blockSelectionWhileInactive)
            gameObject.layer = _originalLayer;

        if (_agent != null && _agent.isActiveAndEnabled)
            _agent.isStopped = false;

        RecruitPromptUI.Hide();
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;

        ApplyInactiveState();
        RecruitPromptUI.Hide();
    }

    private void ApplyInactiveState()
    {
        if (IsActive) return;

        if (blockSelectionWhileInactive)
        {
            int ignore = LayerMask.NameToLayer("Ignore Raycast");
            if (ignore >= 0) gameObject.layer = ignore;
        }

        // Only freeze movement if you explicitly want inactive allies to be totally immobile.
        if (freezeNavMeshAgentWhileInactive && _agent != null && _agent.isActiveAndEnabled)
        {
            _agent.isStopped = true;
            if (_agent.hasPath) _agent.ResetPath();
            _agent.velocity = Vector3.zero;
        }
    }

    private float GetPromptRange()
    {
        return (recruitPromptRangeOverride > 0f) ? recruitPromptRangeOverride : activationRange;
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
}
