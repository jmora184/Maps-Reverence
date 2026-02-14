using System;
using UnityEngine;
using UnityEngine.UI;

public class TeamIconUI : MonoBehaviour
{
    [Header("UI")]
    public Button button;

    [Tooltip("Drag the child Text (Legacy) that sits in the middle of the star.")]
    public Text label;

    [Tooltip("Optional: the RectTransform (or any Transform) you want to scale. If left null, this GameObject is scaled.")]
    public RectTransform scaleTarget;

    [Header("Team Size Scaling (Ally Team Icon)")]
    [Tooltip("If enabled, the icon scales up as team size grows.")]
    public bool scaleWithTeamSize = true;

    [Tooltip("Scale when the team has 1 member.")]
    public float baseScale = 1.0f;

    [Tooltip("Additional scale growth factor. Used by the selected growth mode.")]
    public float growth = 0.12f;

    [Tooltip("Minimum allowed scale.")]
    public float minScale = 0.9f;

    [Tooltip("Maximum allowed scale.")]
    public float maxScale = 1.6f;

    public enum GrowthMode
    {
        LinearClamped,
        SqrtClamped
    }

    [Tooltip("How the scale grows with team size. Sqrt feels smooth for larger teams.")]
    public GrowthMode growthMode = GrowthMode.SqrtClamped;

    private Team _team;

    public Team Team => _team;
    private Action<Team> _onClick;

    private void Reset()
    {
        button = GetComponent<Button>();
        if (label == null)
            label = GetComponentInChildren<Text>(true);

        if (scaleTarget == null)
            scaleTarget = GetComponent<RectTransform>();
    }

    public void Bind(Team team, Action<Team> onClick)
    {
        _team = team;
        _onClick = onClick;

        RefreshVisuals();

        if (button == null) button = GetComponent<Button>();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => _onClick?.Invoke(_team));
        }
    }

    /// <summary>
    /// Call this if the team member count changes and you want the icon to update
    /// without rebinding (optional but handy).
    /// </summary>
    public void RefreshVisuals()
    {
        if (_team == null)
            return;

        int count = (_team.Members != null) ? _team.Members.Count : 0;

        // Display the number of members on the team
        if (label != null)
        {
            label.text = count.ToString();

            // Optional: hide if 0 or 1 (uncomment if you prefer)
            // label.gameObject.SetActive(count > 1);
        }

        // Scale icon by team size
        if (scaleWithTeamSize)
        {
            float s = ComputeScale(count);

            // Scale target (preferred) or this GameObject
            Transform t = (scaleTarget != null) ? (Transform)scaleTarget : transform;
            t.localScale = Vector3.one * s;
        }
    }

    private float ComputeScale(int count)
    {
        // Ensure sane count
        count = Mathf.Max(0, count);

        float s = baseScale;

        // Treat 1 member as baseline
        int n = Mathf.Max(1, count);

        switch (growthMode)
        {
            case GrowthMode.LinearClamped:
                // Example: baseScale + (n-1) * growth
                s = baseScale + (n - 1) * growth;
                break;

            case GrowthMode.SqrtClamped:
            default:
                // Smooth growth: baseScale + growth * sqrt(n-1)
                s = baseScale + growth * Mathf.Sqrt(n - 1);
                break;
        }

        // Clamp to safe bounds
        if (maxScale < minScale)
            maxScale = minScale;

        s = Mathf.Clamp(s, minScale, maxScale);
        return s;
    }
}
