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

    [Header("Team Star Prefab (UI)")]
    public RectTransform teamStarIconPrefab;
    public float teamStarWorldHeight = 2.25f;

    [Header("Command Button Panel (UI)")]
    public RectTransform commandButtonPanel;
    public float buttonPanelWorldHeight = 2.4f;
    public Vector2 buttonPanelScreenOffset = new Vector2(70f, 0f);

    [Header("Settings")]
    public float iconWorldHeight = 2f;
    public bool enemyIconsClickable = true;

    [Tooltip("If a teamed ally is clicked (and not in-join-route), select the whole team.")]
    public bool selectTeamWhenClickTeamedMember = true;

    [Header("Join Route Detection")]
    [Tooltip("How close agent.destination must be to team.Anchor.position to be considered 'joining team route'.")]
    public float joinRouteDestinationSnap = 0.6f;

    [Header("Selection Ring")]
    public string selectedRingChildName = "SelectedRing";

    [Header("Player Icon (Persistent UI)")]
    public RectTransform playerIcon;
    public Transform playerTarget;
    public float playerIconWorldHeight = 2f;

    [Header("Boss Icon (Persistent UI)")]
    public RectTransform bossIcon;
    public Transform bossTarget;
    public float bossIconWorldHeight = 2.5f;

    [Header("Hint Toast (auto-found if not assigned)")]
    public HintToastUI hintToastUI;
    public float defaultHintDuration = 2f;

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

    // -------------------- COMMAND MODE --------------------

    private void EnsureCommandCam()
    {
        if (commandCam != null) return;

        if (CommandCamToggle.Instance != null && CommandCamToggle.Instance.commandCam != null)
            commandCam = CommandCamToggle.Instance.commandCam;
    }

    private bool IsInCommandMode()
    {
        if (CommandCamToggle.Instance != null)
            return CommandCamToggle.Instance.IsCommandMode;

        return commandCam != null && commandCam.enabled;
    }

    public void BuildIcons()
    {
        EnsureCommandCam();

        ClearIconsDict(allyIconByUnit);
        ClearIconsDict(enemyIconByUnit);
        ClearTeamStars();

        BuildIconsForTag("Ally", allyIconPrefab, allyIconByUnit, clickable: true);
        BuildIconsForTag("Enemy", enemyIconPrefab, enemyIconByUnit, clickable: enemyIconsClickable);
    }

    private void BuildIconsForTag(string tag, RectTransform prefab, Dictionary<Transform, RectTransform> dict, bool clickable)
    {
        if (prefab == null || canvasRoot == null) return;

        var units = GameObject.FindGameObjectsWithTag(tag);
        for (int i = 0; i < units.Length; i++)
        {
            var unitGO = units[i];
            if (unitGO == null) continue;

            Transform t = unitGO.transform;

            var icon = Instantiate(prefab, canvasRoot);
            icon.gameObject.SetActive(true);
            icon.SetAsLastSibling();
            dict[t] = icon;

            // Compile-safe: bind any UI script that has Bind(Transform)
            TryBindHealthUI(icon, t);

            var ring = icon.Find(selectedRingChildName);
            if (ring != null) ring.gameObject.SetActive(false);

            var btn = icon.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();

                if (clickable)
                {
                    Transform captured = t;
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

    private void ClearIconsDict(Dictionary<Transform, RectTransform> dict)
    {
        foreach (var kvp in dict)
            if (kvp.Value != null) Destroy(kvp.Value.gameObject);

        dict.Clear();
    }

    private void ClearTeamStars()
    {
        foreach (var kvp in teamStarByTeamId)
            if (kvp.Value != null) Destroy(kvp.Value.gameObject);

        teamStarByTeamId.Clear();
    }

    private void LateUpdate()
    {
        EnsureCommandCam();
        if (canvasRoot == null) return;

        bool inCommandMode = IsInCommandMode();

        UpdatePersistentIcon(playerIcon, ref playerTarget, "Player", playerIconWorldHeight, inCommandMode);
        UpdatePersistentIcon(bossIcon, ref bossTarget, "Boss", bossIconWorldHeight, inCommandMode);

        if (commandButtonPanel != null && !inCommandMode)
            commandButtonPanel.gameObject.SetActive(false);

        if (!inCommandMode)
            return;

        Camera uiCam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCam = canvas.worldCamera;

        UpdateDictPositions(allyIconByUnit, uiCam, iconWorldHeight);
        UpdateDictPositions(enemyIconByUnit, uiCam, iconWorldHeight);

        SyncTeamsAndStars(uiCam);
        UpdateSelectionHighlight();
        UpdateButtonPanelPosition(uiCam);
    }

    private void UpdateDictPositions(Dictionary<Transform, RectTransform> dict, Camera uiCam, float worldHeight)
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

    private void UpdateSelectionHighlight()
    {
        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;

        Transform selectedT = (sm.PrimarySelected != null) ? sm.PrimarySelected.transform : null;

        foreach (var kvp in allyIconByUnit)
            SetSelectedRing(kvp.Value, kvp.Key == selectedT);

        foreach (var kvp in enemyIconByUnit)
            SetSelectedRing(kvp.Value, kvp.Key == selectedT);
    }

    private void SetSelectedRing(RectTransform icon, bool on)
    {
        if (icon == null) return;
        var ring = icon.Find(selectedRingChildName);
        if (ring != null) ring.gameObject.SetActive(on);
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
                teamUI.Bind(team, null);

            Transform anchor = team.Anchor;
            if (anchor == null && team.Members.Count > 0) anchor = team.Members[0];

            if (anchor != null)
                UpdateWorldAnchoredUI(star, anchor, teamStarWorldHeight, uiCam);
            else
                star.gameObject.SetActive(false);
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
        }
    }

    // ✅ JOIN CONFIRMATION: hide the button panel and suppress it until selection changes
    private void HandleAddRequested_ForButtonPanel(IReadOnlyList<GameObject> selection, GameObject clickedUnit)
    {
        if (clickedUnit == null) return;
        if (sm == null) return;

        // Join is confirmed when:
        // JoinArmed == true, JoinSource exists, and clickedUnit != JoinSource
        if (sm.JoinArmed && sm.JoinSource != null && clickedUnit.transform != sm.JoinSource.transform)
        {
            suppressButtonPanelAfterJoinTargetChosen = true;
            suppressButtonPanelPrimarySelectionRef = sm.PrimarySelected;

            // Clear any anchor override so it doesn't "move" to the 2nd ally
            buttonPanelAnchorOverride = null;
            buttonPanelAnchorOverrideUntil = 0f;

            if (commandButtonPanel != null)
                commandButtonPanel.gameObject.SetActive(false);
        }
    }

    // ✅ When selection changes, allow the panel to come back naturally
    private void HandleSelectionChanged_ForButtonPanel(IReadOnlyList<GameObject> newSelection)
    {
        if (!suppressButtonPanelAfterJoinTargetChosen) return;
        if (sm == null) return;

        if (sm.PrimarySelected != suppressButtonPanelPrimarySelectionRef)
        {
            suppressButtonPanelAfterJoinTargetChosen = false;
            suppressButtonPanelPrimarySelectionRef = null;
        }
    }

    private void UpdateButtonPanelPosition(Camera uiCam)
    {
        if (commandButtonPanel == null) return;

        if (!IsInCommandMode())
        {
            commandButtonPanel.gameObject.SetActive(false);
            return;
        }

        // ✅ If join just completed, keep hidden until selection changes
        if (suppressButtonPanelAfterJoinTargetChosen)
        {
            commandButtonPanel.gameObject.SetActive(false);
            return;
        }

        Transform anchor = null;

        if (buttonPanelAnchorOverride != null && Time.time <= buttonPanelAnchorOverrideUntil)
        {
            anchor = buttonPanelAnchorOverride;
        }
        else
        {
            buttonPanelAnchorOverride = null;

            if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
            if (sm != null && sm.PrimarySelected != null)
            {
                var selT = sm.PrimarySelected.transform;

                if (TeamManager.Instance != null)
                {
                    var team = TeamManager.Instance.GetTeamOf(selT);
                    if (team != null && team.Anchor != null)
                        anchor = team.Anchor;
                }

                if (anchor == null)
                    anchor = selT;
            }
        }

        if (anchor == null)
        {
            commandButtonPanel.gameObject.SetActive(false);
            return;
        }

        Vector3 worldPos = anchor.position + Vector3.up * buttonPanelWorldHeight;

        Vector3 screen = (commandCam != null)
            ? commandCam.WorldToScreenPoint(worldPos)
            : Camera.main.WorldToScreenPoint(worldPos);

        if (screen.z <= 0f)
        {
            commandButtonPanel.gameObject.SetActive(false);
            return;
        }

        if (!commandButtonPanel.gameObject.activeSelf)
            commandButtonPanel.gameObject.SetActive(true);

        RectTransform parentRT = commandButtonPanel.parent as RectTransform;
        if (parentRT == null) parentRT = canvasRoot;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRT,
            new Vector2(screen.x, screen.y),
            uiCam,
            out Vector2 localPoint);

        commandButtonPanel.anchoredPosition = localPoint + buttonPanelScreenOffset;
    }

    private void OnUnitClicked(Transform unit)
    {
        if (unit == null) return;

        if (sm == null) sm = FindObjectOfType<CommandStateMachine>();
        if (sm == null) return;

        // ✅ REMOVED:
        // During join, we no longer pin the button panel to the 2nd ally.
        // The desired behavior is: hide after join target chosen.

        // ✅ If teamed ally clicked: show "in route" OR select team
        if (unit.CompareTag("Ally") && TeamManager.Instance != null)
        {
            var team = TeamManager.Instance.GetTeamOf(unit);
            if (team != null)
            {
                if (IsInRouteToTeamAnchor(unit, team))
                {
                    ShowHint("Ally in route to Team", defaultHintDuration);
                    return;
                }

                if (selectTeamWhenClickTeamedMember)
                {
                    var sel = new List<GameObject>();
                    for (int i = 0; i < team.Members.Count; i++)
                        if (team.Members[i] != null)
                            sel.Add(team.Members[i].gameObject);

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

    private void UpdateWorldAnchoredUI(RectTransform ui, Transform target, float worldHeight, Camera uiCam)
    {
        if (ui == null || target == null) return;

        Vector3 worldPos = target.position + Vector3.up * worldHeight;

        Vector3 screen = (commandCam != null)
            ? commandCam.WorldToScreenPoint(worldPos)
            : Camera.main.WorldToScreenPoint(worldPos);

        if (screen.z <= 0f)
        {
            ui.gameObject.SetActive(false);
            return;
        }

        if (!ui.gameObject.activeSelf)
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

    private void TryBindHealthUI(RectTransform icon, Transform unit)
    {
        if (icon == null || unit == null) return;

        var behaviours = icon.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in behaviours)
        {
            if (mb == null) continue;

            Type t = mb.GetType();
            MethodInfo bind = t.GetMethod(
                "Bind",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Transform) },
                null);

            if (bind != null)
            {
                try { bind.Invoke(mb, new object[] { unit }); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CommandOverlayUI] Bind(Transform) found on {t.Name} but failed: {e.Message}");
                }
                return;
            }
        }
    }

    private void UpdatePersistentIcon(
        RectTransform iconRT,
        ref Transform target,
        string autoFindTag,
        float worldHeight,
        bool inCommandMode)
    {
        if (iconRT == null) return;

        if (!inCommandMode)
        {
            if (iconRT.gameObject.activeSelf) iconRT.gameObject.SetActive(false);
            return;
        }

        if (target == null)
        {
            var go = GameObject.FindGameObjectWithTag(autoFindTag);
            if (go != null) target = go.transform;
        }

        if (target == null)
        {
            if (iconRT.gameObject.activeSelf) iconRT.gameObject.SetActive(false);
            return;
        }

        Camera uiCam2 = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCam2 = canvas.worldCamera;

        Vector3 worldPos = target.position + Vector3.up * worldHeight;

        Vector3 screen = (commandCam != null)
            ? commandCam.WorldToScreenPoint(worldPos)
            : Camera.main.WorldToScreenPoint(worldPos);

        if (screen.z <= 0f)
        {
            iconRT.gameObject.SetActive(false);
            return;
        }

        if (!iconRT.gameObject.activeSelf)
            iconRT.gameObject.SetActive(true);

        RectTransform parentRT = iconRT.parent as RectTransform;
        if (parentRT == null) parentRT = canvasRoot;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRT,
            new Vector2(screen.x, screen.y),
            uiCam2,
            out Vector2 localPoint);

        iconRT.anchoredPosition = localPoint;
    }
}
