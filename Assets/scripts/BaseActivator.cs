using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Base/terminal activator that behaves like RecruitPromptUI.
/// Shows a prompt when the player is within range.
/// If enemies are active nearby, shows a warning and blocks activation.
/// </summary>
public class BaseActivator : MonoBehaviour
{
    [Header("Prompt")]
    [SerializeField] private string readyPromptMessage = "Press M to activate";
    [SerializeField] private string blockedPromptMessage = "Enemies Active in Area";
    [SerializeField] private KeyCode activateKey = KeyCode.M;

    [Header("Player Detection")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float promptRange = 12f;
    [SerializeField] private bool oneTimeUse = false;

    [Header("Enemy Blocking")]
    [SerializeField] private float enemyDetectionRadius = 100f;
    [Tooltip("Optional. If set, any collider on these layers inside Enemy Detection Radius blocks activation.")]
    [SerializeField] private LayerMask enemyLayers;
    [Tooltip("Fallback tags checked when Enemy Layers is empty or does not find anything.")]
    [SerializeField] private string[] enemyTags = new string[] { "Enemy" };
    [SerializeField] private bool drawDebugGizmos = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onPlayerEnteredRange;
    [SerializeField] private UnityEvent onPlayerExitedRange;
    [SerializeField] private UnityEvent onActivated;
    [SerializeField] private UnityEvent onActivationBlockedByEnemies;

    private Transform player;
    private bool playerInRange;
    private bool hasActivated;
    private string lastShownMessage = string.Empty;
    private readonly Collider[] overlapResults = new Collider[128];

    private void Awake()
    {
        FindPlayer();
    }

    private void OnEnable()
    {
        FindPlayer();
        RefreshPrompt();
    }

    private void OnDisable()
    {
        if (playerInRange)
            RecruitPromptUI.Hide();

        playerInRange = false;
        lastShownMessage = string.Empty;
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null)
            {
                ClearPromptIfNeeded();
                return;
            }
        }

        bool isInRangeNow = Vector3.Distance(player.position, transform.position) <= promptRange;

        if (isInRangeNow != playerInRange)
        {
            playerInRange = isInRangeNow;

            if (playerInRange)
                onPlayerEnteredRange?.Invoke();
            else
                onPlayerExitedRange?.Invoke();
        }

        RefreshPrompt();

        if (!playerInRange)
            return;

        if (oneTimeUse && hasActivated)
            return;

        if (!Input.GetKeyDown(activateKey))
            return;

        if (AreEnemiesActiveInArea())
        {
            ShowPrompt(blockedPromptMessage);
            onActivationBlockedByEnemies?.Invoke();
            return;
        }

        Activate();
    }

    public void Activate()
    {
        if (oneTimeUse && hasActivated)
            return;

        if (AreEnemiesActiveInArea())
        {
            ShowPrompt(blockedPromptMessage);
            onActivationBlockedByEnemies?.Invoke();
            return;
        }

        hasActivated = true;
        RecruitPromptUI.Hide();
        lastShownMessage = string.Empty;
        onActivated?.Invoke();
    }

    public void SetPromptMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            readyPromptMessage = message;

        RefreshPrompt();
    }

    public void SetPromptRange(float newRange)
    {
        promptRange = Mathf.Max(0.1f, newRange);
        RefreshPrompt();
    }

    public void SetEnemyDetectionRadius(float newRadius)
    {
        enemyDetectionRadius = Mathf.Max(0f, newRadius);
        RefreshPrompt();
    }

    private void RefreshPrompt()
    {
        if (!playerInRange)
        {
            ClearPromptIfNeeded();
            return;
        }

        if (oneTimeUse && hasActivated)
        {
            ClearPromptIfNeeded();
            return;
        }

        ShowPrompt(AreEnemiesActiveInArea() ? blockedPromptMessage : readyPromptMessage);
    }

    private void ShowPrompt(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (lastShownMessage == message)
            return;

        RecruitPromptUI.Show(message);
        lastShownMessage = message;
    }

    private void ClearPromptIfNeeded()
    {
        if (string.IsNullOrEmpty(lastShownMessage))
            return;

        RecruitPromptUI.Hide();
        lastShownMessage = string.Empty;
    }

    private bool AreEnemiesActiveInArea()
    {
        if (enemyDetectionRadius <= 0f)
            return false;

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            enemyDetectionRadius,
            overlapResults,
            enemyLayers.value == 0 ? Physics.AllLayers : enemyLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapResults[i];
            if (hit == null)
                continue;

            if (IsValidEnemy(hit.transform))
                return true;
        }

        if (enemyLayers.value != 0)
            return false;

        // Fallback tag scan if no enemy layer mask is configured.
        if (enemyTags == null || enemyTags.Length == 0)
            return false;

        foreach (string tag in enemyTags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;

            GameObject[] tagged = GameObject.FindGameObjectsWithTag(tag);
            for (int i = 0; i < tagged.Length; i++)
            {
                GameObject go = tagged[i];
                if (go == null || !go.activeInHierarchy)
                    continue;

                float sqr = (go.transform.position - transform.position).sqrMagnitude;
                if (sqr <= enemyDetectionRadius * enemyDetectionRadius)
                    return true;
            }
        }

        return false;
    }

    private bool IsValidEnemy(Transform hit)
    {
        if (hit == null || !hit.gameObject.activeInHierarchy)
            return false;

        if (hit.CompareTag(playerTag))
            return false;

        if (enemyTags != null && enemyTags.Length > 0)
        {
            for (int i = 0; i < enemyTags.Length; i++)
            {
                string tag = enemyTags[i];
                if (!string.IsNullOrWhiteSpace(tag) && hit.CompareTag(tag))
                    return true;
            }
        }

        if (enemyLayers.value != 0)
            return ((1 << hit.gameObject.layer) & enemyLayers.value) != 0;

        return false;
    }

    private void FindPlayer()
    {
        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        player = go != null ? go.transform : null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
            return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, promptRange);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, enemyDetectionRadius);
    }
}
