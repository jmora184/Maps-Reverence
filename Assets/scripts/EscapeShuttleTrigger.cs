using UnityEngine;

public class EscapeShuttleTrigger : MonoBehaviour
{
    [Header("References")]
    [Tooltip("UI object to show when the player can board the shuttle. Usually a Canvas panel, button, or TMP parent object.")]
    public GameObject endGameUI;

    [Header("Player Detection")]
    [Tooltip("Required tag for the player entering the trigger.")]
    public string playerTag = "Player";

    [Header("Startup")]
    [Tooltip("Hide the UI at startup.")]
    public bool hideUIAtStart = true;

    [Header("Cursor")]
    [Tooltip("Unlock and show the mouse cursor while the end game UI is visible.")]
    public bool unlockCursorWhenUIVisible = true;

    private bool playerInRange = false;

    private void Start()
    {
        if (hideUIAtStart && endGameUI != null)
            endGameUI.SetActive(false);

        ApplyCursorState();
    }

    private void OnEnable()
    {
        RefreshUI();
    }

    private void OnDisable()
    {
        if (unlockCursorWhenUIVisible)
            SetCursorLocked(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        playerInRange = true;
        RefreshUI();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        playerInRange = false;
        RefreshUI();
    }

    private void Update()
    {
        // Keeps the UI in sync in case the shuttle unlocks while the player
        // is already standing inside the trigger.
        if (playerInRange)
            RefreshUI();
    }

    private void RefreshUI()
    {
        if (endGameUI == null) return;

        bool shuttleUnlocked = EnemyDestroyTracker.Instance != null && EnemyDestroyTracker.Instance.ShuttleUnlocked;
        bool shouldShow = playerInRange && shuttleUnlocked;

        if (endGameUI.activeSelf != shouldShow)
            endGameUI.SetActive(shouldShow);

        ApplyCursorState();
    }

    private void ApplyCursorState()
    {
        if (!unlockCursorWhenUIVisible) return;

        bool uiVisible = endGameUI != null && endGameUI.activeInHierarchy;
        SetCursorLocked(!uiVisible);
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
