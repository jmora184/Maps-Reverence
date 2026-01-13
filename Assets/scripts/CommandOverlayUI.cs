using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public class CommandOverlayUI : MonoBehaviour
{
    public static CommandOverlayUI Instance { get; private set; }
    public static CommandOverlayUI instance => Instance; // backwards compat

    [Header("References")]
    public Camera commandCam;
    public RectTransform canvasRoot;

    [Header("Icon Prefabs (UI)")]
    public RectTransform allyIconPrefab;
    public RectTransform enemyIconPrefab;

    [Header("Player Icon (Persistent UI)")]
    public RectTransform playerIcon;
    public Transform playerTarget;
    public float playerIconWorldHeight = 2f;

    [Header("Boss Icon (Persistent UI)")]
    public RectTransform bossIcon;
    public Transform bossTarget;
    public float bossIconWorldHeight = 2.5f;

    [Header("Settings")]
    public float iconWorldHeight = 2f;
    public bool enemyIconsClickable = true;

    [Header("Selected Ring")]
    public string selectedRingChildName = "SelectedRing";
    [Tooltip("Optional second ring for team selection. Add a child under the ally icon named this (ex: TeamSelectedRing).")]
    public string teamSelectedRingChildName = "TeamSelectedRing";

    [Header("Team Star Bob Sync")]
    [Tooltip("If enabled, team star bob uses the anchor ally icon\'s current bob offset (so it feels attached). Falls back to its own sine bob if not found.")]
    public bool teamStarBobSyncToAnchorIcon = true;


    [Header("Team Star Icon")]
    public RectTransform teamStarIconPrefab;
    public float teamStarWorldHeight = 2.2f;
    public bool selectTeamWhenClickTeamedMember = true;

    [Header("Button Panel (optional)")]
    public RectTransform commandButtonPanel;
    public Vector2 buttonPanelScreenOffset = new Vector2(0f, 0f);

    [Header("Hint Toast (auto-found if not assigned)")]
    public HintToastUI hintToastUI;
    public float defaultHintDuration = 2f;

    [Header("Join route detection")]
    [Tooltip("How close agent.destination must be to team.Anchor.position to count as a join-route destination.")]
    public float joinRouteDestinationSnap = 1.25f;

    [Header("Team Star Bob")]
    [Tooltip("If enabled, team star icons gently bob up and down (UI pixels) like ally/enemy icons.")]
    public bool teamStarBobEnabled = true;
    [Tooltip("Pixels up/down.")]
    public float teamStarBobAmount = 6f;
    [Tooltip("Speed of bob animation.")]
    public float teamStarBobSpeed = 2f;

    private readonly Dictionary<int, float> teamStarBobSeedByTeamId = new();

    private Canvas canvas;
    private CommandStateMachine sm;

    private readonly Dictionary<Transform, RectTransform> allyIconByUnit = new();
    private readonly Dictionary<Transform, RectTransform> enemyIconByUnit = new();
    private readonly Dictionary<int, RectTransform> teamStarByTeamId = new();
    private readonly HashSet<Transform> teamedUnits = new();

    private Transform buttonPanelAnchorOverride;
    private float buttonPanelAnchorOverrideUntil;

    private Coroutine hintHideRoutine;

    // ✅ NEW: after join is confirmed (2nd ally chosen), keep the panel hidden
    // until the primary selection changes.
    private bool suppressButtonPanelAfterJoinTargetChosen;
    private GameObject suppressButtonPanelPrimarySelectionRef;

    private void Awake()
    {
        Instance = this;

        canvas = GetComponentInParent<Canvas>();
        if (canvasRoot == null && canvas != null)
            canvasRoot = canvas.transform as RectTransform;

        sm = FindObjectOfType<CommandStateMachine>();

        AutoFindHintToastUI();

        if (commandButtonPanel != null)
            commandButtonPanel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm != null)
        {
            sm.OnAddRequested += HandleAddRequested_ForButtonPanel;
            sm.OnSelectionChanged += HandleSelectionChanged_ForButtonPanel;
        }

        AutoFindHintToastUI();
    }

    private void OnDisable()
    {
        if (sm != null)
        {
            sm.OnAddRequested -= HandleAddRequested_ForButtonPanel;
            sm.OnSelectionChanged -= HandleSelectionChanged_ForButtonPanel;
        }
    }

    private void Start()
    {
        AutoFindHintToastUI();

        if (playerTarget == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerTarget = p.transform;
        }

        if (bossTarget == null)
        {
            var b = GameObject.FindGameObjectWithTag("Boss");
            if (b != null) bossTarget = b.transform;
        }

        BuildIcons();
    }

    // -------------------- HINT TOAST (WORKS EVEN IF DISABLED) --------------------

    private void AutoFindHintToastUI()
    {
        if (hintToastUI != null) return;

        // Finds disabled objects too.
        var all = Resources.FindObjectsOfTypeAll<HintToastUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null) continue;

            // Only use scene objects (not prefabs/assets)
            if (!h.gameObject.scene.IsValid()) continue;

            hintToastUI = h;
            return;
        }
    }

    private void ShowHint(string message, float durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        AutoFindHintToastUI();

        if (hintToastUI == null)
        {
            Debug.Log($"[Hint] {message}");
            return;
        }

        // Show now
        hintToastUI.Show(message);

        // Auto-hide after duration
        if (hintHideRoutine != null) StopCoroutine(hintHideRoutine);
        hintHideRoutine = StartCoroutine(HideHintAfter(durationSeconds));
    }

    private IEnumerator HideHintAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, seconds));

        if (hintToastUI != null)
            hintToastUI.Hide();

        hintHideRoutine = null;
    }

    // -------------------- ICON BUILD / UPDATE --------------------

    public void BuildIcons()
    {
        ClearAllIcons();

        // Allies
        var allies = GameObject.FindGameObjectsWithTag("Ally");
        for (int i = 0; i < allies.Length; i++)
        {
            var go = allies[i];
            if (go == null) continue;

            var icon = Instantiate(allyIconPrefab, canvasRoot);
            icon.gameObject.SetActive(true);

            allyIconByUnit[go.transform] = icon;

            var btn = icon.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                Transform captured = go.transform;
                btn.onClick.AddListener(() => OnUnitClicked(captured));
            }
        }

        // Enemies
        var enemies = GameObject.FindGameObjectsWithTag("Enemy");
        for (int i = 0; i < enemies.Length; i++)
        {
            var go = enemies[i];
            if (go == null) continue;

            var icon = Instantiate(enemyIconPrefab, canvasRoot);
            icon.gameObject.SetActive(true);

            enemyIconByUnit[go.transform] = icon;

            var btn = icon.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                Transform captured = go.transform;

                if (enemyIconsClickable)
                {
                    btn.onClick.AddListener(() => OnUnitClicked(captured));
                    btn.interactable = true;
                    btn.enabled = true;
                }
                else
                {
                    btn.interactable = false;
                    btn.enabled = false;
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (canvasRoot == null) return;

        Camera uiCam = canvas != null ? canvas.worldCamera : null;
        if (uiCam == null) uiCam = Camera.main;

        // Update ally/enemy icons
        UpdateIcons(allyIconByUnit, iconWorldHeight, uiCam);
        UpdateIcons(enemyIconByUnit, iconWorldHeight, uiCam);

        // Update persistent player icon
        if (playerIcon != null && playerTarget != null)
            UpdateWorldAnchoredUI(playerIcon, playerTarget, playerIconWorldHeight, uiCam);

        // Update persistent boss icon
        if (bossIcon != null && bossTarget != null)
            UpdateWorldAnchoredUI(bossIcon, bossTarget, bossIconWorldHeight, uiCam);

        // Sync team stars & disable teamed unit icons
        SyncTeamsAndStars(uiCam);
        UpdateAllyIconClickability();

        // Selection ring highlight
        UpdateSelectionHighlight();

        // Optional command button panel anchoring (not required for your ContextCommandPanelUI)
        UpdateCommandButtonPanel(uiCam);
    }

    private void UpdateIcons(Dictionary<Transform, RectTransform> dict, float worldHeight, Camera uiCam)
    {
        var toRemove = new List<Transform>();

        foreach (var kvp in dict)
        {
            Transform t = kvp.Key;
            RectTransform icon = kvp.Value;

            if (t == null || icon == null)
            {
                toRemove.Add(t);
                continue;
            }

            UpdateWorldAnchoredUI(icon, t, worldHeight, uiCam);
        }

        for (int i = 0; i < toRemove.Count; i++)
            dict.Remove(toRemove[i]);
    }

    private void UpdateWorldAnchoredUI(RectTransform ui, Transform worldTarget, float height, Camera uiCam)
    {
        if (ui == null || worldTarget == null) return;

        Vector3 wpos = worldTarget.position + Vector3.up * height;
        Vector3 screen = (commandCam != null) ? commandCam.WorldToScreenPoint(wpos) : Camera.main.WorldToScreenPoint(wpos);

        // Hide if behind camera
        if (screen.z < 0f)
        {
            ui.gameObject.SetActive(false);
            return;
        }

        ui.gameObject.SetActive(true);

        RectTransform parentRT = ui.parent as RectTransform;
        if (parentRT == null) parentRT = canvasRoot;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRT,
            new Vector2(screen.x, screen.y),
            uiCam,
            out Vector2 localPoint);

        ui.anchoredPosition = localPoint;
    }

    private void UpdateSelectionHighlight()
    {
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;

        // Build selected transform sets
        HashSet<Transform> selected = new();
        var cur = sm.CurrentSelection;
        if (cur != null)
        {
            for (int i = 0; i < cur.Count; i++)
            {
                var go = cur[i];
                if (go != null) selected.Add(go.transform);
            }
        }

        // Only allies participate in team highlighting
        HashSet<Transform> selectedAllies = new();
        foreach (var t in selected)
            if (t != null && allyIconByUnit.ContainsKey(t))
                selectedAllies.Add(t);

        bool multiAllySelection = selectedAllies.Count > 1;

        // Cache whether each team is fully selected (to enable the team ring)
        Dictionary<int, bool> fullTeamSelectedById = null;
        if (multiAllySelection && TeamManager.Instance != null)
        {
            fullTeamSelectedById = new Dictionary<int, bool>();
            var teams = TeamManager.Instance.Teams;
            for (int i = 0; i < teams.Count; i++)
            {
                var team = teams[i];
                if (team == null) continue;
                fullTeamSelectedById[team.Id] = IsFullTeamSelected(team, selectedAllies);
            }
        }

        // Allies: regular selected ring for any selected ally; team ring only when the full team is selected
        foreach (var kvp in allyIconByUnit)
        {
            bool on = selected.Contains(kvp.Key);
            SetSelectedRing(kvp.Value, on);

            bool teamOn = false;
            if (on && multiAllySelection && TeamManager.Instance != null)
            {
                var team = TeamManager.Instance.GetTeamOf(kvp.Key);
                if (team != null && fullTeamSelectedById != null && fullTeamSelectedById.TryGetValue(team.Id, out bool full) && full)
                    teamOn = true;
            }

            SetTeamSelectedRing(kvp.Value, teamOn);
        }

        // Enemies: only regular selected ring
        foreach (var kvp in enemyIconByUnit)
            SetSelectedRing(kvp.Value, selected.Contains(kvp.Key));
    }

    private void SetSelectedRing(RectTransform icon, bool on)
    {
        if (icon == null) return;
        var ring = icon.Find(selectedRingChildName);
        if (ring != null) ring.gameObject.SetActive(on);
    }

    private void SetTeamSelectedRing(RectTransform icon, bool on)
    {
        if (icon == null) return;
        if (string.IsNullOrEmpty(teamSelectedRingChildName)) return;
        var ring = icon.Find(teamSelectedRingChildName);
        if (ring != null) ring.gameObject.SetActive(on);
    }

    private bool IsFullTeamSelected(Team team, HashSet<Transform> selectedAllies)
    {
        if (team == null || selectedAllies == null) return false;

        int memberCount = 0;
        for (int i = 0; i < team.Members.Count; i++)
        {
            var m = team.Members[i];
            if (m == null) continue;
            memberCount++;
            if (!selectedAllies.Contains(m)) return false;
        }

        return selectedAllies.Count == memberCount;
    }

    private float GetAnchorIconBobOffsetPixels(Transform anchor)
    {
        if (anchor == null) return 0f;
        if (!allyIconByUnit.TryGetValue(anchor, out var icon) || icon == null) return 0f;

        // Try to read the current bob offset from the ally icon\'s bob script via reflection.
        // This keeps the team star perfectly in-phase with the anchor ally icon if that prefab is bobbing itself.
        var behaviours = icon.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            var b = behaviours[i];
            if (b == null) continue;

            var t = b.GetType();
            var fTarget = t.GetField("bobTarget", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var fBase = t.GetField("bobBaseLocalPos", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fTarget == null || fBase == null) continue;

            try
            {
                var bobTarget = fTarget.GetValue(b) as RectTransform;
                if (bobTarget == null) continue;

                object baseObj = fBase.GetValue(b);
                if (baseObj is Vector3 basePos)
                    return bobTarget.localPosition.y - basePos.y;
            }
            catch { }
        }

        return 0f;
    }

    private void SyncTeamsAndStars(Camera uiCam)
    {
        teamedUnits.Clear();

        if (TeamManager.Instance == null) return;
        var teams = TeamManager.Instance.Teams;

        HashSet<int> liveTeamIds = new();

        for (int i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            if (team == null) continue;

            liveTeamIds.Add(team.Id);

            for (int m = 0; m < team.Members.Count; m++)
                if (team.Members[m] != null)
                    teamedUnits.Add(team.Members[m]);

            if (teamStarIconPrefab == null) continue;

            if (!teamStarByTeamId.TryGetValue(team.Id, out RectTransform star) || star == null)
            {
                star = Instantiate(teamStarIconPrefab, canvasRoot);
                star.gameObject.SetActive(true);
                teamStarByTeamId[team.Id] = star;
            }

            star.SetAsLastSibling();

            var teamUI = star.GetComponent<TeamIconUI>();
            if (teamUI != null)
                teamUI.Bind(team, OnTeamStarClicked);

            Transform anchor = team.Anchor;
            if (anchor == null && team.Members.Count > 0) anchor = team.Members[0];

            if (anchor != null)
            {
                // Ensure visible (it may have been hidden if anchor was temporarily null)
                if (!star.gameObject.activeSelf) star.gameObject.SetActive(true);

                // Per-team bob seed so each star moves a little differently
                float seed;
                if (!teamStarBobSeedByTeamId.TryGetValue(team.Id, out seed))
                {
                    seed = UnityEngine.Random.Range(0f, 1000f);
                    teamStarBobSeedByTeamId[team.Id] = seed;
                }

                // Position star over the anchor (star-holder) then apply optional bob in UI pixels
                UpdateWorldAnchoredUI(star, anchor, teamStarWorldHeight, uiCam);

                if (teamStarBobEnabled)
                {
                    float y = 0f;

                    // Prefer syncing to the anchor ally icon\'s bob if available
                    if (teamStarBobSyncToAnchorIcon)
                        y = GetAnchorIconBobOffsetPixels(anchor);

                    // Fallback: sine bob with per-team seed
                    if (Mathf.Approximately(y, 0f))
                        y = Mathf.Sin((Time.unscaledTime + seed) * teamStarBobSpeed) * teamStarBobAmount;

                    star.anchoredPosition += new Vector2(0f, y);
                }
            }
            else
            {
                star.gameObject.SetActive(false);
            }
        }

        // Cleanup removed teams
        var removeIds = new List<int>();
        foreach (var kvp in teamStarByTeamId)
            if (!liveTeamIds.Contains(kvp.Key))
                removeIds.Add(kvp.Key);

        for (int i = 0; i < removeIds.Count; i++)
        {
            int id = removeIds[i];
            if (teamStarByTeamId.TryGetValue(id, out var rt) && rt != null)
                Destroy(rt.gameObject);
            teamStarByTeamId.Remove(id);
            teamStarBobSeedByTeamId.Remove(id);
        }
    }

    // If a unit is part of a team, its ally icon should not be selectable.
    // Players must click the Team Star icon to select / command the whole team.
    private void UpdateAllyIconClickability()
    {
        if (TeamManager.Instance == null) return;

        foreach (var kvp in allyIconByUnit)
        {
            Transform unit = kvp.Key;
            RectTransform icon = kvp.Value;

            if (unit == null || icon == null) continue;

            var btn = icon.GetComponent<Button>();
            if (btn == null) continue;

            // If a unit is in a team, normally its icon is not clickable.
            // BUT: while that unit is still traveling to the team ("in route"), keep it clickable
            // so the player can click and receive the hint message.
            var team = TeamManager.Instance.GetTeamOf(unit);
            bool isTeamed = team != null;

            bool allowClick = !isTeamed;

            if (isTeamed && team != null)
            {
                // If it's still moving toward the team anchor, allow click so OnUnitClicked can show the hint.
                allowClick = IsInRouteToTeamAnchor(unit, team);

                // Fallback: if JoinRouteMarker is present, also treat it as "in route"
                // (covers cases where destination isn't snapped exactly to the anchor yet).
                if (!allowClick)
                {
                    var marker = unit.GetComponent<JoinRouteMarker>();
                    if (marker != null && marker.inRoute)
                        allowClick = true;
                }
            }

            btn.interactable = allowClick;
            btn.enabled = allowClick;
        }
    }


    // Build a selection list for a team such that the team's Anchor is first.
    // This ensures any UI that anchors to PrimarySelected (selection[0]) appears over the star-holder.
    private List<GameObject> BuildTeamSelection(Team team)
    {
        var sel = new List<GameObject>();
        if (team == null) return sel;

        // Put the anchor first (if it is a member)
        if (team.Anchor != null && team.Contains(team.Anchor))
            sel.Add(team.Anchor.gameObject);

        // Then add the rest (skipping nulls and the anchor if already added)
        for (int i = 0; i < team.Members.Count; i++)
        {
            var m = team.Members[i];
            if (m == null) continue;
            if (team.Anchor != null && m == team.Anchor) continue;
            sel.Add(m.gameObject);
        }

        return sel;
    }

    private void OnTeamStarClicked(Team team)
    {
        if (team == null) return;

        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;

        // Make the CommandStateMachine treat the team like the current selection.
        // IMPORTANT: Put the star-holder (team.Anchor) first so PrimarySelected matches the star UI.
        var sel = BuildTeamSelection(team);
        sm.SetSelection(sel);

        // Pin panel over the team anchor briefly (optional)
        if (team.Anchor != null)
        {
            buttonPanelAnchorOverride = team.Anchor;
            buttonPanelAnchorOverrideUntil = Time.time + 10f;
        }
    }

    private void OnUnitClicked(Transform unit)
    {
        if (unit == null) return;

        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;

        // If teamed ally clicked: show "in route" OR select team
        if (unit.CompareTag("Ally") && TeamManager.Instance != null)
        {
            var team = TeamManager.Instance.GetTeamOf(unit);
            if (team != null)
            {
                if (IsInRouteToTeamAnchor(unit, team))
                {
                    string msg = (sm != null && !string.IsNullOrWhiteSpace(sm.joinInRouteMessage)) ? sm.joinInRouteMessage : "Ally is en route";
                    float dur = (sm != null ? sm.joinInRouteDuration : defaultHintDuration);
                    ShowHint(msg, dur);
                    return;
                }

                if (selectTeamWhenClickTeamedMember)
                {
                    var sel = BuildTeamSelection(team);

                    sm.SetSelection(sel);

                    if (team.Anchor != null)
                    {
                        buttonPanelAnchorOverride = team.Anchor;
                        buttonPanelAnchorOverrideUntil = Time.time + 10f;
                    }
                    return;
                }
            }
        }

        sm.ClickUnitFromUI(unit.gameObject);
    }

    private bool IsInRouteToTeamAnchor(Transform unit, Team team)
    {
        if (unit == null || team == null) return false;
        if (team.Anchor == null) return false;
        if (!teamedUnits.Contains(unit)) return false;

        var agent = unit.GetComponent<NavMeshAgent>();
        if (agent == null) agent = unit.GetComponentInChildren<NavMeshAgent>();
        if (agent == null || !agent.isActiveAndEnabled) return false;

        if (agent.pathPending) return false;
        if (!agent.hasPath) return false;

        float minRemain = Mathf.Max(agent.stoppingDistance, 0.05f);
        if (agent.remainingDistance <= minRemain) return false;

        float destToAnchor = Vector3.Distance(agent.destination, team.Anchor.position);
        return destToAnchor <= joinRouteDestinationSnap;
    }

    private void UpdateCommandButtonPanel(Camera uiCam)
    {
        if (commandButtonPanel == null) return;

        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;

        if (suppressButtonPanelAfterJoinTargetChosen)
        {
            if (sm.PrimarySelected != suppressButtonPanelPrimarySelectionRef)
                suppressButtonPanelAfterJoinTargetChosen = false;
            else
            {
                commandButtonPanel.gameObject.SetActive(false);
                return;
            }
        }

        if (sm.PrimarySelected == null || sm.CurrentState != CommandStateMachine.State.UnitSelected)
        {
            commandButtonPanel.gameObject.SetActive(false);
            return;
        }

        commandButtonPanel.gameObject.SetActive(true);

        Transform anchor = sm.PrimarySelected.transform;

        if (buttonPanelAnchorOverride != null && Time.time <= buttonPanelAnchorOverrideUntil)
            anchor = buttonPanelAnchorOverride;

        Vector3 wpos = anchor.position + Vector3.up * iconWorldHeight;
        Vector3 screen = (commandCam != null) ? commandCam.WorldToScreenPoint(wpos) : Camera.main.WorldToScreenPoint(wpos);

        if (screen.z < 0f)
        {
            commandButtonPanel.gameObject.SetActive(false);
            return;
        }

        RectTransform parentRT = commandButtonPanel.parent as RectTransform;
        if (parentRT == null) parentRT = canvasRoot;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRT,
            new Vector2(screen.x, screen.y),
            uiCam,
            out Vector2 localPoint);

        commandButtonPanel.anchoredPosition = localPoint + buttonPanelScreenOffset;
    }

    private void HandleAddRequested_ForButtonPanel(IReadOnlyList<GameObject> selection, GameObject clickedUnit)
    {
        if (sm == null) return;
        if (clickedUnit == null) return;

        // Detect JOIN confirmation moment:
        if (sm.JoinArmed && sm.JoinSource != null && clickedUnit != sm.JoinSource)
        {
            suppressButtonPanelAfterJoinTargetChosen = true;
            suppressButtonPanelPrimarySelectionRef = sm.PrimarySelected;

            if (commandButtonPanel != null)
                commandButtonPanel.gameObject.SetActive(false);
        }
    }

    private void HandleSelectionChanged_ForButtonPanel(IReadOnlyList<GameObject> selection)
    {
        // Selection changed => allow panel again
        suppressButtonPanelAfterJoinTargetChosen = false;
        suppressButtonPanelPrimarySelectionRef = null;
    }

    private void ClearAllIcons()
    {
        foreach (var kvp in allyIconByUnit)
            if (kvp.Value != null) Destroy(kvp.Value.gameObject);
        allyIconByUnit.Clear();

        foreach (var kvp in enemyIconByUnit)
            if (kvp.Value != null) Destroy(kvp.Value.gameObject);
        enemyIconByUnit.Clear();

        foreach (var kvp in teamStarByTeamId)
            if (kvp.Value != null) Destroy(kvp.Value.gameObject);
        teamStarByTeamId.Clear();
    }
}