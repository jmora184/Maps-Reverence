using UnityEngine;

public class QueuedDestination : MonoBehaviour
{
    public bool hasQueuedDestination;
    public Vector3 queuedDestination;

    public void SetQueued(Vector3 dest)
    {
        hasQueuedDestination = true;
        queuedDestination = dest;
    }

    public void ClearQueued()
    {
        hasQueuedDestination = false;
    }
}
