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

    private bool playerInRange = false;

    private void Start()
    {
        if (hideUIAtStart && endGameUI != null)
            endGameUI.SetActive(false);
    }

    private void OnEnable()
    {
        RefreshUI();
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
        endGameUI.SetActive(playerInRange && shuttleUnlocked);
    }
}
