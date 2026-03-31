using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Activates an existing mech object and optional boss health bar after the player
/// destroys a certain number of enemies.
///
/// Simple setup:
/// - Put this on any always-active scene object.
/// - Assign your EnemyDestroyTracker (or leave blank to use EnemyDestroyTracker.Instance).
/// - Assign a disabled mech GameObject.
/// - Assign a disabled boss health bar root GameObject (optional).
/// - Optional: assign a TMP popup text object to show the mech spawn message.
/// </summary>
public class MechSpawnAfterKills : MonoBehaviour
{
    [Header("Tracking")]
    [Tooltip("Optional explicit tracker reference. If left empty, uses EnemyDestroyTracker.Instance.")]
    public EnemyDestroyTracker destroyTracker;

    [Min(1)]
    [Tooltip("How many destroyed enemies are required before the mech appears.")]
    public int requiredDestroyedCount = 2;

    [Header("Spawn Targets")]
    [Tooltip("Existing mech GameObject in the scene. Usually disabled at start.")]
    public GameObject mechObject;

    [Tooltip("Optional existing boss health bar root on the Canvas. Usually disabled at start.")]
    public GameObject mechHealthBarObject;

    [Tooltip("Optional existing boss icon object under the MiniUI/Canvas. Usually disabled at start.")]
    public GameObject bossIconObject;

    [Tooltip("If true, force the mech off at startup until the kill threshold is reached.")]
    public bool disableMechAtStart = true;

    [Tooltip("If true, force the boss health bar off at startup until the kill threshold is reached.")]
    public bool disableHealthBarAtStart = true;

    [Tooltip("If true, force the boss icon off at startup until the kill threshold is reached.")]
    public bool disableBossIconAtStart = true;

    [Header("Spawn Popup")]
    [Tooltip("Optional TMP text object used as a temporary spawn popup.")]
    public TMP_Text spawnPopupText;

    [TextArea]
    public string spawnPopupMessage = "<color=#cc0000>General Hux Speaking:</color> All Units, attack the Human bases. Leave it to me to cut the head off this Army's last officer.";

    [Min(0f)]
    public float spawnPopupDuration = 4f;

    [Tooltip("If true, hide the popup object at startup.")]
    public bool hidePopupAtStart = true;

    [Header("Debug")]
    public bool debugLogs = false;

    private bool _hasSpawned;
    private Coroutine _popupRoutine;

    private void Awake()
    {
        if (disableMechAtStart && mechObject != null)
            mechObject.SetActive(false);

        if (disableHealthBarAtStart && mechHealthBarObject != null)
            mechHealthBarObject.SetActive(false);

        if (disableBossIconAtStart && bossIconObject != null)
            bossIconObject.SetActive(false);

        if (hidePopupAtStart && spawnPopupText != null)
            spawnPopupText.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_hasSpawned)
            return;

        EnemyDestroyTracker tracker = GetTracker();
        if (tracker == null)
            return;

        if (tracker.CurrentDestroyedCount >= requiredDestroyedCount)
            SpawnMech();
    }

    private EnemyDestroyTracker GetTracker()
    {
        if (destroyTracker != null)
            return destroyTracker;

        destroyTracker = EnemyDestroyTracker.Instance;
        return destroyTracker;
    }

    private void SpawnMech()
    {
        _hasSpawned = true;

        if (mechObject != null)
            mechObject.SetActive(true);

        if (mechHealthBarObject != null)
            mechHealthBarObject.SetActive(true);

        if (bossIconObject != null)
            bossIconObject.SetActive(true);

        ShowSpawnPopup();

        if (debugLogs)
            Debug.Log($"[MechSpawnAfterKills] Spawned mech after {requiredDestroyedCount} destroyed enemies.", this);
    }

    private void ShowSpawnPopup()
    {
        if (spawnPopupText == null)
            return;

        spawnPopupText.text = spawnPopupMessage;
        spawnPopupText.gameObject.SetActive(true);

        if (_popupRoutine != null)
            StopCoroutine(_popupRoutine);

        if (spawnPopupDuration > 0f)
            _popupRoutine = StartCoroutine(HidePopupAfterDelay(spawnPopupDuration));
    }

    private IEnumerator HidePopupAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (spawnPopupText != null)
            spawnPopupText.gameObject.SetActive(false);

        _popupRoutine = null;
    }

    public void ForceSpawnNow()
    {
        if (_hasSpawned)
            return;

        SpawnMech();
    }

    public void ResetSpawner(bool hideTargetsAgain = true)
    {
        _hasSpawned = false;

        if (_popupRoutine != null)
        {
            StopCoroutine(_popupRoutine);
            _popupRoutine = null;
        }

        if (hideTargetsAgain)
        {
            if (mechObject != null)
                mechObject.SetActive(false);

            if (mechHealthBarObject != null)
                mechHealthBarObject.SetActive(false);

            if (bossIconObject != null)
                bossIconObject.SetActive(false);

            if (spawnPopupText != null)
                spawnPopupText.gameObject.SetActive(false);
        }
    }
}
