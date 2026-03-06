using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// SUPER SIMPLE enemy-team icon binder.
/// Attach this to EncounterDirector_CurrentLevel (or any manager object).
///
/// What it does:
/// - Finds Enemy team roots named like "EnemyTeam_#"
/// - For each team root, creates 1 UI icon (RectTransform prefab)
/// - Each frame, moves that UI icon to follow the team's anchor (EncounterTeamAnchor.AnchorWorldPosition)
///
/// This is read-only: no clicking, no control.
/// 
/// Setup:
/// 1) Add this component to: EncounterDirector_CurrentLevel
/// 2) Create a UI container under your Canvas: EnemyTeamIcons (RectTransform)
/// 3) Create a UI prefab for the enemy team icon (Image). Drag it into Project as a prefab.
/// 4) Assign:
///    - iconPrefab (RectTransform prefab)
///    - iconParent (EnemyTeamIcons)
///    - commandCamera (your CommandCamera)
///
/// Notes:
/// - Works best with Canvas Render Mode = Screen Space - Overlay.
/// - If you use Screen Space - Camera, it can still work; make sure the canvas is using the same camera.
/// </summary>
public class EncounterDirectorEnemyTeamIcons : MonoBehaviour
{
    [Header("UI")]
    public RectTransform iconPrefab;
    public RectTransform iconParent;

    [Header("Camera")]
    public Camera commandCamera;

    [Header("Team Discovery")]
    [Tooltip("Enemy team roots are typically named EnemyTeam_1, EnemyTeam_2, ...")]
    public string enemyTeamRootPrefix = "EnemyTeam_";

    [Tooltip("If set, we only scan under this transform for teams. If null, we scan the whole scene (POC).")]
    public Transform teamRootsParent;

    [Tooltip("How often to rescan for new teams (seconds).")]
    public float rescanInterval = 0.5f;

    [Header("Placement")]
    public Vector3 worldOffset = new Vector3(0f, 2f, 0f);
    public Vector2 screenOffsetPixels = Vector2.zero;

    [Header("Visibility")]
    [Tooltip("If true, hide icons unless the command camera is enabled+active.")]
    public bool onlyShowWhenCommandCameraEnabled = true;

    private float _nextScanTime;
    private readonly Dictionary<Transform, RectTransform> _iconByTeamRoot = new Dictionary<Transform, RectTransform>();
    private readonly Dictionary<Transform, TMP_Text> _flankTextByTeamRoot = new Dictionary<Transform, TMP_Text>();
    private readonly List<Transform> _teamsScratch = new List<Transform>(64);

    private void Awake()
    {
        if (commandCamera == null) commandCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (onlyShowWhenCommandCameraEnabled && (commandCamera == null || !commandCamera.enabled || !commandCamera.gameObject.activeInHierarchy))
        {
            SetAllVisible(false);
            return;
        }

        if (commandCamera == null || iconPrefab == null || iconParent == null)
            return;

        if (Time.unscaledTime >= _nextScanTime)
        {
            _nextScanTime = Time.unscaledTime + Mathf.Max(0.1f, rescanInterval);
            ScanForTeams();
        }

        // Update positions
        foreach (var kv in _iconByTeamRoot)
        {
            var teamRoot = kv.Key;
            var icon = kv.Value;
            if (teamRoot == null || icon == null) continue;

            var anchor = teamRoot.GetComponent<EncounterTeamAnchor>();
            Vector3 worldPos;

            if (anchor != null)
                worldPos = anchor.AnchorWorldPosition + worldOffset;
            else
                worldPos = teamRoot.position + worldOffset;

            Vector3 screen = commandCamera.WorldToScreenPoint(worldPos);

            // Behind camera
            if (screen.z < 0.01f)
            {
                if (icon.gameObject.activeSelf) icon.gameObject.SetActive(false);
                continue;
            }

            if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);
            icon.position = (Vector2)screen + screenOffsetPixels;

            UpdateFlankText(teamRoot, icon);
        }
    }

    private void SetAllVisible(bool visible)
    {
        foreach (var kv in _iconByTeamRoot)
        {
            if (kv.Value != null && kv.Value.gameObject.activeSelf != visible)
                kv.Value.gameObject.SetActive(visible);
        }
    }

    private void ScanForTeams()
    {
        _teamsScratch.Clear();

        if (teamRootsParent != null)
        {
            for (int i = 0; i < teamRootsParent.childCount; i++)
            {
                var child = teamRootsParent.GetChild(i);
                if (child != null && child.name.StartsWith(enemyTeamRootPrefix))
                    _teamsScratch.Add(child);
            }
        }
        else
        {
            // POC: scan scene objects
            var anchors = GameObject.FindObjectsOfType<EncounterTeamAnchor>(true);
            for (int i = 0; i < anchors.Length; i++)
            {
                var a = anchors[i];
                if (a == null) continue;
                if (a.transform != null && a.transform.name.StartsWith(enemyTeamRootPrefix))
                    _teamsScratch.Add(a.transform);
            }
        }

        // Create missing icons
        for (int i = 0; i < _teamsScratch.Count; i++)
        {
            var teamRoot = _teamsScratch[i];
            if (teamRoot == null) continue;

            if (_iconByTeamRoot.ContainsKey(teamRoot))
                continue;

            var icon = Instantiate(iconPrefab, iconParent);
            icon.name = $"EnemyTeamIcon_{teamRoot.name}";
            icon.gameObject.SetActive(true);
            _iconByTeamRoot[teamRoot] = icon;
            _flankTextByTeamRoot[teamRoot] = FindFlankText(icon);

            var overlay = FindObjectOfType<CommandOverlayUI>();
            if (overlay != null)
                overlay.RegisterEnemyTeamIcon(teamRoot, icon, "Enemy Team");

            // --- Direction arrow (optional) ---
            var anchor = teamRoot.GetComponent<EncounterTeamAnchor>();
            if (anchor != null)
            {
                var arrow = icon.GetComponent<EnemyTeamDirectionArrowUI>();
                if (arrow == null) arrow = icon.gameObject.AddComponent<EnemyTeamDirectionArrowUI>();
                arrow.TryAutoWire();
                arrow.Bind(anchor, commandCamera);

                var provider = anchor.GetComponent<EnemyTeamMoveTargetProvider>();
                if (provider == null) provider = anchor.gameObject.AddComponent<EnemyTeamMoveTargetProvider>();
                arrow.moveTargetProvider = provider;
            }

        }

        // Cleanup dead teams
        var dead = new List<Transform>();
        foreach (var kv in _iconByTeamRoot)
        {
            if (kv.Key == null || kv.Value == null)
                dead.Add(kv.Key);
        }

        for (int i = 0; i < dead.Count; i++)
        {
            if (_iconByTeamRoot.TryGetValue(dead[i], out var icon) && icon != null)
                Destroy(icon.gameObject);

            _iconByTeamRoot.Remove(dead[i]);
            _flankTextByTeamRoot.Remove(dead[i]);
        }
    }


    private void UpdateFlankText(Transform teamRoot, RectTransform icon)
    {
        if (teamRoot == null || icon == null) return;

        if (!_flankTextByTeamRoot.TryGetValue(teamRoot, out TMP_Text flankText) || flankText == null)
        {
            flankText = FindFlankText(icon);
            _flankTextByTeamRoot[teamRoot] = flankText;
        }

        if (flankText == null) return;

        int flankCount = TeamFlankIndicatorSystem.Instance != null
            ? TeamFlankIndicatorSystem.Instance.GetCommittedAttackerTeamCount(teamRoot)
            : 0;

        bool show = flankCount >= 2;
        if (flankText.gameObject.activeSelf != show)
            flankText.gameObject.SetActive(show);

        if (show)
            flankText.text = $"Flank X{flankCount}";
    }

    private TMP_Text FindFlankText(RectTransform icon)
    {
        if (icon == null) return null;

        Transform starImage = icon.Find("StarImage");
        if (starImage != null)
        {
            TMP_Text tmp = starImage.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
                return tmp;
        }

        return icon.GetComponentInChildren<TMP_Text>(true);
    }

}
