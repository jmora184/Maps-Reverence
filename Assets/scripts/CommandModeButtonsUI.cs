using UnityEngine;
using UnityEngine.UI;

public class CommandModeButtonsUI : MonoBehaviour
{
    [Header("References")]
    public CommandStateMachine sm;

    [Header("Buttons")]
    public Button moveButton;
    public Button joinButton;
    public Button cancelButton;

    [Header("Optional: Panel Root")]
    public GameObject root; // set to the button panel GameObject if you want hide/show

    private void Awake()
    {
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();

        if (moveButton != null)
            moveButton.onClick.AddListener(OnMoveClicked);

        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinClicked);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void OnMoveClicked()
    {
        if (sm == null) return;
        sm.ArmMoveFromCurrentSelection();
    }

    private void OnJoinClicked()
    {
        if (sm == null) return;
        sm.ArmJoinFromCurrentSelection();
    }

    private void OnCancelClicked()
    {
        if (sm == null) return;

        // If currently in AddTargeting (join targeting), cancel join targeting.
        // Otherwise clear selection / return to await selection.
        sm.CancelJoin();
        sm.ClearSelection();
    }

    /// <summary>
    /// Called by CommandStateMachine/others to update interactivity/visibility.
    /// Keep it simple: show when we are in command mode and something is selected.
    /// </summary>
    public void Refresh(CommandStateMachine.State state, int selectionCount)
    {
        // If you didn't wire a root, do nothing
        if (root != null)
        {
            // Show panel only when something is selected or we're targeting.
            bool show =
                state == CommandStateMachine.State.UnitSelected ||
                state == CommandStateMachine.State.MoveTargeting ||
                state == CommandStateMachine.State.AddTargeting;

            root.SetActive(show);
        }

        if (moveButton != null)
            moveButton.interactable = selectionCount > 0;

        if (joinButton != null)
            joinButton.interactable = selectionCount > 0;

        if (cancelButton != null)
            cancelButton.interactable = true;
    }
}
