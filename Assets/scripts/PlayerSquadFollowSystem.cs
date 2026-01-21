using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps a chosen squad in a dynamic ARC behind the player while in FPS mode.
///
/// Core idea:
/// - We create per-ally "slot" Transforms and set AllyController.target = slotTransform.
/// - AllyController only updates destination toward target when NOT chasing (combat),
///   so it naturally re-forms after combat.
///
/// Pick flow supported:
/// - Player icon -> Follow Me (arms pick mode)
/// - Next click on Ally icon (or Team Star) chooses who follows.
///
/// Comfort fixes:
/// - Personal-space clamp + increased stoppingDistance while following (prevents running into player)
/// - "Freeze while idle" so rotating in place doesn't make allies shuffle behind you
/// - BUT: initial / far-away catch-up still updates even while idle
/// - PlayerTag under AllyIcon is toggled for followers
/// </summary>
public class PlayerSquadFollowSystem : MonoBehaviour
{
    public static PlayerSquadFollowSystem Instance { get; private set; }

    /// <summary>
    /// Re-apply PlayerTag UI indicators for the current followers.
    /// Call this after CommandOverlayUI rebuilds icons or changes visibility.
    /// </summary>
    public void RefreshFollowerTagsUI()
    {
        UpdateFollowerTags();
    }


    [Header("Refs")]
    [Tooltip("Player transform. If null, will auto-find by tag 'Player'.")]
    public Transform player;

    [Header("Activation")]
    [Tooltip("If true, we automatically stop following whenever command mode is entered (so command orders don't fight with follow slots).")]
    public bool stopFollowWhenEnterCommandMode = false;

    [Tooltip("If true, follow slot updates only run while NOT in command mode. (Recommended).")]
    public bool onlyUpdateInFpsMode = true;

    [Header("Pick Mode (UI)")]
    [Tooltip("If true, picking replaces any existing follow group. If false, adds to existing follow group.")]
    public bool pickReplacesExisting = true;


    [Tooltip("If true, you can click multiple allies in a single Follow Me picking session (until you cancel it).")]
    public bool pickStaysArmedUntilCancelled = true;

    [Tooltip("If false, allies that already belong to a team cannot be picked to follow (prevents stealing from teams).")]
    public bool allowPickingTeamedAllies = false;

    [Tooltip("If true, clicking a Team Star can pick the whole team to follow. If false, team star clicks are ignored in pick mode.")]
    public bool allowPickingTeams = false;

    [Header("Pick Mode Messages")]
    public string pickBlockedTeamedAllyText = "That ally is already on a team.";
    public string pickBlockedTeamText = "You can't recruit a whole team in Follow Me mode.";


    [Tooltip("Show a hint when pick mode begins (requires HintToastUI in scene).")]
    public bool showPickHint = true;

    [Tooltip("Text shown when pick mode begins.")]
    public string pickHintText = "Click an ally (or Team Star) to follow you.";

    [Tooltip("How long the pick hint stays visible.")]
    public float pickHintDuration = 2f;

    public bool IsPickingFollowers => isPickingFollowers;
    private bool isPickingFollowers;
    private bool pickSessionHasPickedAny;

    [Header("Arc Shape")]
    [Tooltip("Base distance behind player for the first row.")]
    public float baseBehindDistance = 3.2f;

    [Tooltip("Base lateral spacing (will scale up with squad size).")]
    public float baseSpacing = 1.8f;

    [Tooltip("How much spacing grows with squad size: spacing *= (1 + spacingGrowthPerSqrtMember * sqrt(n-1)).")]
    public float spacingGrowthPerSqrtMember = 0.14f;

    [Tooltip("Max spacing multiplier from size scaling.")]
    public float maxSpacingMultiplier = 2.2f;

    [Tooltip("How many degrees wide the arc behind the player is (0..180). 120 means a wide wedge behind.")]
    [Range(20f, 180f)]
    public float arcDegrees = 120f;

    [Tooltip("Extra distance between rows (multiplier of spacing).")]
    public float rowDistanceMultiplier = 1.15f;

    [Header("NavMesh")]
    [Tooltip("NavMesh sample radius for slot points.")]
    public float navmeshSampleRadius = 2.5f;

    [Tooltip("If true, when a slot can't be sampled on NavMesh, fall back to raw point (still may work if agent can reach).")]
    public bool allowRawFallbackWhenNoNavmesh = true;

    [Header("Follow Distance")]
    [Tooltip("NavMeshAgent stopping distance to use while following slots (prevents face-hugging the player).")]
    public float followStoppingDistance = 1.6f;

    [Tooltip("Extra personal space added on top of stopping distance when building slot radii (prevents slots too close to the player).")]
    public float playerPersonalSpace = 0.8f;

    [Tooltip("Minimum radius behind player regardless of other settings.")]
    public float minBehindRadius = 2.6f;

    [Header("Smoothing")]
    [Tooltip("How quickly slot transforms move toward their desired positions (higher = snappier).")]
    public float slotLerpSpeed = 14f;

    [Tooltip("If player isn't moving much, keep slots tighter.")]
    public bool tightenWhenPlayerIdle = true;

    [Tooltip("Player speed (approx) below which we consider idle.")]
    public float playerIdleSpeedThreshold = 0.15f;

    [Tooltip("If true, we do NOT recompute slot positions while player is idle. This prevents allies from re-forming just because you rotate in place.")]
    public bool freezeSlotsWhenPlayerIdle = true;

    [Tooltip("If freezeSlotsWhenPlayerIdle is false, and this is true, formation heading uses last movement direction (not look direction) while idle.")]
    public bool useLastMoveDirectionWhenIdle = true;

    [Tooltip("Multiplier on baseBehindDistance when player is idle.")]
    public float idleDistanceMultiplier = 0.75f;

    [Tooltip("Multiplier on spacing when player is idle.")]
    public float idleSpacingMultiplier = 0.75f;

    [Header("Catch-up")]
    [Tooltip("If any follower is farther than this from the player, we keep updating slot positions even if the player is idle (so stragglers still run to you).")]
    public float catchUpDistance = 12f;

    [Tooltip("After starting follow, we force slot updates for a few frames even if idle (so followers immediately move toward you when exiting command mode).")]
    public int forceUpdateFramesOnBegin = 6;

    private int forceUpdateFramesLeft;

    // Followers + slots
    private readonly List<Transform> followers = new();
    private readonly Dictionary<Transform, Transform> slotByFollower = new(); // follower -> slot transform

    // UI tagging (PlayerTag under AllyIcon)
    private readonly HashSet<Transform> taggedFollowers = new();

    // Restore agent stopping distance after follow
    private readonly Dictionary<Transform, float> originalStoppingDistanceByFollower = new();

    // Track last player position to estimate speed
    private Vector3 lastPlayerPos;
    private bool hasLastPlayerPos;

    // Track last movement direction so rotating-in-place doesn't move "behind"
    private Vector3 lastMoveDir = Vector3.forward;
    private bool hasLastMoveDir;

    // Hint toast
    private HintToastUI hintToastUI;
    private Coroutine hintHideRoutine;

    public static PlayerSquadFollowSystem EnsureExists()
    {
        if (Instance != null) return Instance;

        var existing = FindObjectOfType<PlayerSquadFollowSystem>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("PlayerSquadFollowSystem");
        Instance = go.AddComponent<PlayerSquadFollowSystem>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }

        if (player != null)
        {
            lastPlayerPos = player.position;
            hasLastPlayerPos = true;

            Vector3 fwd = player.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            lastMoveDir = fwd.normalized;
            hasLastMoveDir = true;
        }
    }

    public void SetPlayer(Transform playerTransform)
    {
        player = playerTransform;
        if (player != null)
        {
            lastPlayerPos = player.position;
            hasLastPlayerPos = true;

            Vector3 fwd = player.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            lastMoveDir = fwd.normalized;
            hasLastMoveDir = true;
        }
    }

    // -------------------- PICK MODE --------------------

    public void ArmPickFollowers()
    {

        // Force multi-pick session: stay armed until user cancels (click Follow Me again / Esc / RMB if you add it).
        pickStaysArmedUntilCancelled = true;

        // Do not allow picking allies that already belong to a team.
        allowPickingTeamedAllies = false;

        allowPickingTeams = false;

        // Toggle behavior: clicking Follow Me again cancels pick mode.
        if (isPickingFollowers)
        {
            DisarmPickFollowers();
            return;
        }

        isPickingFollowers = true;
        pickSessionHasPickedAny = false;

        if (showPickHint)
            ShowHint(pickHintText, pickHintDuration);
    }

    public void DisarmPickFollowers()
    {
        isPickingFollowers = false;
        pickSessionHasPickedAny = false;
    }

    public void ConsumePickOnAlly(Transform ally)
    {
        if (!isPickingFollowers) return;
        if (ally == null) return;

        // Block picking allies that already belong to a team (optional).
        if (!allowPickingTeamedAllies && TeamManager.Instance != null)
        {
            var t = TeamManager.Instance.GetTeamOf(ally);
            if (t != null)
            {
                if (!string.IsNullOrWhiteSpace(pickBlockedTeamedAllyText))
                    ShowHint(pickBlockedTeamedAllyText, pickHintDuration);
                return;
            }
        }

        // Build the new follow list. If pickReplacesExisting is on, the first pick in a session starts a fresh list.
        List<Transform> newList;
        if (pickReplacesExisting && !pickSessionHasPickedAny)
            newList = new List<Transform>();
        else
            newList = new List<Transform>(followers);

        // Toggle: click again to remove, click to add.
        if (newList.Contains(ally))
            newList.Remove(ally);
        else
            newList.Add(ally);

        newList.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        if (newList.Count == 0)
            StopFollow(stopAgents: false);
        else
            BeginFollow(newList);

        pickSessionHasPickedAny = true;

        if (!pickStaysArmedUntilCancelled)
            isPickingFollowers = false;
    }

    public void ConsumePickOnTeam(List<Transform> teamMembers)
    {
        if (!isPickingFollowers) return;

        if (!allowPickingTeams)
        {
            if (!string.IsNullOrWhiteSpace(pickBlockedTeamText))
                ShowHint(pickBlockedTeamText, pickHintDuration);

            if (!pickStaysArmedUntilCancelled)
                isPickingFollowers = false;

            return;
        }

        if (teamMembers == null || teamMembers.Count == 0) return;

        // Sort for determinism.
        teamMembers.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        List<Transform> newList;
        if (pickReplacesExisting && !pickSessionHasPickedAny)
            newList = new List<Transform>();
        else
            newList = new List<Transform>(followers);

        // If any member isn't already a follower, add all. Otherwise remove all (toggle).
        bool anyNotFollower = false;
        for (int i = 0; i < teamMembers.Count; i++)
        {
            var m = teamMembers[i];
            if (m == null) continue;
            if (!newList.Contains(m)) { anyNotFollower = true; break; }
        }

        for (int i = 0; i < teamMembers.Count; i++)
        {
            var m = teamMembers[i];
            if (m == null) continue;

            if (anyNotFollower)
            {
                if (!newList.Contains(m)) newList.Add(m);
            }
            else
            {
                newList.Remove(m);
            }
        }

        newList.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        if (newList.Count == 0)
            StopFollow(stopAgents: false);
        else
            BeginFollow(newList);

        pickSessionHasPickedAny = true;

        if (!pickStaysArmedUntilCancelled)
            isPickingFollowers = false;
    }

    // -------------------- PUBLIC FOLLOW API --------------------

    /// <summary>
    /// Start following with a specific set of followers (chosen allies).
    /// Calling this again replaces the current follow group.
    /// </summary>
    public void BeginFollow(List<Transform> followerTransforms)
    {
        if (followerTransforms == null) followerTransforms = new List<Transform>();

        ReplaceFollowers(followerTransforms);
        AssignTargetsToSlots();
        ApplyFollowStoppingDistance();
        UpdateFollowerTags();

        // Force a few frames of updates so followers immediately start moving even if player is idle.
        forceUpdateFramesLeft = Mathf.Max(0, forceUpdateFramesOnBegin);

        // Seed lastMoveDir (used when idle)
        if (!hasLastMoveDir && player != null)
        {
            Vector3 fwd = player.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            lastMoveDir = fwd.normalized;
            hasLastMoveDir = true;
        }

        enabled = true;
    }

    public void BeginFollow_AllAllies()
    {
        var list = new List<Transform>();
        var allies = GameObject.FindGameObjectsWithTag("Ally");
        for (int i = 0; i < allies.Length; i++)
        {
            var go = allies[i];
            if (go == null) continue;
            list.Add(go.transform);
        }

        list.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
        BeginFollow(list);
    }

    public void StopFollow(bool stopAgents = false)
    {
        // Disable PlayerTag on previous followers
        var ui = CommandOverlayUI.Instance;
        if (ui != null)
        {
            foreach (var f in taggedFollowers)
                if (f != null) ui.SetPlayerFollowerTag(f, false);
        }
        taggedFollowers.Clear();

        // Clear follow targets
        for (int i = 0; i < followers.Count; i++)
        {
            var f = followers[i];
            if (f == null) continue;

            var ally = f.GetComponent<AllyController>();
            if (ally != null)
            {
                // Only clear if it's one of our slots (prevents nuking other follow systems).
                if (ally.target != null && IsOurSlot(ally.target))
                    ally.target = null;
            }

            if (stopAgents)
            {
                var agent = GetAgent(f);
                if (agent != null)
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                }
            }
        }

        RestoreStoppingDistance();

        followers.Clear();
        slotByFollower.Clear();

        enabled = false;
    }

    // -------------------- UPDATE LOOP --------------------

    private void Update()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
            if (player == null) return;
        }

        bool commandMode = (CommandCamToggle.Instance != null && CommandCamToggle.Instance.IsCommandMode);

        // If we're in "pick followers" mode, cancel it when leaving command mode or on Escape / Right-Click.
        if (isPickingFollowers)
        {
            if (!commandMode)
            {
                // Can't pick followers outside of command view.
                DisarmPickFollowers();
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                    DisarmPickFollowers();
            }
        }


        if (stopFollowWhenEnterCommandMode && commandMode && followers.Count > 0 && !isPickingFollowers)
        {
            StopFollow(stopAgents: false);
            return;
        }

        if (onlyUpdateInFpsMode && commandMode)
            return;

        if (followers.Count == 0)
            return;

        // Estimate player speed and movement direction
        float playerSpeed = 0f;
        Vector3 moveDir = Vector3.zero;

        if (hasLastPlayerPos)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 delta = (player.position - lastPlayerPos);
            delta.y = 0f;

            playerSpeed = delta.magnitude / dt;
            if (delta.sqrMagnitude > 0.000001f)
                moveDir = delta.normalized;
        }

        // Cache last movement direction ONLY when actually moving.
        if (playerSpeed >= playerIdleSpeedThreshold && moveDir.sqrMagnitude > 0.000001f)
        {
            lastMoveDir = moveDir;
            hasLastMoveDir = true;
        }

        lastPlayerPos = player.position;
        hasLastPlayerPos = true;

        // Calculate spacing scaling with team size
        float sizeScale = 1f;
        if (followers.Count > 1)
        {
            sizeScale = 1f + spacingGrowthPerSqrtMember * Mathf.Sqrt(followers.Count - 1);
            sizeScale = Mathf.Min(sizeScale, Mathf.Max(1f, maxSpacingMultiplier));
        }

        float spacing = baseSpacing * sizeScale;
        float behindDist = baseBehindDistance;

        if (tightenWhenPlayerIdle && playerSpeed < playerIdleSpeedThreshold)
        {
            spacing *= Mathf.Max(0.1f, idleSpacingMultiplier);
            behindDist *= Mathf.Max(0.1f, idleDistanceMultiplier);
        }

        // Freeze while idle (to avoid re-forming on rotation),
        // BUT never freeze during initial "pull in" frames or if anyone is far away.
        bool shouldFreeze = freezeSlotsWhenPlayerIdle && playerSpeed < playerIdleSpeedThreshold;

        if (forceUpdateFramesLeft > 0)
        {
            shouldFreeze = false;
            forceUpdateFramesLeft--;
        }
        else if (NeedCatchUp())
        {
            shouldFreeze = false;
        }

        if (shouldFreeze)
            return;

        // Heading for the arc:
        // - While moving: use movement direction (not look direction)
        // - While idle: optionally use last movement direction
        Vector3 formationForward = player.forward;

        if (playerSpeed >= playerIdleSpeedThreshold && moveDir.sqrMagnitude > 0.000001f)
            formationForward = moveDir;
        else if (playerSpeed < playerIdleSpeedThreshold && useLastMoveDirectionWhenIdle && hasLastMoveDir)
            formationForward = lastMoveDir;

        ComputeAndApplySlotPositions(player.position, formationForward, spacing, behindDist);
    }

    // -------------------- INTERNALS --------------------

    private bool NeedCatchUp()
    {
        if (followers.Count == 0 || player == null) return false;
        float catchSqr = catchUpDistance * catchUpDistance;

        for (int i = 0; i < followers.Count; i++)
        {
            var f = followers[i];
            if (f == null) continue;

            Vector3 d = f.position - player.position;
            d.y = 0f;

            if (d.sqrMagnitude > catchSqr)
                return true;
        }

        return false;
    }

    private bool IsOurSlot(Transform t)
    {
        if (t == null) return false;
        if (t.parent != transform) return false;
        return t.name.StartsWith("FollowSlot_");
    }

    private void ReplaceFollowers(List<Transform> newFollowers)
    {
        // Clear targets for anyone who is no longer following
        if (followers.Count > 0)
        {
            var newSet = new HashSet<Transform>();
            for (int i = 0; i < newFollowers.Count; i++)
                if (newFollowers[i] != null) newSet.Add(newFollowers[i]);

            for (int i = 0; i < followers.Count; i++)
            {
                var oldF = followers[i];
                if (oldF == null) continue;
                if (newSet.Contains(oldF)) continue;

                var ally = oldF.GetComponent<AllyController>();
                if (ally != null && ally.target != null && IsOurSlot(ally.target))
                    ally.target = null;
            }
        }

        // We'll re-apply stopping distances after replacing followers
        RestoreStoppingDistance();

        followers.Clear();
        slotByFollower.Clear();

        // Clean null entries + duplicates
        var seen = new HashSet<Transform>();
        for (int i = 0; i < newFollowers.Count; i++)
        {
            var f = newFollowers[i];
            if (f == null) continue;
            if (!seen.Add(f)) continue;

            // Avoid adding player as follower
            if (player != null && f == player) continue;

            followers.Add(f);
        }

        // Stable order so slots don't shuffle.
        followers.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        EnsureSlotsForFollowers();
        UpdateFollowerTags();
    }

    private void AssignTargetsToSlots()
    {
        for (int i = 0; i < followers.Count; i++)
        {
            var f = followers[i];
            if (f == null) continue;
            if (!slotByFollower.TryGetValue(f, out var slot) || slot == null) continue;

            var ally = f.GetComponent<AllyController>();
            if (ally != null)
                ally.target = slot;
        }
    }

    private void EnsureSlotsForFollowers()
    {
        for (int i = followers.Count - 1; i >= 0; i--)
        {
            if (followers[i] == null) followers.RemoveAt(i);
        }

        for (int i = 0; i < followers.Count; i++)
        {
            var f = followers[i];
            if (f == null) continue;

            if (!slotByFollower.TryGetValue(f, out var slot) || slot == null)
            {
                var slotGo = new GameObject($"FollowSlot_{f.name}_{f.GetInstanceID()}");
                slotGo.hideFlags = HideFlags.DontSave;
                slotGo.transform.SetParent(transform, false);
                slotGo.transform.position = f.position;

                slotByFollower[f] = slotGo.transform;
            }
        }
    }

    private void ComputeAndApplySlotPositions(Vector3 playerPos, Vector3 playerForward, float spacing, float behindDist)
    {
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f)
            playerForward = Vector3.forward;

        playerForward.Normalize();

        Vector3 behindDir = -playerForward;
        Vector3 rightDir = Vector3.Cross(Vector3.up, behindDir).normalized;

        int remaining = followers.Count;
        int index = 0;
        int row = 0;

        float halfArc = Mathf.Clamp(arcDegrees * 0.5f, 10f, 90f);

        while (remaining > 0)
        {
            row++;

            int rowCapacity = row; // 1,2,3,...
            int take = Mathf.Min(rowCapacity, remaining);

            float radius = behindDist + (row - 1) * spacing * rowDistanceMultiplier;

            // Prevent slots from ever being too close to the player (avoids running into you)
            float minRadius = Mathf.Max(minBehindRadius, followStoppingDistance + playerPersonalSpace);
            if (radius < minRadius) radius = minRadius;

            for (int j = 0; j < take; j++)
            {
                if (index >= followers.Count) break;

                float t = (take == 1) ? 0f : (j / (float)(take - 1));
                float angle = Mathf.Lerp(-halfArc, halfArc, t);

                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * behindDir;

                // Small lateral nudge so the very first row isn't perfectly centered every time
                float lateralJitter = 0f;
                if (row == 1 && take == 1)
                    lateralJitter = 0.15f * spacing;

                Vector3 desired = playerPos + dir * radius + rightDir * lateralJitter;
                desired.y = playerPos.y;

                Vector3 finalPos = desired;
                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, Mathf.Max(0.1f, navmeshSampleRadius), NavMesh.AllAreas))
                    finalPos = hit.position;
                else if (!allowRawFallbackWhenNoNavmesh)
                    finalPos = playerPos;

                var follower = followers[index];
                if (follower != null && slotByFollower.TryGetValue(follower, out var slot) && slot != null)
                {
                    float lerpT = 1f - Mathf.Exp(-slotLerpSpeed * Time.deltaTime);
                    slot.position = Vector3.Lerp(slot.position, finalPos, lerpT);
                }

                index++;
                remaining--;
            }
        }
    }

    // -------------------- UI TAGGING --------------------

    private void UpdateFollowerTags()
    {
        var ui = CommandOverlayUI.Instance;
        if (ui == null) return;

        // Turn off tags that are no longer followers
        foreach (var oldF in taggedFollowers)
        {
            if (oldF == null) continue;
            if (!followers.Contains(oldF))
                ui.SetPlayerFollowerTag(oldF, false);
        }

        taggedFollowers.Clear();

        // Turn on tags for current followers
        for (int i = 0; i < followers.Count; i++)
        {
            var f = followers[i];
            if (f == null) continue;
            taggedFollowers.Add(f);
            ui.SetPlayerFollowerTag(f, true);
        }
    }

    // -------------------- AGENT HELPERS --------------------

    private NavMeshAgent GetAgent(Transform t)
    {
        if (t == null) return null;
        var agent = t.GetComponent<NavMeshAgent>();
        if (agent == null) agent = t.GetComponentInChildren<NavMeshAgent>();
        if (agent == null) return null;
        if (!agent.isActiveAndEnabled) return null;
        return agent;
    }

    private void ApplyFollowStoppingDistance()
    {
        for (int i = 0; i < followers.Count; i++)
        {
            var f = followers[i];
            if (f == null) continue;

            var agent = GetAgent(f);
            if (agent == null) continue;

            if (!originalStoppingDistanceByFollower.ContainsKey(f))
                originalStoppingDistanceByFollower[f] = agent.stoppingDistance;

            agent.stoppingDistance = Mathf.Max(agent.stoppingDistance, followStoppingDistance);
        }
    }

    private void RestoreStoppingDistance()
    {
        foreach (var kvp in originalStoppingDistanceByFollower)
        {
            var f = kvp.Key;
            if (f == null) continue;

            var agent = GetAgent(f);
            if (agent == null) continue;

            agent.stoppingDistance = kvp.Value;
        }

        originalStoppingDistanceByFollower.Clear();
    }

    // -------------------- HINT TOAST --------------------

    private void AutoFindHintToastUI()
    {
        if (hintToastUI != null) return;

        var all = Resources.FindObjectsOfTypeAll<HintToastUI>();
        for (int i = 0; i < all.Length; i++)
        {
            var h = all[i];
            if (h == null) continue;
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
}