using TMPro;
using UnityEngine;

/// <summary>
/// Displays the current number of allies following the player via PlayerSquadFollowSystem.
/// Attach this to a Canvas object (for example your FollowerCount root),
/// then assign the TMP_Text you want to update.
/// </summary>
[DisallowMultipleComponent]
public class PlayerFollowerCountUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text countText;
    [SerializeField] private string prefix = "x ";
    [SerializeField] private string suffix = "";

    [Header("Refresh")]
    [SerializeField] private float refreshInterval = 0.1f;

    [Header("Optional Visibility")]
    [SerializeField] private bool hideRootWhenZero = false;
    [SerializeField] private GameObject rootToShowHide;

    private float nextRefreshTime;
    private int lastShownCount = int.MinValue;

    private void Awake()
    {
        if (countText == null)
            countText = GetComponentInChildren<TMP_Text>(true);

        if (rootToShowHide == null)
            rootToShowHide = gameObject;
    }

    private void OnEnable()
    {
        RefreshNow(force: true);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRefreshTime)
            RefreshNow(force: false);
    }

    public void RefreshNow(bool force = true)
    {
        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);

        int followerCount = 0;
        if (PlayerSquadFollowSystem.Instance != null)
            followerCount = Mathf.Max(0, PlayerSquadFollowSystem.Instance.FollowerCount);

        if (!force && followerCount == lastShownCount)
            return;

        lastShownCount = followerCount;

        if (countText != null)
            countText.text = prefix + followerCount + suffix;

        if (hideRootWhenZero && rootToShowHide != null)
            rootToShowHide.SetActive(followerCount > 0);
    }
}
