using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Base/terminal activator that behaves like RecruitPromptUI.
/// Shows a prompt when the player is within range.
/// If enemies are active nearby, shows a warning and blocks activation.
/// After the first successful activation, the ready prompt will never show again.
/// Enemy scanning continues forever, and can trigger a separate 2-second warning UI after activation.
/// </summary>
public class BaseActivator : MonoBehaviour
{
    [Header("Prompt")]
    [SerializeField] private string readyPromptMessage = "Press M to activate";
    [SerializeField] private string blockedPromptMessage = "Enemies Active in Area";
    [SerializeField] private KeyCode activateKey = KeyCode.M;

    [Header("Blocked Prompt UI")]
    [Tooltip("Optional custom UI root shown instead of RecruitPromptUI when activation is blocked by nearby enemies.")]
    [SerializeField] private GameObject blockedPromptUI;
    [Tooltip("Optional text component updated with Blocked Prompt Message when Blocked Prompt UI is shown.")]
    [SerializeField] private TMP_Text blockedPromptText;

    [Header("Player Detection")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float promptRange = 12f;
    [SerializeField] private bool oneTimeUse = false;

    [Header("Starting State")]
    [Tooltip("If enabled, this base starts already captured/activated and will not require the player to press the activation key.")]
    [SerializeField] private bool capturedByDefault = false;
    [Tooltip("If enabled, On Activated is invoked once on startup when Captured By Default is checked.")]
    [SerializeField] private bool invokeActivatedEventOnStart = true;

    [Header("Enemy Blocking")]
    [SerializeField] private float enemyDetectionRadius = 100f;
    [Tooltip("Optional. If set, any collider on these layers inside Enemy Detection Radius blocks activation.")]
    [SerializeField] private LayerMask enemyLayers;
    [Tooltip("Fallback tags checked when Enemy Layers is empty or does not find anything.")]
    [SerializeField] private string[] enemyTags = new string[] { "Enemy" };
    [SerializeField] private bool drawDebugGizmos = true;

    [Header("Post-Activation Warning UI")]
    [Tooltip("Optional UI object to show for 2 seconds after the base has already been activated and an enemy is detected in range.")]
    [SerializeField] private GameObject enemyDetectedWarningUI;
    [SerializeField] private float enemyDetectedWarningDuration = 2f;

    [Header("Events")]
    [SerializeField] private UnityEvent onPlayerEnteredRange;
    [SerializeField] private UnityEvent onPlayerExitedRange;
    [SerializeField] private UnityEvent onActivated;
    [SerializeField] private UnityEvent onActivationBlockedByEnemies;

    private Transform player;
    private bool playerInRange;
    private bool hasActivated;
    private bool enemyWasDetectedAfterActivation;
    private string lastShownMessage = string.Empty;
    private Coroutine enemyWarningRoutine;
    private bool enemyWarningVisibleByThisScript;
    private readonly Collider[] overlapResults = new Collider[128];
    private static readonly Dictionary<int, BaseActivator> blockedPromptOwners = new Dictionary<int, BaseActivator>();

    public bool IsActivated => hasActivated;
    public bool AreEnemiesDetectedInArea => AreEnemiesActiveInArea();

    private void Awake()
    {
        FindPlayer();
        enemyWarningVisibleByThisScript = false;
        SetBlockedPromptVisible(false);

        if (capturedByDefault)
        {
            hasActivated = true;
            enemyWasDetectedAfterActivation = false;
            ClearPromptIfNeeded();

            if (invokeActivatedEventOnStart)
                onActivated?.Invoke();
        }
    }

    private void OnEnable()
    {
        FindPlayer();
        enemyWarningVisibleByThisScript = false;
        SetBlockedPromptVisible(false);
        RefreshPrompt(false);
    }

    private void OnDisable()
    {
        if (playerInRange)
            RecruitPromptUI.Hide();

        if (enemyWarningRoutine != null)
        {
            StopCoroutine(enemyWarningRoutine);
            enemyWarningRoutine = null;
        }

        HideEnemyWarningOwnedByThisScript();
        SetBlockedPromptVisible(false);
        playerInRange = false;
        lastShownMessage = string.Empty;
        enemyWasDetectedAfterActivation = false;
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null)
            {
                ClearPromptIfNeeded();
                HandlePostActivationEnemyWarning(false);
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

        bool enemiesActive = AreEnemiesActiveInArea();

        RefreshPrompt(enemiesActive);
        HandlePostActivationEnemyWarning(enemiesActive);

        if (!playerInRange)
            return;

        if (oneTimeUse && hasActivated)
            return;

        if (!Input.GetKeyDown(activateKey))
            return;

        if (enemiesActive)
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
        enemyWasDetectedAfterActivation = false;
        ClearPromptIfNeeded();
        onActivated?.Invoke();
    }

    public void SetPromptMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            readyPromptMessage = message;

        RefreshPrompt(AreEnemiesActiveInArea());
    }

    public void SetBlockedPromptMessage(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            blockedPromptMessage = message;

        RefreshPrompt(AreEnemiesActiveInArea());
    }

    public void SetPromptRange(float newRange)
    {
        promptRange = Mathf.Max(0.1f, newRange);
        RefreshPrompt(AreEnemiesActiveInArea());
    }

    public void SetEnemyDetectionRadius(float newRadius)
    {
        enemyDetectionRadius = Mathf.Max(0f, newRadius);
        bool enemiesActive = AreEnemiesActiveInArea();
        RefreshPrompt(enemiesActive);
        HandlePostActivationEnemyWarning(enemiesActive);
    }

    private void RefreshPrompt(bool enemiesActive)
    {
        if (!playerInRange)
        {
            ClearPromptIfNeeded();
            return;
        }

        // Surgical fix requested by user:
        // when the player is near this base and an enemy is inside the detection radius,
        // drive the EnemyNear / blocked UI directly.
        // This is no longer gated behind the base being uncaptured.
        if (enemiesActive)
        {
            if (blockedPromptText != null)
                blockedPromptText.text = blockedPromptMessage;

            RecruitPromptUI.Hide();
            SetBlockedPromptVisible(true);
            lastShownMessage = blockedPromptMessage;
            return;
        }

        // After the first successful activation, never show the ready prompt again.
        if (hasActivated)
        {
            ClearPromptIfNeeded();
            return;
        }

        SetBlockedPromptVisible(false);
        ShowPrompt(readyPromptMessage);
    }

    private void HandlePostActivationEnemyWarning(bool enemiesActive)
    {
        if (!hasActivated)
        {
            enemyWasDetectedAfterActivation = false;
            HideEnemyWarningOwnedByThisScript();
            return;
        }

        if (!enemiesActive)
        {
            enemyWasDetectedAfterActivation = false;
            HideEnemyWarningOwnedByThisScript();
            return;
        }

        if (enemyWasDetectedAfterActivation)
            return;

        enemyWasDetectedAfterActivation = true;
        ShowEnemyWarningForDuration();
    }

    private void ShowEnemyWarningForDuration()
    {
        if (enemyDetectedWarningUI == null)
            return;

        if (enemyWarningRoutine != null)
            StopCoroutine(enemyWarningRoutine);

        enemyWarningRoutine = StartCoroutine(EnemyWarningRoutine());
    }

    private IEnumerator EnemyWarningRoutine()
    {
        SetEnemyWarningUIVisible(true);
        yield return new WaitForSeconds(Mathf.Max(0.01f, enemyDetectedWarningDuration));
        HideEnemyWarningOwnedByThisScript();
        enemyWarningRoutine = null;
    }

    private void HideEnemyWarningOwnedByThisScript()
    {
        if (!enemyWarningVisibleByThisScript)
            return;

        SetEnemyWarningUIVisible(false);
    }

    private void SetEnemyWarningUIVisible(bool visible)
    {
        if (enemyDetectedWarningUI == null)
            return;

        enemyDetectedWarningUI.SetActive(visible);
        enemyWarningVisibleByThisScript = visible;
    }

    private void ShowPrompt(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        bool isBlockedMessage = message == blockedPromptMessage;

        if (isBlockedMessage)
        {
            if (blockedPromptText != null)
                blockedPromptText.text = blockedPromptMessage;

            if (blockedPromptUI != null)
            {
                RecruitPromptUI.Hide();
                SetBlockedPromptVisible(true);
                lastShownMessage = message;
                return;
            }
        }

        SetBlockedPromptVisible(false);

        if (lastShownMessage == message)
            return;

        RecruitPromptUI.Show(message);
        lastShownMessage = message;
    }

    private void ClearPromptIfNeeded()
    {
        if (string.IsNullOrEmpty(lastShownMessage) && !IsBlockedPromptVisible())
            return;

        RecruitPromptUI.Hide();
        SetBlockedPromptVisible(false);
        lastShownMessage = string.Empty;
    }

    private void SetBlockedPromptVisible(bool visible)
    {
        if (blockedPromptUI == null)
            return;

        int key = blockedPromptUI.GetInstanceID();

        if (visible)
        {
            blockedPromptOwners[key] = this;
            blockedPromptUI.SetActive(true);
            return;
        }

        if (blockedPromptOwners.TryGetValue(key, out BaseActivator owner) && owner != this)
            return;

        blockedPromptOwners.Remove(key);
        blockedPromptUI.SetActive(false);
    }

    private bool IsBlockedPromptVisible()
    {
        if (blockedPromptUI == null)
            return false;

        int key = blockedPromptUI.GetInstanceID();
        return blockedPromptUI.activeSelf && (!blockedPromptOwners.TryGetValue(key, out BaseActivator owner) || owner == this);
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
