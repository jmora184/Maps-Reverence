using UnityEngine;

/// <summary>
/// Attach this to your *scene-spawned* Enemy Team Icon UI prefab.
/// The spawner should call Bind(teamRootTransform) right after instantiating the icon.
///
/// This registers the icon with CommandOverlayUI so it supports:
/// - Hover preview in Move/Attack targeting (attack cursor + preview badge)
/// - Click commit (committed badge + submits follow/attack to the team anchor)
///
/// IMPORTANT:
/// Enemy team icons are often created before CommandOverlayUI exists/enables (eg. different scene load order).
/// So this script will keep retrying registration until it succeeds.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class EnemyTeamIconTargetingBridge : MonoBehaviour
{
    [Header("Binding")]
    [Tooltip("Persistent enemy team root/anchor Transform. Ideally the team parent object.")]
    public Transform enemyTeamAnchor;

    [Header("Optional")]
    public string hoverHintMessage = "Enemy Team";

    [Header("Registration Retry")]
    [Tooltip("How often (seconds) to retry finding/registering with CommandOverlayUI until successful.")]
    public float retryInterval = 0.25f;

    private RectTransform _rect;
    private CommandOverlayUI _overlay;
    private bool _registered;
    private float _nextTryTime;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        // Re-try on enable (icons sometimes toggle active with command camera).
        _nextTryTime = 0f;
        TryRegister(force: true);
    }

    private void Start()
    {
        // In case OnEnable happened before bindings were set.
        _nextTryTime = 0f;
        TryRegister(force: true);
    }

    private void Update()
    {
        if (_registered)
        {
            // Overlay could be destroyed/reloaded; if so, allow re-registering.
            if (_overlay == null)
                _registered = false;

            return;
        }

        if (enemyTeamAnchor == null)
            return;

        if (retryInterval <= 0f)
            retryInterval = 0.25f;

        if (Time.unscaledTime < _nextTryTime)
            return;

        _nextTryTime = Time.unscaledTime + retryInterval;
        TryRegister(force: true);
    }

    /// <summary>
    /// Call this from your enemy team spawner immediately after creating the team root and icon.
    /// Example:
    ///   var icon = Instantiate(enemyTeamIconPrefab, uiParent);
    ///   icon.GetComponent&lt;EnemyTeamIconTargetingBridge&gt;().Bind(teamRoot.transform);
    /// </summary>
    public void Bind(Transform teamAnchor)
    {
        enemyTeamAnchor = teamAnchor;
        _registered = false;
        _nextTryTime = 0f;
        TryRegister(force: true);
    }

    private void TryRegister(bool force = false)
    {
        if (!force && _registered) return;
        if (enemyTeamAnchor == null) return;

        if (_rect == null)
            _rect = GetComponent<RectTransform>();

        if (_overlay == null)
        {
#if UNITY_2023_1_OR_NEWER
            // Include inactive because CommandOverlayUI may start disabled in FPS mode.
            var overlays = Object.FindObjectsByType<CommandOverlayUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (overlays != null && overlays.Length > 0)
                _overlay = overlays[0];
#else
            _overlay = Object.FindObjectOfType<CommandOverlayUI>(true);
#endif
        }

        if (_overlay == null) return;

        // Register (safe to call multiple times; CommandOverlayUI removes duplicate triggers).
        _overlay.RegisterEnemyTeamIcon(enemyTeamAnchor, _rect, hoverHintMessage);

        _registered = true;
    }
}
