using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Matches follower NavMeshAgent speed to the player's movement speed WITHOUT editing AllyController.
/// Attach to the Player (or any manager object).
///
/// This version can AUTO-FIND Slots Root at runtime so you don't have to drag anything in Inspector.
/// It detects the follow-slot container by looking at any AllyController.target that appears to be a "slot",
/// then walking up the hierarchy to find a shared parent that contains multiple slot children.
/// </summary>
public class PlayerFollowerSpeedMatcher : MonoBehaviour
{
    [Header("Follower detection")]
    [Tooltip("Parent Transform that contains the follow-slot Transforms created/used by your PlayerSquadFollowSystem. Leave empty to auto-find at runtime.")]
    public Transform slotsRoot;

    [Tooltip("If true and Slots Root is empty, the script will try to auto-detect it at runtime.")]
    public bool autoFindSlotsRoot = true;

    [Tooltip("How often (seconds) to retry auto-detecting Slots Root if it's still missing.")]
    public float autoFindRetryInterval = 1.0f;

    [Tooltip("Only scan for allies within this radius around the player (0 = scan all).")]
    public float scanRadius = 60f;

    [Tooltip("How often (seconds) to rescan the scene for allies. Speed updates still happen every frame.")]
    public float rescanInterval = 0.5f;

    [Header("Speed matching")]
    [Tooltip("Minimum agent speed while following (helps avoid stuck agents).")]
    public float minAgentSpeed = 2.0f;

    [Tooltip("Maximum agent speed while following.")]
    public float maxAgentSpeed = 6.0f;

    [Tooltip("Multiply the player's speed by this factor for the follower's base speed.")]
    public float playerSpeedMultiplier = 1.05f;

    [Tooltip("Extra speed added when the follower is far behind (catch-up).")]
    public float catchUpBonusMax = 2.0f;

    [Tooltip("Distance at which catch-up bonus starts ramping up.")]
    public float catchUpStartDistance = 4.0f;

    [Tooltip("Distance at which catch-up bonus reaches max.")]
    public float catchUpFullDistance = 12.0f;

    [Header("Optional agent tuning")]
    public bool matchAcceleration = true;
    public float accelerationMin = 12f;
    public float accelerationMax = 60f;

    public bool matchAngularSpeed = false;
    public float angularSpeed = 720f;

    [Header("Player speed measurement")]
    [Tooltip("If true, uses Rigidbody velocity (if present). Otherwise uses transform delta.")]
    public bool preferRigidbodyVelocity = true;

    [Tooltip("Smooth player speed to avoid jitter from tiny idle movements.")]
    public float speedSmoothing = 12f;

    // Cached allies in scene (we still verify each one is currently following).
    private AllyController[] _allies = new AllyController[0];

    private float _nextRescanTime;
    private float _nextAutoFindTime;
    private Vector3 _lastPos;
    private float _smoothedPlayerSpeed;

    void Awake()
    {
        _lastPos = transform.position;
        RescanAllies();
        TryAutoFindSlotsRoot(force: true);
    }

    void OnEnable()
    {
        _lastPos = transform.position;
        _nextRescanTime = Time.time + Random.Range(0f, Mathf.Max(0.05f, rescanInterval));
        _nextAutoFindTime = Time.time + Random.Range(0f, Mathf.Max(0.1f, autoFindRetryInterval));
    }

    void Update()
    {
        // Auto-find slots root if missing
        if ((slotsRoot == null) && autoFindSlotsRoot && Time.time >= _nextAutoFindTime)
        {
            _nextAutoFindTime = Time.time + Mathf.Max(0.1f, autoFindRetryInterval);
            TryAutoFindSlotsRoot(force: false);
        }

        // Periodic rescan (cheap + avoids needing to wire references).
        if (Time.time >= _nextRescanTime)
        {
            _nextRescanTime = Time.time + Mathf.Max(0.05f, rescanInterval);
            RescanAllies();
        }

        float rawPlayerSpeed = MeasurePlayerSpeed();
        _smoothedPlayerSpeed = Mathf.Lerp(_smoothedPlayerSpeed, rawPlayerSpeed, 1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));

        if (slotsRoot == null)
            return; // can't reliably detect "followers" yet

        // Apply to followers
        for (int i = 0; i < _allies.Length; i++)
        {
            AllyController ally = _allies[i];
            if (ally == null) continue;

            Transform t = ally.target; // assumes AllyController has public Transform target
            if (t == null) continue;

            if (!IsTargetUnderSlotsRoot(t))
                continue;

            NavMeshAgent agent = ally.GetComponent<NavMeshAgent>();
            if (agent == null) continue;

            float distToSlot = Vector3.Distance(ally.transform.position, t.position);
            float catchUpT = Mathf.InverseLerp(catchUpStartDistance, catchUpFullDistance, distToSlot);
            float catchUpBonus = Mathf.Lerp(0f, catchUpBonusMax, catchUpT);

            float desired = (_smoothedPlayerSpeed * playerSpeedMultiplier) + catchUpBonus;
            desired = Mathf.Clamp(desired, minAgentSpeed, maxAgentSpeed);

            if (Mathf.Abs(agent.speed - desired) > 0.05f)
                agent.speed = desired;

            if (matchAcceleration)
            {
                float acc = Mathf.Lerp(accelerationMin, accelerationMax, Mathf.InverseLerp(minAgentSpeed, maxAgentSpeed, desired));
                if (Mathf.Abs(agent.acceleration - acc) > 0.5f)
                    agent.acceleration = acc;
            }

            if (matchAngularSpeed)
            {
                if (Mathf.Abs(agent.angularSpeed - angularSpeed) > 1f)
                    agent.angularSpeed = angularSpeed;
            }
        }
    }

    private bool IsTargetUnderSlotsRoot(Transform target)
    {
        Transform cur = target;
        while (cur != null)
        {
            if (cur == slotsRoot) return true;
            cur = cur.parent;
        }
        return false;
    }

    private float MeasurePlayerSpeed()
    {
        float speed = 0f;

        if (preferRigidbodyVelocity)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                speed = rb.linearVelocity.magnitude;
                _lastPos = transform.position;
                return speed;
            }
        }

        Vector3 pos = transform.position;
        Vector3 delta = pos - _lastPos;
        _lastPos = pos;

        if (Time.deltaTime > 0.0001f)
            speed = delta.magnitude / Time.deltaTime;

        return speed;
    }

    private void RescanAllies()
    {
        AllyController[] found = FindObjectsOfType<AllyController>(true);

        if (scanRadius <= 0.01f)
        {
            _allies = found;
            return;
        }

        Vector3 p = transform.position;
        int count = 0;
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null) continue;
            float d = Vector3.Distance(found[i].transform.position, p);
            if (d <= scanRadius) count++;
        }

        AllyController[] filtered = new AllyController[count];
        int idx = 0;
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null) continue;
            float d = Vector3.Distance(found[i].transform.position, p);
            if (d <= scanRadius) filtered[idx++] = found[i];
        }

        _allies = filtered;
    }

    private void TryAutoFindSlotsRoot(bool force)
    {
        if (!autoFindSlotsRoot) return;
        if (slotsRoot != null && !force) return;

        // Try: look at any ally target that looks like a follow "slot".
        for (int i = 0; i < _allies.Length; i++)
        {
            AllyController ally = _allies[i];
            if (ally == null) continue;

            Transform t = ally.target;
            if (t == null) continue;

            // If target name doesn't look like a slot, still try but lower confidence.
            Transform candidate = FindLikelySlotsRootFromTarget(t);
            if (candidate != null)
            {
                slotsRoot = candidate;
                return;
            }
        }
    }

    private Transform FindLikelySlotsRootFromTarget(Transform target)
    {
        // Walk up parents and find a parent that appears to be a "slot container":
        // - Has 2+ children
        // - Several children have "slot" in name OR parent name has "slot"/"follow"/"formation"
        Transform cur = target;
        Transform best = null;
        int bestScore = 0;

        int safety = 0;
        while (cur != null && safety++ < 12)
        {
            int score = ScoreAsSlotsRoot(cur);

            // Prefer containers that are under THIS player to avoid accidentally choosing another system's root.
            if (IsUnderThisPlayer(cur))
                score += 2;

            if (score > bestScore)
            {
                bestScore = score;
                best = cur;
            }

            cur = cur.parent;
        }

        // Require at least a modest confidence score.
        return bestScore >= 4 ? best : null;
    }

    private int ScoreAsSlotsRoot(Transform tr)
    {
        int score = 0;

        string n = tr.name.ToLowerInvariant();
        if (n.Contains("slot")) score += 3;
        if (n.Contains("follow")) score += 2;
        if (n.Contains("formation")) score += 2;
        if (n.Contains("arc")) score += 1;

        int childCount = tr.childCount;
        if (childCount >= 2) score += 1;
        if (childCount >= 4) score += 1;

        // Count children that look like slots
        int slotChildren = 0;
        for (int i = 0; i < childCount; i++)
        {
            string cn = tr.GetChild(i).name.ToLowerInvariant();
            if (cn.Contains("slot") || cn.StartsWith("s_") || cn.StartsWith("slot_"))
                slotChildren++;
        }

        if (slotChildren >= 2) score += 2;
        if (slotChildren >= 4) score += 2;

        return score;
    }

    private bool IsUnderThisPlayer(Transform tr)
    {
        Transform cur = tr;
        int safety = 0;
        while (cur != null && safety++ < 20)
        {
            if (cur == this.transform) return true;
            cur = cur.parent;
        }
        return false;
    }
}
