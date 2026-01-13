using System;
using UnityEngine;
using UnityEngine.UI;

public class TeamIconUI : MonoBehaviour
{
    [Header("UI")]
    public Button button;

    [Tooltip("Drag the child Text (Legacy) that sits in the middle of the star.")]
    public Text label;

    private Team _team;
    private Action<Team> _onClick;

    private void Reset()
    {
        button = GetComponent<Button>();
        if (label == null)
            label = GetComponentInChildren<Text>(true);
    }

    public void Bind(Team team, Action<Team> onClick)
    {
        _team = team;
        _onClick = onClick;

        // Display the number of members on the team
        if (label != null && _team != null)
        {
            int count = (_team.Members != null) ? _team.Members.Count : 0;
            label.text = count.ToString();

            // Optional: hide if 0 or 1 (uncomment if you prefer)
            // label.gameObject.SetActive(count > 1);
        }

        if (button == null) button = GetComponent<Button>();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => _onClick?.Invoke(_team));
        }
    }
}
