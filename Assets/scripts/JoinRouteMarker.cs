using UnityEngine;

public class JoinRouteMarker : MonoBehaviour
{
    public Transform target;
    public bool inRoute;

    public void Begin(Transform joinTarget)
    {
        target = joinTarget;
        inRoute = true;
    }

    public void End()
    {
        inRoute = false;
        target = null;
    }
}
