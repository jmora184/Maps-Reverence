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

    [Header("Team Star Icon")]
    public RectTransform teamStarIconPrefab;
    public float teamStarWorldHeight = 2.2f;
    public bool selectTeamWhenClickTeamedMember = true;

    [Header("Team Star Bob")]
    [Tooltip("If enabled, team star icons gently bob up and down (UI pixels) like ally/enemy icons.")]
    public bool teamStarBobEnabled = true;
    [Tooltip("Pixels up/down.")]
    public float teamStarBobAmount = 6f;
    [Tooltip("Speed of bob animation.")]
    public float teamStarBobSpeed = 2f;
    [Header("Team Star Bob Sync")]
    [Tooltip("If enabled, team star bob uses the anchor ally icon's current bob offset (so it feels attached). Falls back to its own sine bob if not found.")]
    public bool teamStarBobSyncToAnchorIcon = true;

    private readonly Dictionary<int, float> teamStarBobSeedByTeamId = new();

    [Header("Team Star Snap To Move Destination")]
    [Tooltip("If true, when issuing a move order for a team, the team star jumps to the chosen destination immediately in command view.")]
    public bool teamStarSnapToIssuedMoveDestination = true;
    [Tooltip("How close the team anchor must get to the last issued destination before the star snaps back onto the anchor (world units).")]
    public float teamStarSnapClearDistance = 1.0f;

    // Last issued move destination per team (world position on ground). When set, star renders at this point until cleared.
    private readonly Dictionary<int, Vector3> teamStarForcedWorldPosByTeamId = new();

    [Header("Button Panel (optional)")]
    public RectTransform commandButtonPanel;
    public Vector2 buttonPanelScreenOffset = new Vector2(0f, 0f);

    [Header("Hint Toast (auto-found if not assigned)")]
    public HintToastUI hintToastUI;
    public float defaultHintDuration = 2f;

    [Header("Join route detection")]
    [Tooltip("How close agent.destination must be to team.Anchor.position to count as a join-route destination.")]
    public float joinRouteDestinationSnap = 1.25f;

    private Canvas canvas;
    private CommandStateMachine sm;

    private readonly Dictionary<Transform, RectTransform> allyIconByUnit = new();
    private readonly Dictionary<Transform, RectTransform> enemyIconByUnit = new();
    private readonly Dictionary<int, RectTransform> teamStarByTeamId = new();
    private readonly HashSet<Transform> teamedUnits = new();

    private Coroutine hintHideRoutine;

    // After JOIN confirmed (2nd ally chosen), keep panel hidden until primary selection changes.
    private bool suppressButtonPanelAfterJoinTargetChosen;
    private GameObject suppressButtonPanelPrimarySelectionRef;

    // After MOVE destination chosen, keep panel hidden until primary selection changes.
    private bool suppressButtonPanelAfterMoveTargetChosen;
    private GameObject suppressButtonPanelPrimarySelectionRef_Move;

    // Track state transitions (for move confirm suppression in setups that return to UnitSelected)
    private CommandStateMachine.State lastSmState = CommandStateMachine.State.AwaitSelection;

    private void Awake()
    {
        Instance = this;

        canvas = GetComponentInParent<Canvas>();
        if (canvasRoot == null && canvas != null)
            canvasRoot = canvas.transform as RectTransform;

        sm = FindObjectOfType<CommandStateMachine>();
        if (sm != null) lastSmState = sm.CurrentState;

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

            // Snap star on move target chosen (works for queued or immediate moves)
            sm.OnMoveRequested += HandleMoveRequested_SnapTeamStar;
            sm.OnMoveTargetChosen += HandleMoveRequested_SnapTeamStar;
        }

        AutoFindHintToastUI();
    }

    private void OnDisable()
    {
        if (sm != null)
        {
            sm.OnAddRequested -= HandleAddRequested_ForButtonPanel;
            sm.OnSelectionChanged -= HandleSelectionChanged_ForButtonPanel;

            sm.OnMoveRequested -= HandleMoveRequested_SnapTeamStar;
            sm.OnMoveTargetChosen -= HandleMoveRequested_SnapTeamStar;
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

    // -------------------- HINT TOAST --------------------

    private void AutoFindHintToastUI()
    {
        if (hintToastUI != null) return;

        // Finds disabled objects too.
        var all = Resources.FindObjectsOfTypeAll<HintToastUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null) continue;
            if (!h.gameObject.scene.IsValid()) continue; // scene objects only

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

        hintToastUI.Show(message);

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


            // Bind ally UI helpers (health bar, team tag, etc.)
            var allyUI = icon.GetComponent<AllyHealthIcon>();
            if (allyUI != null) allyUI.Bind(go.transform);

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

        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();

        // Detect MOVE confirmation moment: MoveTargeting -> UnitSelected (some setups)
        if (sm != null)
        {
            if (lastSmState == CommandStateMachine.State.MoveTargeting &&
                sm.CurrentState == CommandStateMachine.State.UnitSelected)
            {
                suppressButtonPanelAfterMoveTargetChosen = true;
                suppressButtonPanelPrimarySelectionRef_Move = sm.PrimarySelected;

                if (commandButtonPanel != null)
                    commandButtonPanel.gameObject.SetActive(false);
            }

            lastSmState = sm.CurrentState;
        }

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

        // Optional command button panel anchoring
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
        Camera cam = commandCam != null ? commandCam : Camera.main;
        Vector3 screen = cam != null ? cam.WorldToScreenPoint(wpos) : Vector3.zero;

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

    private void UpdateWorldAnchoredUI_Point(RectTransform ui, Vector3 worldPoint, float height, Camera uiCam)
    {
        if (ui == null) return;

        Vector3 wpos = worldPoint + Vector3.up * height;
        Camera cam = commandCam != null ? commandCam : Camera.main;
        Vector3 screen = cam != null ? cam.WorldToScreenPoint(wpos) : Vector3.zero;

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

    // -------------------- SELECTION HIGHLIGHT --------------------

    private void UpdateSelectionHighlight()
    {
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;

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

        HashSet<Transform> selectedAllies = new();
        foreach (var t in selected)
            if (t != null && allyIconByUnit.ContainsKey(t))
                selectedAllies.Add(t);

        bool multiAllySelection = selectedAllies.Count > 1;

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

    // -------------------- TEAM STARS --------------------

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
                if (!star.gameObject.activeSelf) star.gameObject.SetActive(true);

                // seed
                if (!teamStarBobSeedByTeamId.TryGetValue(team.Id, out float seed))
                {
                    seed = UnityEngine.Random.Range(0f, 1000f);
                    teamStarBobSeedByTeamId[team.Id] = seed;
                }

                // forced move destination?
                if (teamStarSnapToIssuedMoveDestination && teamStarForcedWorldPosByTeamId.TryGetValue(team.Id, out var forcedPos))
                {
                    UpdateWorldAnchoredUI_Point(star, forcedPos, teamStarWorldHeight, uiCam);

                    if (!IsTeamStillMovingToward(team, forcedPos))
                        teamStarForcedWorldPosByTeamId.Remove(team.Id);
                }
                else
                {
                    UpdateWorldAnchoredUI(star, anchor, teamStarWorldHeight, uiCam);
                }

                if (teamStarBobEnabled)
                {
                    float y = 0f;

                    if (teamStarBobSyncToAnchorIcon)
                        y = GetAnchorIconBobOffsetPixels(anchor);

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
            teamStarForcedWorldPosByTeamId.Remove(id);
        }
    }

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

            var team = TeamManager.Instance.GetTeamOf(unit);
            bool isTeamed = team != null;

            bool allowClick = !isTeamed;

            if (isTeamed && team != null)
            {
                allowClick = IsInRouteToTeamAnchor(unit, team);

                if (!allowClick)
                {
                    var marker = unit.GetComponent<JoinRouteMarker>();
                    if (marker != null && marker.inRoute)
                        allowClick = true;
                }
            }

            // While join-targeting, allow clicking teamed allies so they can be chosen as the JOIN target.
            if (sm != null && sm.CurrentState == CommandStateMachine.State.AddTargeting)
                allowClick = true;

            btn.interactable = allowClick;
            btn.enabled = allowClick;
        }
    }

    private List<GameObject> BuildTeamSelection(Team team)
    {
        var sel = new List<GameObject>();
        if (team == null) return sel;

        if (team.Anchor != null && team.Contains(team.Anchor))
            sel.Add(team.Anchor.gameObject);

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

        // ✅ If we're currently choosing a JOIN target, clicking the star should act like clicking the team "anchor"
        // so an unteamed ally can join an already-existing team.
        if (sm.CurrentState == CommandStateMachine.State.AddTargeting)
        {
            Transform anchor = team.Anchor;
            if (anchor == null && team.Members != null && team.Members.Count > 0)
                anchor = team.Members[0];

            if (anchor != null)
                sm.ClickUnitFromUI(anchor.gameObject);

            return;
        }

        // Otherwise: select the whole team.
        sm.SetSelection(BuildTeamSelection(team));

        // IMPORTANT: Do NOT override the panel anchor to a member Transform.
        // The panel will anchor under the star UI in UpdateCommandButtonPanel.
    }

    private void OnUnitClicked(Transform unit)
    {
        if (unit == null) return;

        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;


        // If we're currently choosing a JOIN target, do NOT auto-select the whole team.
        // We need the click to pass through so CommandStateMachine can fire OnAddRequested.
        if (sm.CurrentState == CommandStateMachine.State.AddTargeting)
        {
            sm.ClickUnitFromUI(unit.gameObject);
            return;
        }

        if (unit.CompareTag("Ally") && TeamManager.Instance != null)
        {
            var team = TeamManager.Instance.GetTeamOf(unit);
            if (team != null)
            {
                if (IsInRouteToTeamAnchor(unit, team))
                {
                    string msg = (!string.IsNullOrWhiteSpace(sm.joinInRouteMessage)) ? sm.joinInRouteMessage : "Ally is en route";
                    float dur = sm.joinInRouteDuration > 0 ? sm.joinInRouteDuration : defaultHintDuration;
                    ShowHint(msg, dur);
                    return;
                }

                if (selectTeamWhenClickTeamedMember)
                {
                    sm.SetSelection(BuildTeamSelection(team));

                    // IMPORTANT: Do NOT override the panel anchor to a member Transform.
                    // The panel will anchor under the star UI in UpdateCommandButtonPanel.
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

    // -------------------- BUTTON PANEL --------------------

    private void UpdateCommandButtonPanel(Camera uiCam)
    {
        if (commandButtonPanel == null) return;

        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;

        // JOIN suppression
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

        // MOVE suppression
        if (suppressButtonPanelAfterMoveTargetChosen)
        {
            if (sm.PrimarySelected != suppressButtonPanelPrimarySelectionRef_Move)
                suppressButtonPanelAfterMoveTargetChosen = false;
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

        // Prefer anchoring under team star UI whenever the primary selected unit belongs to a team.
        RectTransform teamStarUI = null;
        if (TeamManager.Instance != null && sm.PrimarySelected != null)
        {
            var team = TeamManager.Instance.GetTeamOf(sm.PrimarySelected.transform);
            if (team != null)
                teamStarByTeamId.TryGetValue(team.Id, out teamStarUI);
        }

        if (teamStarUI != null && teamStarUI.gameObject.activeInHierarchy)
        {
            // Best case: both under the same RectTransform parent (common setup).
            var panelParent = commandButtonPanel.parent as RectTransform;
            var starParent = teamStarUI.parent as RectTransform;

            if (panelParent != null && starParent != null && panelParent == starParent)
            {
                commandButtonPanel.anchoredPosition = teamStarUI.anchoredPosition + buttonPanelScreenOffset;
                return;
            }

            // Otherwise, convert the star's UI world position into the panel parent's local space.
            RectTransform targetParent = panelParent != null ? panelParent : canvasRoot;
            if (targetParent != null)
            {
                Vector3 starWorld = teamStarUI.TransformPoint(teamStarUI.rect.center);
                Vector3 lp3 = targetParent.InverseTransformPoint(starWorld);
                commandButtonPanel.anchoredPosition = new Vector2(lp3.x, lp3.y) + buttonPanelScreenOffset;
                return;
            }
        }

        // Fallback: anchor under the primary selected unit (world position)
        Transform anchor = sm.PrimarySelected.transform;

        Vector3 wpos = anchor.position + Vector3.up * iconWorldHeight;
        Camera cam = commandCam != null ? commandCam : Camera.main;
        Vector3 screen = cam != null ? cam.WorldToScreenPoint(wpos) : Vector3.zero;

        if (screen.z < 0f)
        {
            commandButtonPanel.gameObject.SetActive(false);
            return;
        }

        RectTransform parentRect = commandButtonPanel.parent as RectTransform;
        if (parentRect == null) parentRect = canvasRoot;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect,
            new Vector2(screen.x, screen.y),
            uiCam,
            out Vector2 lp);

        commandButtonPanel.anchoredPosition = lp + buttonPanelScreenOffset;
    }

    // -------------------- TEAM STAR SNAP --------------------

    private void HandleMoveRequested_SnapTeamStar(IReadOnlyList<GameObject> selection, Vector3 destination)
    {
        if (!teamStarSnapToIssuedMoveDestination) return;
        if (selection == null || selection.Count == 0) return;
        if (TeamManager.Instance == null) return;

        Transform primary = selection[0] != null ? selection[0].transform : null;
        if (primary == null) return;

        Team team = TeamManager.Instance.GetTeamOf(primary);
        if (team == null) return;

        teamStarForcedWorldPosByTeamId[team.Id] = destination;

        // Also suppress the panel until selection changes (so panel doesn't pop back immediately after click)
        suppressButtonPanelAfterMoveTargetChosen = true;
        suppressButtonPanelPrimarySelectionRef_Move = sm != null ? sm.PrimarySelected : selection[0];
        if (commandButtonPanel != null) commandButtonPanel.gameObject.SetActive(false);
    }

    private bool IsTeamStillMovingToward(Team team, Vector3 destination)
    {
        if (team == null) return false;

        Transform anchor = team.Anchor != null ? team.Anchor : (team.Members.Count > 0 ? team.Members[0] : null);
        if (anchor == null) return false;

        if (Vector3.Distance(anchor.position, destination) <= Mathf.Max(0.1f, teamStarSnapClearDistance))
            return false;

        var agent = anchor.GetComponent<NavMeshAgent>();
        if (agent == null) agent = anchor.GetComponentInChildren<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
        {
            if (agent.pathPending) return true;
            if (agent.hasPath && agent.remainingDistance > Mathf.Max(agent.stoppingDistance, 0.05f) + 0.2f)
                return true;
        }

        for (int i = 0; i < team.Members.Count; i++)
        {
            var m = team.Members[i];
            if (m == null) continue;
            if (Vector3.Distance(m.position, destination) > Mathf.Max(0.1f, teamStarSnapClearDistance) + 0.25f)
                return true;
        }

        return false;
    }

    // -------------------- PANEL EVENTS --------------------

    private void HandleAddRequested_ForButtonPanel(IReadOnlyList<GameObject> selection, GameObject clickedUnit)
    {
        if (sm == null) return;
        if (clickedUnit == null) return;

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
        suppressButtonPanelAfterJoinTargetChosen = false;
        suppressButtonPanelPrimarySelectionRef = null;

        suppressButtonPanelAfterMoveTargetChosen = false;
        suppressButtonPanelPrimarySelectionRef_Move = null;
    }

    // -------------------- CLEANUP --------------------

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
