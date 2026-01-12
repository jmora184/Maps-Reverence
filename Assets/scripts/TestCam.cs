using UnityEngine;

public class TestCam : MonoBehaviour
{
    public static TestCam instance;

    [Header("FPS Camera")]
    public Camera cam;
    public Transform camTrans;

    [Header("Zoom")]
    private float startFOV, targetFOV;
    public float zoomSpeed = 1f;

    [Header("Follow Target (optional)")]
    public Transform target;

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        if (cam == null) cam = GetComponentInChildren<Camera>();

        startFOV = cam != null ? cam.fieldOfView : 60f;
        targetFOV = startFOV;

        // FPS-style cursor default
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (target != null)
        {
            transform.position = target.position;
            transform.rotation = target.rotation;
        }

        if (cam != null)
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, zoomSpeed * Time.unscaledDeltaTime);
    }

    public void ZoomIn(float newZoom) => targetFOV = newZoom;
    public void ZoomOut() => targetFOV = startFOV;

    void Update()
    {
        // IMPORTANT:
        // Let CommandCamToggle handle command mode keys (K/L) and camera switching.
        // This TestCam script should NOT toggle command mode.

        // Your FPS click test (only when FPS cam is enabled)
        if (cam != null && cam.enabled)
        {
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    Debug.Log("CLICKED test cam " + hit.collider.name);
                }
            }
        }
    }
}
