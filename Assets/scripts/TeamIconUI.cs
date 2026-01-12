using System;
using UnityEngine;
using UnityEngine.UI;

public class TeamIconUI : MonoBehaviour
{
    [Header("UI")]
    public Button button;
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

        if (label != null && _team != null)
            label.text = _team.GetDisplayLabel();

        if (button == null) button = GetComponent<Button>();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => _onClick?.Invoke(_team));
        }
    }
}
