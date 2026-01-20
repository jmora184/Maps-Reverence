using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent HUD attack indicator system.
///
/// Key change vs older versions:
/// - Badges are NEVER parented under (or attached as children of) EnemyIcon(Clone).
///   Many projects destroy/rebuild command icons when exiting command mode, which destroys children.
/// - Instead, badges always live under persistentRoot (HUD) and are POSITIONED to match:
///     * Enemy icon position + badgeOffset (when icon exists/active)
///     * Enemy world position (world-follow) when icon is missing (FPS / icons hidden)
///
/// Setup:
/// - Put this GameObject under a HUD canvas that stays enabled in FPS.
/// - Assign persistentRoot to a RectTransform under that HUD canvas (stretched full screen).
/// - Assign badgePrefab (RectTransform prefab with AttackTargetIndicatorBadgeUI).
/// - CommandOverlayUI should call RegisterEnemyIcon(enemyTransform, enemyIconRect) when building icons.
/// </summary>
public class AttackTargetIndicatorSystem : MonoBehaviour
{
    public static AttackTargetIndicatorSystem Instance { get; private set; }

    [Header("Prefabs")]
    public RectTransform badgePrefab;

    [Header("Placement")]
    [Tooltip("Offset (pixels) from the enemy icon position (screen space).")]
    public Vector2 badgeOffset = new Vector2(22f, 0f);

    [Tooltip("Offset (pixels) applied when world-following (FPS / no icon).")]
    public Vector2 worldFollowScreenOffset = new Vector2(22f, 0f);

    // Track offset changes so tweaking inspector in Play Mode updates existing badges.
    private Vector2 _lastAppliedOffset;

    [Header("Behavior")]
    [Tooltip("If true, preview badge shows selection count while hovering an enemy during MoveTargeting.")]
    public bool enablePreview = true;

    [Tooltip("If true, badges are only rendered while in Command Mode (prevents lingering badges in FPS mode).")]
    public bool onlyShowInCommandMode = true;

    [Tooltip("If true, when an enemy icon is not available the badge follows enemy world position on the HUD.")]
    public bool worldFollowWhenNoIcon = true;

    [Header("Persistent HUD")]
    [Tooltip("A RectTransform under a HUD canvas that stays enabled in FPS. Badges always live here.")]
    public RectTransform persistentRoot;

    [Tooltip("Camera used for WorldToScreenPoint when following in FPS (set this to your FPS/Main Camera).")]
    public Camera worldCamera;

    // Enemy -> icon rect (command mode). Icons may be destroyed/rebuilt; that's OK.
    private readonly Dictionary<Transform, RectTransform> iconByEnemy = new();

    // Enemy -> badge
    private readonly Dictionary<Transform, AttackTargetIndicatorBadgeUI> badgeByEnemy = new();

    // Enemy -> committed attackers
    private readonly Dictionary<Transform, HashSet<Transform>> attackersByEnemy = new();

    // Attacker -> current enemy (for retargeting)
    private readonly Dictionary<Transform, Transform> enemyByAttacker = new();

    private Transform previewEnemy;
    private int previewCount;

    private Canvas _hudCanvas;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _lastAppliedOffset = badgeOffset;

        if (worldCamera == null)
            worldCamera = Camera.main;

        CacheHudCanvas();
    }

    private void OnEnable()
    {
        CacheHudCanvas();
    }

    private void CacheHudCanvas()
    {
        if (persistentRoot != null)
            _hudCanvas = persistentRoot.GetComponentInParent<Canvas>();
        else
            _hudCanvas = GetComponentInParent<Canvas>();
    }

    private void OnValidate()
    {
        if (_lastAppliedOffset != badgeOffset)
        {
            _lastAppliedOffset = badgeOffset;
            // No parenting to update; LateUpdate uses badgeOffset each frame.
        }
    }

    /// <summary>Called by CommandOverlayUI when an enemy icon is (re)created.</summary>
    public void RegisterEnemyIcon(Transform enemy, RectTransform icon)
    {
        if (enemy == null || icon == null) return;
        iconByEnemy[enemy] = icon;

        // Ensure badge exists if enemy is currently targeted.
        RefreshEnemy(enemy);
    }

    /// <summary>Call when icon is destroyed/hidden (optional). It's OK if you don't call this.</summary>
    public void UnregisterEnemyIcon(Transform enemy)
    {
        if (enemy == null) return;
        iconByEnemy.Remove(enemy);
    }


    /// <summary>Fully removes all tracking + destroys any badge for this enemy.</summary>
    public void UnregisterEnemy(Transform enemy)
    {
        if (enemy == null) return;

        iconByEnemy.Remove(enemy);

        if (badgeByEnemy.TryGetValue(enemy, out var badge) && badge != null)
            Destroy(badge.gameObject);
        badgeByEnemy.Remove(enemy);

        attackersByEnemy.Remove(enemy);

        if (previewEnemy == enemy)
        {
            previewEnemy = null;
            previewCount = 0;
        }

        // Remove any attacker mappings pointing to this enemy.
        List<Transform> attackers = null;
        foreach (var pair in enemyByAttacker)
        {
            if (pair.Key == null) continue;
            if (pair.Value == enemy)
            {
                attackers ??= new List<Transform>();
                attackers.Add(pair.Key);
            }
        }
        if (attackers != null)
        {
            for (int i = 0; i < attackers.Count; i++)
                enemyByAttacker.Remove(attackers[i]);
        }
    }

    public void SetPreview(Transform enemy, int count)
    {
        if (!enablePreview) return;
        previewEnemy = enemy;
        previewCount = Mathf.Max(0, count);
        RefreshEnemy(enemy);
    }

    public void SetPreview(Transform enemy) => SetPreview(enemy, 1);

    public void ClearPreview(Transform enemy)
    {
        if (previewEnemy == enemy)
        {
            previewEnemy = null;
            previewCount = 0;
        }
        RefreshEnemy(enemy);
    }

    public void ClearAllPreview()
    {
        var prev = previewEnemy;
        previewEnemy = null;
        previewCount = 0;
        if (prev != null) RefreshEnemy(prev);
    }

    public void RegisterCommittedAttack(IReadOnlyList<GameObject> attackers, Transform enemy)
    {
        if (enemy == null || attackers == null || attackers.Count == 0) return;
        var arr = new GameObject[attackers.Count];
        for (int i = 0; i < attackers.Count; i++) arr[i] = attackers[i];
        RegisterCommittedAttack(arr, enemy);
    }

    public void RegisterCommittedAttack(GameObject[] attackers, Transform enemy)
    {
        if (enemy == null || attackers == null || attackers.Length == 0) return;

        for (int i = 0; i < attackers.Length; i++)
        {
            var go = attackers[i];
            if (go == null) continue;
            var attacker = go.transform;
            if (attacker == null) continue;

            // Remove from old enemy if needed
            if (enemyByAttacker.TryGetValue(attacker, out var oldEnemy) && oldEnemy != null && oldEnemy != enemy)
            {
                if (attackersByEnemy.TryGetValue(oldEnemy, out var oldSet) && oldSet != null)
                {
                    oldSet.Remove(attacker);
                    if (oldSet.Count == 0) attackersByEnemy.Remove(oldEnemy);
                }
                RefreshEnemy(oldEnemy);
            }

            enemyByAttacker[attacker] = enemy;

            if (!attackersByEnemy.TryGetValue(enemy, out var set) || set == null)
            {
                set = new HashSet<Transform>();
                attackersByEnemy[enemy] = set;
            }
            set.Add(attacker);
        }

        RefreshEnemy(enemy);
    }

    public void UnregisterAttacker(Transform attacker)
    {
        if (attacker == null) return;

        if (enemyByAttacker.TryGetValue(attacker, out var enemy) && enemy != null)
        {
            enemyByAttacker.Remove(attacker);

            if (attackersByEnemy.TryGetValue(enemy, out var set) && set != null)
            {
                set.Remove(attacker);
                if (set.Count == 0) attackersByEnemy.Remove(enemy);
            }

            RefreshEnemy(enemy);
        }
    }

    public void UnregisterAttackers(IReadOnlyList<GameObject> attackers)
    {
        if (attackers == null || attackers.Count == 0) return;
        for (int i = 0; i < attackers.Count; i++)
        {
            var go = attackers[i];
            if (go == null) continue;
            UnregisterAttacker(go.transform);
        }
    }

    public void UnregisterAttackers(GameObject[] attackers)
    {
        if (attackers == null || attackers.Length == 0) return;
        for (int i = 0; i < attackers.Length; i++)
        {
            var go = attackers[i];
            if (go == null) continue;
            UnregisterAttacker(go.transform);
        }
    }

    private void RefreshEnemy(Transform enemy)
    {
        if (enemy == null) return;

        int committedCount = 0;
        if (attackersByEnemy.TryGetValue(enemy, out var set) && set != null)
            committedCount = set.Count;

        bool isPreviewEnemy = (enablePreview && previewEnemy == enemy && previewCount > 0);

        int shownCount = committedCount;
        bool previewOnly = false;

        if (committedCount <= 0 && isPreviewEnemy)
        {
            shownCount = previewCount;
            previewOnly = true;
        }

        if (shownCount <= 0)
        {
            if (badgeByEnemy.TryGetValue(enemy, out var b) && b != null)
                b.SetCount(0);
            return;
        }

        var badge = GetOrCreateBadge(enemy);
        if (badge == null) return;

        badge.SetPreview(previewOnly);
        badge.SetCount(shownCount);
    }

    private AttackTargetIndicatorBadgeUI GetOrCreateBadge(Transform enemy)
    {
        if (enemy == null) return null;

        if (badgeByEnemy.TryGetValue(enemy, out var existing) && existing != null)
            return existing;

        if (badgePrefab == null || persistentRoot == null) return null;

        var rt = Instantiate(badgePrefab, persistentRoot);
        rt.gameObject.SetActive(true);

        // Normalize transform
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;

        var badge = rt.GetComponent<AttackTargetIndicatorBadgeUI>();
        if (badge == null) badge = rt.gameObject.AddComponent<AttackTargetIndicatorBadgeUI>();

        badgeByEnemy[enemy] = badge;
        return badge;
    }

    private bool IsCommandMode()
    {
        if (CommandCamToggle.Instance != null)
            return CommandCamToggle.Instance.IsCommandMode;

        if (CommandOverlayUI.Instance != null && CommandOverlayUI.Instance.commandCam != null)
            return CommandOverlayUI.Instance.commandCam.enabled;

        return true;
    }

    private void LateUpdate()
    {
        if (persistentRoot == null || badgePrefab == null) return;

        if (worldCamera == null) worldCamera = Camera.main;
        CacheHudCanvas();

        bool commandMode = !onlyShowInCommandMode || IsCommandMode();

        // Cleanup dead enemies + bad badges up-front (prevents "badge at center" artifacts).
        List<Transform> enemiesToRemove = null;
        foreach (var kvp in badgeByEnemy)
        {
            if (kvp.Key == null || kvp.Value == null)
            {
                enemiesToRemove ??= new List<Transform>();
                enemiesToRemove.Add(kvp.Key);
            }
        }
        if (enemiesToRemove != null)
        {
            for (int i = 0; i < enemiesToRemove.Count; i++)
            {
                var enemy = enemiesToRemove[i];
                if (enemy != null)
                    UnregisterEnemy(enemy);
                else
                {
                    // Remove null-key entries (rare but possible)
                    badgeByEnemy.Remove(enemy);
                    iconByEnemy.Remove(enemy);
                    attackersByEnemy.Remove(enemy);
                }
            }
        }

        // If we don't want badges in FPS, hard-hide them while not in command mode.
        if (!commandMode)
        {
            foreach (var kvp in badgeByEnemy)
            {
                var badge = kvp.Value;
                if (badge == null) continue;
                if (badge.gameObject.activeSelf)
                    badge.gameObject.SetActive(false);
            }
            return;
        }

        // Update badge positions every frame so they can survive icon rebuilds/hides.
        foreach (var kvp in badgeByEnemy)
        {
            var enemy = kvp.Key;
            var badge = kvp.Value;
            if (enemy == null || badge == null) continue;

            var rt = badge.transform as RectTransform;
            if (rt == null) continue;

            // If badge should be visible (count>0) but was force-hidden (FPS), re-enable it.
            if (!badge.gameObject.activeSelf && badge.CurrentCount > 0)
                badge.gameObject.SetActive(true);

            // If badge UI decided to hide (count=0), skip positioning.
            if (!badge.gameObject.activeInHierarchy) continue;

            // Prefer icon positioning if the icon exists and is active.
            if (iconByEnemy.TryGetValue(enemy, out var icon) && icon != null && icon.gameObject.activeInHierarchy)
            {
                Vector3 screen = GetIconScreenPoint(icon);
                screen.x += badgeOffset.x;
                screen.y += badgeOffset.y;
                SetBadgeScreenPosition(rt, screen);
                continue;
            }

            // Otherwise, world follow.
            if (worldFollowWhenNoIcon && worldCamera != null)
            {
                var screen = worldCamera.WorldToScreenPoint(enemy.position);
                if (screen.z <= 0f) continue;

                screen.x += worldFollowScreenOffset.x;
                screen.y += worldFollowScreenOffset.y;
                SetBadgeScreenPosition(rt, screen);
            }
        }

        // Cleanup null attackers
        List<Transform> attackersToRemove = null;
        foreach (var pair in enemyByAttacker)
        {
            if (pair.Key == null || pair.Value == null)
            {
                attackersToRemove ??= new List<Transform>();
                attackersToRemove.Add(pair.Key);
            }
        }
        if (attackersToRemove != null)
        {
            for (int i = 0; i < attackersToRemove.Count; i++)
                UnregisterAttacker(attackersToRemove[i]);
        }

        if (previewEnemy == null)
            previewCount = 0;
    }

    private Vector3 GetIconScreenPoint(RectTransform icon)
    {
        if (icon == null) return Vector3.zero;

        var iconCanvas = icon.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (iconCanvas != null && iconCanvas.renderMode == RenderMode.ScreenSpaceCamera)
            cam = iconCanvas.worldCamera;

        // For ScreenSpaceOverlay, cam is null (correct).
        return RectTransformUtility.WorldToScreenPoint(cam, icon.position);
    }

    private void SetBadgeScreenPosition(RectTransform rt, Vector3 screen)
    {
        if (rt == null || persistentRoot == null) return;

        // Overlay canvas: position directly
        if (_hudCanvas != null && _hudCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            rt.position = screen;
            return;
        }

        // Camera/World canvas: convert screen to local anchoredPosition
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            persistentRoot,
            screen,
            (_hudCanvas != null && _hudCanvas.renderMode == RenderMode.ScreenSpaceCamera) ? _hudCanvas.worldCamera : null,
            out localPoint);

        rt.anchoredPosition = localPoint;
    }
}
