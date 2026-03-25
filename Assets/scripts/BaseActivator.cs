using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Base/terminal activator that behaves like RecruitPromptUI.
/// Shows a prompt when the player is within range.
/// If enemies are active nearby, shows a warning and blocks activation.
/// After the first successful activation, the ready prompt will never show again.
/// After activation, if enemies enter the base radius, show the warning and a countdown.
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
    [Tooltip("Optional UI object shown while this base has already been activated and an enemy is detected in range.")]
    [SerializeField] private GameObject enemyDetectedWarningUI;

    [Header("Post-Activation Countdown")]
    [Tooltip("Optional TMP text shown as 'Game Over in 59' while this base has already been activated and an enemy is detected in range.")]
    [SerializeField] private TMP_Text gameOverCountdownText;
    [SerializeField] private string startScreenSceneName = "StartScreen";

    [Header("Activation Audio")]
    [SerializeField] private AudioClip activatedSfx;
    [SerializeField] private AudioSource activationAudioSource;
    [SerializeField, Range(0f, 1f)] private float activatedSfxVolume = 1f;
    [SerializeField] private bool usePlayClipAtPointForActivation = false;
    [SerializeField, Range(0f, 1f)] private float activationSpatialBlend = 1f;
    [SerializeField] private float activationMinDistance = 10f;
    [SerializeField] private float activationMaxDistance = 35f;

    [Header("Events")]
    [SerializeField] private UnityEvent onPlayerEnteredRange;
    [SerializeField] private UnityEvent onPlayerExitedRange;
    [SerializeField] private UnityEvent onActivated;
    [SerializeField] private UnityEvent onActivationBlockedByEnemies;

    private const float GameOverCountdownStartSeconds = 59f;

    private Transform player;
    private bool playerInRange;
    private bool hasActivated;
    private bool enemyWasDetectedAfterActivation;
    private string lastShownMessage = string.Empty;
    private bool enemyWarningVisibleByThisScript;
    private float gameOverCountdownRemaining = GameOverCountdownStartSeconds;
    private readonly Collider[] overlapResults = new Collider[128];
    private static readonly Dictionary<int, BaseActivator> blockedPromptOwners = new Dictionary<int, BaseActivator>();

    public bool IsActivated => hasActivated;
    public bool AreEnemiesDetectedInArea => AreEnemiesActiveInArea();

    private void Awake()
    {
        FindPlayer();
        EnsureActivationAudioSource();
        enemyWarningVisibleByThisScript = false;
        SetBlockedPromptVisible(false);
        ResetPostActivationEnemyUI();

        if (capturedByDefault)
        {
            hasActivated = true;
            ClearPromptIfNeeded();

            if (invokeActivatedEventOnStart)
                onActivated?.Invoke();
        }
    }

    private void OnEnable()
    {
        FindPlayer();
        EnsureActivationAudioSource();
        enemyWarningVisibleByThisScript = false;
        SetBlockedPromptVisible(false);
        ResetPostActivationEnemyUI();
        RefreshPrompt(false);
    }

    private void OnDisable()
    {
        if (playerInRange)
            RecruitPromptUI.Hide();

        ResetPostActivationEnemyUI();
        SetBlockedPromptVisible(false);
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
                HandlePostActivationEnemyUI(false);
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
        HandlePostActivationEnemyUI(enemiesActive);

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
        ClearPromptIfNeeded();
        ResetPostActivationEnemyUI();
        PlayActivatedSfx();
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
        HandlePostActivationEnemyUI(enemiesActive);
    }

    private void RefreshPrompt(bool enemiesActive)
    {
        if (!playerInRange)
        {
            ClearPromptIfNeeded();
            return;
        }

        if (enemiesActive)
        {
            if (blockedPromptText != null)
                blockedPromptText.text = blockedPromptMessage;

            RecruitPromptUI.Hide();
            SetBlockedPromptVisible(true);
            lastShownMessage = blockedPromptMessage;
            return;
        }

        if (hasActivated)
        {
            ClearPromptIfNeeded();
            return;
        }

        SetBlockedPromptVisible(false);
        ShowPrompt(readyPromptMessage);
    }

    private void HandlePostActivationEnemyUI(bool enemiesActive)
    {
        if (!hasActivated || !enemiesActive)
        {
            ResetPostActivationEnemyUI();
            return;
        }

        if (!enemyWasDetectedAfterActivation)
        {
            enemyWasDetectedAfterActivation = true;
            gameOverCountdownRemaining = GameOverCountdownStartSeconds;
            UpdateGameOverCountdownText();
        }

        SetEnemyWarningUIVisible(true);
        SetGameOverCountdownVisible(true);

        if (gameOverCountdownRemaining > 0f)
        {
            gameOverCountdownRemaining -= Time.deltaTime;
            if (gameOverCountdownRemaining < 0f)
                gameOverCountdownRemaining = 0f;

            UpdateGameOverCountdownText();

            if (gameOverCountdownRemaining <= 0f)
                LoadStartScreenScene();
        }
    }

    private void ResetPostActivationEnemyUI()
    {
        enemyWasDetectedAfterActivation = false;
        gameOverCountdownRemaining = GameOverCountdownStartSeconds;
        HideEnemyWarningOwnedByThisScript();
        SetGameOverCountdownVisible(false);
        UpdateGameOverCountdownText();
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

    private void SetGameOverCountdownVisible(bool visible)
    {
        if (gameOverCountdownText == null)
            return;

        gameOverCountdownText.gameObject.SetActive(visible);
    }

    private void UpdateGameOverCountdownText()
    {
        if (gameOverCountdownText == null)
            return;

        gameOverCountdownText.text = "Game Over in " + Mathf.CeilToInt(gameOverCountdownRemaining);
    }

    private void LoadStartScreenScene()
    {
        if (string.IsNullOrWhiteSpace(startScreenSceneName))
            return;

        SceneManager.LoadScene(startScreenSceneName);
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
                if (sqr <= enemyDetectionRadius * enemyDetectionRadius && IsEnemyAlive(go.transform))
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

        bool matchesEnemy = false;

        if (enemyTags != null && enemyTags.Length > 0)
        {
            for (int i = 0; i < enemyTags.Length; i++)
            {
                string tag = enemyTags[i];
                if (!string.IsNullOrWhiteSpace(tag) && hit.CompareTag(tag))
                {
                    matchesEnemy = true;
                    break;
                }
            }
        }

        if (!matchesEnemy && enemyLayers.value != 0)
            matchesEnemy = ((1 << hit.gameObject.layer) & enemyLayers.value) != 0;

        if (!matchesEnemy)
            return false;

        return IsEnemyAlive(hit);
    }

    private bool IsEnemyAlive(Transform hit)
    {
        Transform current = hit;

        while (current != null)
        {
            MonoBehaviour[] behaviours = current.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                Type type = behaviour.GetType();
                string typeName = type.Name;

                if (typeName.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("Health", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (TryGetBoolMember(type, behaviour, "isDead", out bool isDead) && isDead)
                    return false;

                if (TryGetBoolMember(type, behaviour, "dead", out bool dead) && dead)
                    return false;

                if (TryGetFloatMember(type, behaviour, "currentHealth", out float currentHealth) && currentHealth <= 0f)
                    return false;

                if (TryGetFloatMember(type, behaviour, "health", out float health) && health <= 0f)
                    return false;

                if (TryGetFloatMember(type, behaviour, "currentHP", out float currentHp) && currentHp <= 0f)
                    return false;

                if (TryGetFloatMember(type, behaviour, "hp", out float hp) && hp <= 0f)
                    return false;
            }

            current = current.parent;
        }

        return true;
    }

    private bool TryGetBoolMember(Type type, object instance, string memberName, out bool value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null && field.FieldType == typeof(bool))
        {
            value = (bool)field.GetValue(instance);
            return true;
        }

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
        {
            value = (bool)property.GetValue(instance, null);
            return true;
        }

        value = false;
        return false;
    }

    private bool TryGetFloatMember(Type type, object instance, string memberName, out float value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        FieldInfo field = type.GetField(memberName, flags);
        if (field != null)
        {
            if (field.FieldType == typeof(float))
            {
                value = (float)field.GetValue(instance);
                return true;
            }

            if (field.FieldType == typeof(int))
            {
                value = (int)field.GetValue(instance);
                return true;
            }
        }

        PropertyInfo property = type.GetProperty(memberName, flags);
        if (property != null && property.CanRead)
        {
            if (property.PropertyType == typeof(float))
            {
                value = (float)property.GetValue(instance, null);
                return true;
            }

            if (property.PropertyType == typeof(int))
            {
                value = (int)property.GetValue(instance, null);
                return true;
            }
        }

        value = 0f;
        return false;
    }

    private void EnsureActivationAudioSource()
    {
        if (activationAudioSource == null)
            activationAudioSource = GetComponent<AudioSource>();
    }

    private void PlayActivatedSfx()
    {
        if (activatedSfx == null)
            return;

        if (usePlayClipAtPointForActivation)
        {
            AudioSource.PlayClipAtPoint(activatedSfx, transform.position, Mathf.Clamp01(activatedSfxVolume));
            return;
        }

        EnsureActivationAudioSource();

        if (activationAudioSource == null)
        {
            AudioSource.PlayClipAtPoint(activatedSfx, transform.position, Mathf.Clamp01(activatedSfxVolume));
            return;
        }

        activationAudioSource.spatialBlend = Mathf.Clamp01(activationSpatialBlend);
        activationAudioSource.minDistance = Mathf.Max(0.01f, activationMinDistance);
        activationAudioSource.maxDistance = Mathf.Max(activationAudioSource.minDistance, activationMaxDistance);
        activationAudioSource.PlayOneShot(activatedSfx, Mathf.Clamp01(activatedSfxVolume));
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
