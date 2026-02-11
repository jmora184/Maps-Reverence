using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps a chosen squad in a dynamic formation relative to the player while in FPS mode.
///
/// Core idea:
/// - We create per-ally "slot" Transforms and set AllyController.target = slotTransform.
/// - AllyController only updates destination toward target when NOT chasing (combat),
///   so it naturally re-forms after combat.
///
/// Supports formations:
/// - ArcBehind (classic follow arc)
/// - LineFront (line in front of player)
/// - WedgeFront (wedge in front of player)
///
/// Upgrades:
/// - Optional camera-aim orientation: formation faces where the camera aims (not player body)
/// - Optional "hold the line": periodic snap/pulse to keep formation tight while moving
/// - Optional smart slot assignment: prevents criss-crossing by matching allies to closest slots
/// </summary>
public class PlayerSquadFollowSystem : MonoBehaviour
{
    public static PlayerSquadFollowSystem Instance { get; private set; }

    public enum FollowFormation
    {
        ArcBehind,
        LineFront,
        WedgeFront
    }

    [Header("Formation Mode")]
    [Tooltip("How followers arrange around the player while following.")]
    public FollowFormation formation = FollowFormation.ArcBehind;

    /// <summary>
    /// Change follower formation. Safe to call even if there are no followers yet.
    /// </summary>
    public void SetFormation(FollowFormation newFormation)
    {
        formation = newFormation;

        // Snap into the new shape immediately.
        forceUpdateFramesLeft = Mathf.Max(forceUpdateFramesLeft, forceUpdateFramesOnBegin);

        // Force a re-assignment pass so we avoid criss-crossing on a big re-shape.
        forceReassignNow = true;
    }

    public void SetFormationArcBehind() => SetFormation(FollowFormation.ArcBehind);
    public void SetFormationLineFront() => SetFormation(FollowFormation.LineFront);
    public void SetFormationWedgeFront() => SetFormation(FollowFormation.WedgeFront);

    /// <summary>Convenience: clears any active formation choice and returns to the default Arc-Behind follow.</summary>
    public void CancelFormationToNormalFollow() => SetFormationArcBehind();

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

    [Header("Orientation")]
    [Tooltip("If true, the formation faces where the camera aims (recommended for FPS). If false, uses player movement direction / player forward.")]
    public bool useCameraAimForward = true;

    [Tooltip("Optional override for aim direction source. If null and useCameraAimForward is true, uses Camera.main.")]
    public Transform aimForwardSource;

    [Tooltip("If true, rotating the camera while standing still will still update the formation heading (for Line/Wedge).")]
    public bool allowAimRotateWhileIdle = true;

    [Tooltip("Degrees of aim-rotation change required (while idle) to trigger an update. Prevents micro-jitter.")]
    [Range(0.5f, 20f)]
    public float idleAimRotateThresholdDegrees = 3.0f;


    [Header("Idle Heading Lock")]
    [Tooltip("If true, ArcBehind formation will NOT change its heading just because you rotate your aim while standing still. The heading updates again once you move.")]
    public bool lockArcBehindHeadingWhileIdle = true;

    [Tooltip("If true, when the player is idle we keep using the last 'stable' heading even if followers are far away and need catch-up. Prevents followers from rushing around you when you only look around.")]
    public bool useLockedHeadingDuringCatchUp = true;

    [Header("Arc Shape")]
    [Tooltip("Base distance behind player for the first row.")]
    public float baseBehindDistance = 4.2f;

    [Tooltip("Base lateral spacing (will scale up with squad size).")]
    public float baseSpacing = 2.2f;

    [Tooltip("How much spacing grows with squad size: spacing *= (1 + spacingGrowthPerSqrtMember * sqrt(n-1)).")]
    public float spacingGrowthPerSqrtMember = 0.14f;

    [Tooltip("Max spacing multiplier from size scaling.")]
    public float maxSpacingMultiplier = 2.2f;

    [Tooltip("How many degrees wide the arc behind the player is (0..180). 120 means a wide wedge behind.")]
    [Range(20f, 180f)]
    public float arcDegrees = 120f;

    [Tooltip("Extra distance between rows (multiplier of spacing).")]
    public float rowDistanceMultiplier = 1.15f;

    [Header("Front Line / Wedge")]
    [Tooltip("How far in FRONT of the player the line/wedge starts (meters).")]
    public float baseFrontDistance = 4.2f;

    [Tooltip("Minimum front distance regardless of settings (meters).")]
    public float minFrontDistance = 3.0f;

    [Tooltip("Spacing multiplier for LINE formation only.")]
    public float lineSpacingMultiplier = 1.0f;

    [Tooltip("How far each wedge row steps back from the tip (multiplier of spacing).")]
    public float wedgeRowBackMultiplier = 1.0f;

    [Header("NavMesh")]
    [Tooltip("NavMesh sample radius for slot points.")]
    public float navmeshSampleRadius = 2.5f;

    [Tooltip("If true, when a slot can't be sampled on NavMesh, fall back to raw point (still may work if agent can reach).")]
    public bool allowRawFallbackWhenNoNavmesh = true;

    [Header("Follow Distance")]
    [Tooltip("NavMeshAgent stopping distance to use while following slots (prevents face-hugging the player).")]
    public float followStoppingDistance = 2.4f;

    [Tooltip("Extra personal space added on top of stopping distance when building slot radii (prevents slots too close to the player).")]
    public float playerPersonalSpace = 1.2f;

    [Tooltip("Minimum radius behind player regardless of other settings.")]
    public float minBehindRadius = 3.4f;

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

    [Header("Hold the Line")]
    [Tooltip("If true, periodically 'pulses' the slot movement to snap the formation tighter (helps line stay crisp while moving).")]
    public bool enableReformPulse = true;

    [Tooltip("How often (seconds) to apply a brief snap/pulse update. 0 disables.")]
    [Range(0f, 2f)]
    public float reformPulseInterval = 0.45f;

    [Tooltip("How many frames to snap/pulse when reforming.")]
    [Range(0, 10)]
    public int reformPulseFrames = 2;

    private float nextReformPulseTime;

    [Header("Smart Slot Assignment")]
    [Tooltip("If true, allies are assigned to the nearest slots to avoid criss-crossing when you change heading or formation.")]
    public bool enableSmartAssignment = true;

    [Tooltip("How often (seconds) we recompute the best follower->slot mapping. Lower = more responsive, higher = more stable.")]
    [Range(0.05f, 2f)]
    public float smartAssignmentInterval = 0.25f;

    [Tooltip("If the formation heading changes by at least this many degrees, we immediately recompute mapping.")]
    [Range(1f, 90f)]
    public float smartReassignAngleThreshold = 12f;

    private float lastSmartAssignTime;
    private Vector3 lastAssignedForward = Vector3.forward;
    private bool hasLastAssignedForward;
    private bool forceReassignNow;

    private bool isCatchUpUpdate;
    // Followers + slots
    private readonly List<Transform> followers = new();

    /// <summary>How many allies are currently following the player via this system.</summary>
    public int FollowerCount => followers.Count;

    private readonly Dictionary<Transform, Transform> slotByFollower = new(); // follower -> slot transform

    // follower -> desired slot index (0..n-1)
    private readonly Dictionary<Transform, int> assignedIndexByFollower = new();

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

    // Track last aim direction to detect idle rotate changes
    private Vector3 lastAimDir = Vector3.forward;
    private bool hasLastAimDir;


    // Cached heading while idle (prevents "rush behind me" when only rotating aim)
    private Vector3 lockedIdleForward = Vector3.forward;
    private bool hasLockedIdleForward;

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
            lockedIdleForward = lastMoveDir;
            hasLockedIdleForward = true;

        }

        // Seed aim dir
        Vector3 aim = GetAimForwardFlat();
        if (aim.sqrMagnitude > 0.0001f)
        {
            lastAimDir = aim.normalized;
            hasLastAimDir = true;
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
            lockedIdleForward = lastMoveDir;
            hasLockedIdleForward = true;

        }

        Vector3 aim = GetAimForwardFlat();
        if (aim.sqrMagnitude > 0.0001f)
        {
            lastAimDir = aim.normalized;
            hasLastAimDir = true;
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
        // If we already have followers, treat a new pick session as 'add/remove' so
        // the first click won't wipe the existing group when returning from FPS mode.
        // To truly replace the group, use your StopFollow button/flow first.
        pickSessionHasPickedAny = (followers != null && followers.Count > 0);

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


    /// <summary>
    /// Adds a single ally to the current follower list (without requiring Pick Mode).
    /// Used by command-mode JOIN -> Player flow.
    /// Returns true if the ally was added, false if blocked/already following.
    /// </summary>
    public bool TryAddFollowerDirect(Transform ally, bool showBlockedHint = true)
    {
        if (ally == null) return false;

        // Block allies that already belong to a team (keeps team system separate from player followers).
        if (TeamManager.Instance != null)
        {
            var t = TeamManager.Instance.GetTeamOf(ally);
            if (t != null)
            {
                if (showBlockedHint && !string.IsNullOrWhiteSpace(pickBlockedTeamedAllyText))
                    ShowHint(pickBlockedTeamedAllyText, pickHintDuration);
                return false;
            }
        }

        // Already following
        if (followers.Contains(ally))
            return false;

        var newList = new List<Transform>(followers) { ally };
        newList.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        BeginFollow(newList);
        return true;
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
            lockedIdleForward = lastMoveDir;
            hasLockedIdleForward = true;

        }

        // Seed aim dir
        Vector3 aim = GetAimForwardFlat();
        if (aim.sqrMagnitude > 0.0001f)
        {
            lastAimDir = aim.normalized;
            hasLastAimDir = true;
        }

        // Force a mapping now so the initial arrangement is clean.
        forceReassignNow = true;
        lastSmartAssignTime = -999f;

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
        assignedIndexByFollower.Clear();


        hasLockedIdleForward = false;
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
        }// Update the cached idle heading once the player actually moves.
         // This becomes the "stable" behind direction used while standing still.
        if (playerSpeed >= playerIdleSpeedThreshold)
        {
            Vector3 stable = Vector3.zero;

            if (useCameraAimForward)
            {
                stable = GetAimForwardFlat();
            }

            if (stable.sqrMagnitude < 0.0001f)
            {
                // Fall back to actual movement direction, then player forward.
                if (moveDir.sqrMagnitude > 0.000001f) stable = moveDir;
                else stable = player.forward;
                stable.y = 0f;
            }

            if (stable.sqrMagnitude < 0.0001f) stable = Vector3.forward;

            lockedIdleForward = stable.normalized;
            hasLockedIdleForward = true;
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

        // Determine formation heading
        Vector3 formationForward = ResolveFormationForward(playerSpeed, moveDir);

        // Determine whether we should freeze updates (idle freeze), with overrides:
        // - Never freeze during initial pull-in frames or if someone needs catch-up.
        // - For front formations with camera aim: if you rotate aim enough while idle, we update.
        bool needsCatchUp = NeedCatchUp();

        isCatchUpUpdate = needsCatchUp;
        bool shouldFreeze = freezeSlotsWhenPlayerIdle && playerSpeed < playerIdleSpeedThreshold;

        if (forceUpdateFramesLeft > 0)
        {
            shouldFreeze = false;
            forceUpdateFramesLeft--;
        }
        else if (needsCatchUp)
        {
            shouldFreeze = false;
        }
        else if (shouldFreeze && allowAimRotateWhileIdle && useCameraAimForward && (formation == FollowFormation.LineFront || formation == FollowFormation.WedgeFront))
        {
            // Only unfreeze if the aim direction changed enough while idle.
            Vector3 aim = GetAimForwardFlat();
            if (aim.sqrMagnitude > 0.0001f)
            {
                aim.Normalize();
                if (!hasLastAimDir)
                {
                    lastAimDir = aim;
                    hasLastAimDir = true;
                }
                float ang = Vector3.Angle(lastAimDir, aim);
                if (ang >= idleAimRotateThresholdDegrees)
                {
                    shouldFreeze = false;
                    lastAimDir = aim;
                }
            }
        }

        if (shouldFreeze)
            return;

        // Hold-the-line pulse: periodically snap a couple frames to keep formation crisp.
        bool snapThisFrame = false;
        if (enableReformPulse && reformPulseInterval > 0f && Time.time >= nextReformPulseTime)
        {
            nextReformPulseTime = Time.time + reformPulseInterval;
            if (reformPulseFrames > 0)
            {
                forceSnapFramesLeft = Mathf.Max(forceSnapFramesLeft, reformPulseFrames);
            }
        }

        if (forceSnapFramesLeft > 0)
        {
            snapThisFrame = true;
            forceSnapFramesLeft--;
        }

        ComputeAndApplySlotPositions(player.position, formationForward, spacing, behindDist, snapThisFrame);
    }

    // -------------------- INTERNALS --------------------

    private int forceSnapFramesLeft;

    private Vector3 ResolveFormationForward(float playerSpeed, Vector3 moveDir)
    {
        bool isIdle = playerSpeed < playerIdleSpeedThreshold;

        // --- Idle heading lock (ArcBehind) ---
        // Prevent followers from "rushing behind you" just because you rotate your aim while standing still.
        if (isIdle && lockArcBehindHeadingWhileIdle && formation == FollowFormation.ArcBehind && (!isCatchUpUpdate || useLockedHeadingDuringCatchUp))
        {
            if (hasLockedIdleForward && lockedIdleForward.sqrMagnitude > 0.0001f)
                return lockedIdleForward.normalized;

            // Fallback if we haven't cached yet
            Vector3 fallback = Vector3.zero;

            if (useLastMoveDirectionWhenIdle && hasLastMoveDir)
                fallback = lastMoveDir;
            else if (player != null)
                fallback = player.forward;

            fallback.y = 0f;
            if (fallback.sqrMagnitude < 0.0001f) fallback = Vector3.forward;

            lockedIdleForward = fallback.normalized;
            hasLockedIdleForward = true;
            return lockedIdleForward;
        }

        // Prefer camera aim if enabled
        if (useCameraAimForward)
        {
            Vector3 aim = GetAimForwardFlat();
            if (aim.sqrMagnitude > 0.0001f)
            {
                aim.Normalize();

                // Only treat aim as the driving heading while idle if allowed (front formations).
                // For ArcBehind we handled the idle case above.
                lastAimDir = aim;
                hasLastAimDir = true;

                return aim;
            }
        }

        // Fallback: movement direction when moving; otherwise last movement direction or player forward.
        Vector3 formationForward = player != null ? player.forward : Vector3.forward;

        if (!isIdle && moveDir.sqrMagnitude > 0.000001f)
            formationForward = moveDir;
        else if (isIdle && useLastMoveDirectionWhenIdle && hasLastMoveDir)
            formationForward = lastMoveDir;

        formationForward.y = 0f;
        if (formationForward.sqrMagnitude < 0.0001f) formationForward = Vector3.forward;
        return formationForward.normalized;
    }


    private Vector3 GetAimForwardFlat()
    {
        Transform src = aimForwardSource;

        if (src == null && useCameraAimForward)
        {
            // Camera.main is ok here; you can override with aimForwardSource if you prefer.
            var cam = Camera.main;
            if (cam != null) src = cam.transform;
        }

        if (src == null) return Vector3.zero;

        Vector3 f = src.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) return Vector3.zero;
        return f.normalized;
    }

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

        // Stable order so we have a consistent baseline.
        followers.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        EnsureSlotsForFollowers();
        CleanupAssignmentForMissingFollowers();
        UpdateFollowerTags();

        // Force a clean assignment for the new group.
        forceReassignNow = true;
    }

    private void CleanupAssignmentForMissingFollowers()
    {
        // Remove mapping entries for followers that no longer exist.
        var toRemove = new List<Transform>();
        foreach (var kvp in assignedIndexByFollower)
        {
            if (kvp.Key == null || !followers.Contains(kvp.Key))
                toRemove.Add(kvp.Key);
        }
        for (int i = 0; i < toRemove.Count; i++)
            assignedIndexByFollower.Remove(toRemove[i]);
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
            {
                // If the ally is currently executing a manual move/command (e.g., just told the team to move),
                // do NOT overwrite their travel with a follow-slot target. This is what caused "new team move"
                // to be ignored: the follower kept chasing the leader/slot instead of the newly commanded destination.
                if (ally.HasManualHoldPoint)
                {
                    // If we were already targeting our slot, clear it so AllyController can follow the manual hold.
                    if (ally.target == slot) ally.target = null;
                    continue;
                }

                ally.target = slot;
            }
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

    private void ComputeAndApplySlotPositions(Vector3 playerPos, Vector3 playerForward, float spacing, float baseDist, bool snapThisFrame)
    {
        if (followers == null || followers.Count == 0)
            return;

        // Flatten forward
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.0001f)
            playerForward = Vector3.forward;

        playerForward.Normalize();

        // Common dirs
        Vector3 fwd = playerForward;
        Vector3 rightDir = Vector3.Cross(Vector3.up, fwd).normalized;
        if (rightDir.sqrMagnitude < 0.0001f) rightDir = Vector3.right;

        // Build desired positions for the current formation
        var desiredPositions = BuildDesiredPositions(playerPos, fwd, rightDir, spacing, baseDist);
        if (desiredPositions == null || desiredPositions.Count != followers.Count)
            return;

        // Decide whether to recompute assignment mapping
        bool needReassign = forceReassignNow;

        float now = Time.time;
        if (!needReassign && enableSmartAssignment)
        {
            if ((now - lastSmartAssignTime) >= smartAssignmentInterval)
                needReassign = true;

            if (hasLastAssignedForward)
            {
                float ang = Vector3.Angle(lastAssignedForward, fwd);
                if (ang >= smartReassignAngleThreshold)
                    needReassign = true;
            }
        }

        if (!enableSmartAssignment)
            needReassign = false;

        if (needReassign)
        {
            ReassignFollowersToSlots(desiredPositions);
            lastSmartAssignTime = now;
            lastAssignedForward = fwd;
            hasLastAssignedForward = true;
            forceReassignNow = false;
        }
        else
        {
            // Ensure mapping exists for everyone (fallback to index order)
            EnsureDefaultAssignments();
        }

        // Apply positions
        for (int i = 0; i < followers.Count; i++)
        {
            var follower = followers[i];
            if (follower == null) continue;

            if (!slotByFollower.TryGetValue(follower, out var slot) || slot == null)
                continue;

            int idx = i;
            if (assignedIndexByFollower.TryGetValue(follower, out int mapped))
                idx = Mathf.Clamp(mapped, 0, desiredPositions.Count - 1);

            Vector3 finalPos = desiredPositions[idx];

            if (snapThisFrame)
            {
                slot.position = finalPos;
            }
            else
            {
                float lerpT = 1f - Mathf.Exp(-slotLerpSpeed * Time.deltaTime);
                slot.position = Vector3.Lerp(slot.position, finalPos, lerpT);
            }
        }
    }

    private List<Vector3> BuildDesiredPositions(Vector3 playerPos, Vector3 fwd, Vector3 rightDir, float spacing, float baseDist)
    {
        switch (formation)
        {
            case FollowFormation.LineFront:
                return BuildLineFrontPositions(playerPos, fwd, rightDir, spacing, baseDist);

            case FollowFormation.WedgeFront:
                return BuildWedgeFrontPositions(playerPos, fwd, rightDir, spacing, baseDist);

            default:
                return BuildArcBehindPositions(playerPos, fwd, rightDir, spacing, baseDist);
        }
    }

    private List<Vector3> BuildLineFrontPositions(Vector3 playerPos, Vector3 fwd, Vector3 rightDir, float spacing, float baseDist)
    {
        int n = followers.Count;
        var result = new List<Vector3>(n);
        if (n == 0) return result;

        float frontDist = Mathf.Max(baseFrontDistance, baseDist, minFrontDistance);
        frontDist = Mathf.Max(frontDist, followStoppingDistance + playerPersonalSpace);

        Vector3 basePos = playerPos + fwd * frontDist;
        basePos.y = playerPos.y;

        float cx = (n - 1) * 0.5f;
        float s = Mathf.Max(0.1f, spacing) * Mathf.Max(0.1f, lineSpacingMultiplier);

        for (int i = 0; i < n; i++)
        {
            float x = (i - cx) * s;
            Vector3 desired = basePos + rightDir * x;
            desired.y = playerPos.y;

            result.Add(SampleNavmeshOrFallback(desired, playerPos));
        }

        return result;
    }

    private List<Vector3> BuildWedgeFrontPositions(Vector3 playerPos, Vector3 fwd, Vector3 rightDir, float spacing, float baseDist)
    {
        int n = followers.Count;
        var result = new List<Vector3>(n);
        if (n == 0) return result;

        float tipDist = Mathf.Max(baseFrontDistance, baseDist, minFrontDistance);
        tipDist = Mathf.Max(tipDist, followStoppingDistance + playerPersonalSpace);

        Vector3 tip = playerPos + fwd * tipDist;
        tip.y = playerPos.y;

        float s = Mathf.Max(0.1f, spacing);
        float rowBack = s * Mathf.Max(0.1f, wedgeRowBackMultiplier);

        int remaining = n;
        int index = 0;
        int row = 0;

        while (remaining > 0)
        {
            row++;
            int rowCapacity = row; // 1,2,3,...
            int take = Mathf.Min(rowCapacity, remaining);

            Vector3 rowCenter = tip - fwd * (row - 1) * rowBack;
            rowCenter.y = playerPos.y;

            float cx = (take - 1) * 0.5f;

            for (int j = 0; j < take; j++)
            {
                if (index >= n) break;

                float x = (j - cx) * s;
                Vector3 desired = rowCenter + rightDir * x;
                desired.y = playerPos.y;

                result.Add(SampleNavmeshOrFallback(desired, playerPos));

                index++;
                remaining--;
            }
        }

        return result;
    }

    private List<Vector3> BuildArcBehindPositions(Vector3 playerPos, Vector3 fwd, Vector3 rightDir, float spacing, float baseDist)
    {
        int n = followers.Count;
        var result = new List<Vector3>(n);
        if (n == 0) return result;

        Vector3 behindDir = -fwd;
        Vector3 behindRight = Vector3.Cross(Vector3.up, behindDir).normalized;
        if (behindRight.sqrMagnitude < 0.0001f) behindRight = rightDir;

        int remaining = n;
        int index = 0;
        int row = 0;

        float halfArc = Mathf.Clamp(arcDegrees * 0.5f, 10f, 90f);

        while (remaining > 0)
        {
            row++;
            int rowCapacity = row; // 1,2,3,...
            int take = Mathf.Min(rowCapacity, remaining);

            float radius = Mathf.Max(baseBehindDistance, baseDist) + (row - 1) * spacing * rowDistanceMultiplier;

            // Prevent slots from ever being too close to the player (avoids running into you)
            float minRadius = Mathf.Max(minBehindRadius, followStoppingDistance + playerPersonalSpace);
            if (radius < minRadius) radius = minRadius;

            for (int j = 0; j < take; j++)
            {
                if (index >= n) break;

                float t = (take == 1) ? 0f : (j / (float)(take - 1));
                float angle = Mathf.Lerp(-halfArc, halfArc, t);

                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * behindDir;

                // Small lateral nudge so the very first row isn't perfectly centered every time
                float lateralJitter = 0f;
                if (row == 1 && take == 1)
                    lateralJitter = 0.15f * spacing;

                Vector3 desired = playerPos + dir * radius + behindRight * lateralJitter;
                desired.y = playerPos.y;

                result.Add(SampleNavmeshOrFallback(desired, playerPos));

                index++;
                remaining--;
            }
        }

        return result;
    }

    private Vector3 SampleNavmeshOrFallback(Vector3 desired, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, Mathf.Max(0.1f, navmeshSampleRadius), NavMesh.AllAreas))
            return hit.position;

        return allowRawFallbackWhenNoNavmesh ? desired : fallback;
    }

    private void EnsureDefaultAssignments()
    {
        // Fill in missing assignments with a stable default (index order),
        // without disturbing existing assignments.
        var used = new HashSet<int>();
        foreach (var kvp in assignedIndexByFollower)
        {
            if (kvp.Key == null) continue;
            if (!followers.Contains(kvp.Key)) continue;
            used.Add(kvp.Value);
        }

        for (int i = 0; i < followers.Count; i++)
        {
            var f = followers[i];
            if (f == null) continue;

            if (assignedIndexByFollower.ContainsKey(f)) continue;

            int idx = i;
            while (used.Contains(idx) && idx < followers.Count) idx++;
            if (idx >= followers.Count) idx = i; // fallback
            assignedIndexByFollower[f] = idx;
            used.Add(idx);
        }
    }

    private void ReassignFollowersToSlots(List<Vector3> desiredPositions)
    {
        CleanupAssignmentForMissingFollowers();

        int n = followers.Count;
        if (n == 0) return;

        // Greedy minimal-distance assignment between follower positions and desired slot positions.
        // This avoids criss-crossing when headings/formations change.
        var remainingFollowers = new List<int>(n);
        var remainingSlots = new List<int>(n);

        for (int i = 0; i < n; i++)
        {
            remainingFollowers.Add(i);
            remainingSlots.Add(i);
        }

        assignedIndexByFollower.Clear();

        while (remainingFollowers.Count > 0)
        {
            float best = float.PositiveInfinity;
            int bestFi = -1;
            int bestSi = -1;
            int bestFListIdx = -1;
            int bestSListIdx = -1;

            for (int fiList = 0; fiList < remainingFollowers.Count; fiList++)
            {
                int fi = remainingFollowers[fiList];
                var follower = followers[fi];
                if (follower == null) continue;

                Vector3 fp = follower.position;
                fp.y = desiredPositions[0].y;

                for (int siList = 0; siList < remainingSlots.Count; siList++)
                {
                    int si = remainingSlots[siList];
                    Vector3 sp = desiredPositions[si];

                    float d = (fp - sp).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        bestFi = fi;
                        bestSi = si;
                        bestFListIdx = fiList;
                        bestSListIdx = siList;
                    }
                }
            }

            if (bestFi < 0 || bestSi < 0)
                break;

            var f = followers[bestFi];
            if (f != null)
                assignedIndexByFollower[f] = bestSi;

            // remove used follower and slot
            remainingFollowers.RemoveAt(bestFListIdx);
            remainingSlots.RemoveAt(bestSListIdx);
        }

        // Safety: ensure everyone has an assignment
        EnsureDefaultAssignments();
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