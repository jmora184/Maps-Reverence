using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class EnemyDestroyTracker : MonoBehaviour
{
    public static EnemyDestroyTracker Instance;

    [Header("Goal")]
    [Min(1)] public int requiredDestroyedCount = 50;

    [Header("Runtime")]
    [SerializeField] private int currentDestroyedCount = 0;
    [SerializeField] private bool shuttleUnlocked = false;

    [Header("Tracking")]
    [Tooltip("When enabled, the tracker automatically watches all active objects tagged Enemy in the scene. If one disappears or is deactivated, it counts it. This means you do NOT have to add EnemyDestroyReporter to every enemy.")]
    public bool useSceneScanTracking = true;
    [Tooltip("How often the scene scan checks for Enemy-tagged objects.")]
    [Min(0.05f)] public float scanInterval = 0.25f;
    [Tooltip("Optional extra logs to help debug counting.")]
    public bool debugLogs = true;

    [Header("Shuttle")]
    [Tooltip("This can be the shuttle root, a trigger root, or whatever should become active when the threshold is reached.")]
    public GameObject escapeShuttleObject;
    [Tooltip("If checked, the assigned shuttle object is forced off at startup until the threshold is reached.")]
    public bool disableShuttleAtStart = true;

    [Header("Command Mode Icon")]
    [Tooltip("Optional command mode shuttle icon to reveal when the threshold is reached.")]
    public GameObject commandModeShuttleIcon;
    [Tooltip("If checked, the assigned command mode icon is forced off at startup until the threshold is reached.")]
    public bool disableCommandModeIconAtStart = true;

    [Header("Optional UI")]
    [Tooltip("Optional running counter text, e.g. 'Enemies Destroyed: 12/50'.")]
    public TMP_Text counterText;

    [Header("Optional Unlock Popup")]
    [Tooltip("Assign your TMP object here. The tracker will turn it on when the shuttle unlocks, then hide it again.")]
    public TMP_Text unlockPopupText;
    [TextArea] public string unlockPopupMessage = "Escape Shuttle Available!";
    [Min(0f)] public float unlockPopupDuration = 5f;
    public bool hidePopupAtStart = true;

    public int CurrentDestroyedCount => currentDestroyedCount;
    public bool ShuttleUnlocked => shuttleUnlocked;

    private readonly HashSet<int> presentEnemyIds = new HashSet<int>();
    private readonly HashSet<int> countedEnemyIds = new HashSet<int>();
    private readonly Dictionary<int, string> enemyNamesById = new Dictionary<int, string>();

    private Coroutine popupRoutine;
    private Coroutine scanRoutine;
    private bool warnedMissingEnemyTag;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (disableShuttleAtStart && escapeShuttleObject != null)
            escapeShuttleObject.SetActive(false);

        if (disableCommandModeIconAtStart && commandModeShuttleIcon != null)
            commandModeShuttleIcon.SetActive(false);

        if (hidePopupAtStart && unlockPopupText != null)
            unlockPopupText.gameObject.SetActive(false);

        UpdateCounterUI();
    }

    private void Start()
    {
        InitializeSceneScanSnapshot();

        if (useSceneScanTracking)
            scanRoutine = StartCoroutine(SceneScanLoop());
    }

    private void OnEnable()
    {
        if (Application.isPlaying && useSceneScanTracking && scanRoutine == null)
            scanRoutine = StartCoroutine(SceneScanLoop());
    }

    private void OnDisable()
    {
        if (scanRoutine != null)
        {
            StopCoroutine(scanRoutine);
            scanRoutine = null;
        }
    }

    public void RegisterEnemyDestroyed(GameObject destroyedEnemy)
    {
        int instanceId = destroyedEnemy != null ? destroyedEnemy.GetInstanceID() : 0;
        string enemyName = destroyedEnemy != null ? destroyedEnemy.name : "Unknown Enemy";
        RegisterEnemyDestroyedInternal(instanceId, enemyName, "Reporter");
    }

    public void RegisterEnemyDestroyed()
    {
        RegisterEnemyDestroyedInternal(0, "Unknown Enemy", "Reporter");
    }

    public void ResetCounter(bool relockShuttle = true)
    {
        currentDestroyedCount = 0;
        shuttleUnlocked = false;
        countedEnemyIds.Clear();
        enemyNamesById.Clear();
        presentEnemyIds.Clear();
        InitializeSceneScanSnapshot();
        UpdateCounterUI();

        if (relockShuttle && escapeShuttleObject != null)
            escapeShuttleObject.SetActive(false);

        if (relockShuttle && commandModeShuttleIcon != null)
            commandModeShuttleIcon.SetActive(false);

        if (unlockPopupText != null)
            unlockPopupText.gameObject.SetActive(false);
    }

    private IEnumerator SceneScanLoop()
    {
        var wait = new WaitForSeconds(scanInterval);

        while (true)
        {
            ScanForRemovedEnemies();
            yield return wait;
        }
    }

    private void InitializeSceneScanSnapshot()
    {
        presentEnemyIds.Clear();

        if (!TryGetActiveEnemyObjects(out GameObject[] enemies))
            return;

        for (int i = 0; i < enemies.Length; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null) continue;

            int id = enemy.GetInstanceID();
            presentEnemyIds.Add(id);
            enemyNamesById[id] = enemy.name;
        }

        if (debugLogs)
            Debug.Log($"[EnemyDestroyTracker] Initial scene scan found {presentEnemyIds.Count} active Enemy-tagged objects.", this);
    }

    private void ScanForRemovedEnemies()
    {
        if (!TryGetActiveEnemyObjects(out GameObject[] enemies))
            return;

        HashSet<int> currentIds = new HashSet<int>();

        for (int i = 0; i < enemies.Length; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null) continue;

            int id = enemy.GetInstanceID();
            currentIds.Add(id);

            if (!enemyNamesById.ContainsKey(id))
                enemyNamesById[id] = enemy.name;
        }

        if (presentEnemyIds.Count > 0)
        {
            List<int> removedIds = null;

            foreach (int previousId in presentEnemyIds)
            {
                if (!currentIds.Contains(previousId))
                {
                    removedIds ??= new List<int>();
                    removedIds.Add(previousId);
                }
            }

            if (removedIds != null)
            {
                for (int i = 0; i < removedIds.Count; i++)
                {
                    int removedId = removedIds[i];
                    string enemyName = enemyNamesById.TryGetValue(removedId, out string storedName) ? storedName : $"Enemy #{removedId}";
                    RegisterEnemyDestroyedInternal(removedId, enemyName, "SceneScan");
                }
            }
        }

        presentEnemyIds.Clear();
        foreach (int id in currentIds)
            presentEnemyIds.Add(id);
    }

    private bool TryGetActiveEnemyObjects(out GameObject[] enemies)
    {
        enemies = null;

        try
        {
            enemies = GameObject.FindGameObjectsWithTag("Enemy");
            warnedMissingEnemyTag = false;
            return true;
        }
        catch (UnityException)
        {
            if (!warnedMissingEnemyTag)
            {
                warnedMissingEnemyTag = true;
                Debug.LogWarning("[EnemyDestroyTracker] Tag 'Enemy' does not exist in the project yet. Create it first in Unity Tags and Layers.", this);
            }
            return false;
        }
    }

    private void RegisterEnemyDestroyedInternal(int instanceId, string enemyName, string source)
    {
        if (!Application.isPlaying)
            return;

        if (instanceId != 0)
        {
            if (countedEnemyIds.Contains(instanceId))
                return;

            countedEnemyIds.Add(instanceId);
            enemyNamesById[instanceId] = enemyName;
        }

        currentDestroyedCount++;
        UpdateCounterUI();

        if (debugLogs)
            Debug.Log($"[EnemyDestroyTracker] Counted removed enemy via {source}: {enemyName} | Total {currentDestroyedCount}/{requiredDestroyedCount}", this);

        if (!shuttleUnlocked && currentDestroyedCount >= requiredDestroyedCount)
        {
            shuttleUnlocked = true;
            UnlockEscapeShuttle();
        }
    }

    private void UnlockEscapeShuttle()
    {
        Debug.Log("Escape shuttle unlocked.", this);

        if (escapeShuttleObject != null)
            escapeShuttleObject.SetActive(true);

        if (commandModeShuttleIcon != null)
            commandModeShuttleIcon.SetActive(true);

        ShowUnlockPopup();
    }

    private void UpdateCounterUI()
    {
        if (counterText != null)
            counterText.text = $"Enemies Destroyed: {currentDestroyedCount}/{requiredDestroyedCount}";
    }

    private void ShowUnlockPopup()
    {
        if (unlockPopupText == null)
            return;

        unlockPopupText.text = unlockPopupMessage;
        unlockPopupText.gameObject.SetActive(true);

        if (popupRoutine != null)
            StopCoroutine(popupRoutine);

        if (unlockPopupDuration > 0f)
            popupRoutine = StartCoroutine(HidePopupAfterDelay(unlockPopupDuration));
    }

    private IEnumerator HidePopupAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (unlockPopupText != null)
            unlockPopupText.gameObject.SetActive(false);

        popupRoutine = null;
    }
}
