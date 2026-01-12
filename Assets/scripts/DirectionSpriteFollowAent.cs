using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(SpriteRenderer))]
public class DirectionSpriteFollowAgent : MonoBehaviour
{
    [Header("Refs (optional)")]
    public NavMeshAgent agent;              // if null, auto-find on parent
    public Transform unitRoot;              // if null, uses agent.transform

    [Header("Appearance")]
    public bool hideWhenNotMoving = true;
    public float showWhenRemainingDistanceAbove = 0.35f;
    public float forwardOffset = 1.2f;
    public float heightOffset = 0.05f;

    [Header("Rotation (Top-down arrow)")]
    public Vector3 fixedEuler = new Vector3(90f, 0f, 0f);
    public bool faceDestination = true;

    private SpriteRenderer sr;
    private QueuedDestination queued;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();

        if (agent == null) agent = GetComponentInParent<NavMeshAgent>();
        if (unitRoot == null && agent != null) unitRoot = agent.transform;

        // queued destination lives on the unit root (ally)
        if (agent != null)
        {
            queued = agent.GetComponent<QueuedDestination>();
            if (queued == null) queued = agent.gameObject.GetComponent<QueuedDestination>();
        }

        sr.enabled = false;
    }

    void LateUpdate()
    {
        if (sr == null || agent == null || unitRoot == null) return;

        bool inCommandMode = CommandCamToggle.Instance != null && CommandCamToggle.Instance.IsCommandMode;

        // 1) If we're in command mode, show arrow for QUEUED destination (planning)
        if (inCommandMode)
        {
            queued ??= agent.GetComponent<QueuedDestination>();

            bool hasQueued = (queued != null && queued.hasQueuedDestination);

            if (hideWhenNotMoving) sr.enabled = hasQueued;
            if (!sr.enabled) return;

            Vector3 dest = queued.queuedDestination;
            PlaceAndRotate(dest);
            return;
        }

        // 2) Otherwise (FPS/executing), show arrow for ACTIVE agent movement
        bool hasPath = agent.isActiveAndEnabled && agent.hasPath && !agent.pathPending;
        float remaining = hasPath ? agent.remainingDistance : 0f;
        bool isMoving = hasPath && remaining > showWhenRemainingDistanceAbove;

        if (hideWhenNotMoving) sr.enabled = isMoving;
        if (!sr.enabled) return;

        PlaceAndRotate(agent.destination);
    }

    private void PlaceAndRotate(Vector3 dest)
    {
        Vector3 from = unitRoot.position;
        Vector3 dir = dest - from;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f) return;

        dir.Normalize();

        Vector3 pos = from + dir * forwardOffset;
        pos.y = from.y + heightOffset;
        transform.position = pos;

        if (faceDestination)
        {
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(fixedEuler.x, yaw + fixedEuler.y, fixedEuler.z);
        }
        else
        {
            transform.rotation = Quaternion.Euler(fixedEuler);
        }
    }
}
