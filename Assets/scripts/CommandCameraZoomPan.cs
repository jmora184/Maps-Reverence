// CommandCameraZoomPan.cs
// Drop this onto your CommandCamera GameObject.
//
// Zoom (H): cycles orthographic size 1300 -> 700 -> 500 -> 1300
// Zoom centers on the player each press.
//
// Panning (only when zoomed in, unless allowPanningAtFullMap = true):
//   W/S moves +X / -X
//   A/D moves +Z / -Z   (SWAPPED per request: A = +Z, D = -Z)
// Optional: Arrow keys use the same mapping as A/D for Z and Up/Down for X.
//
// Terrain clamp keeps the camera view inside the terrain bounds.
//
// IMPORTANT UNITY NOTE:
// Public fields are serialized. If you already added this component earlier,
// Unity keeps the old zoomLevels (e.g., 1300/1000/700) in the Inspector even after code changes.
// This script auto-migrates that common legacy set to 1300/700/500 in OnValidate/Awake.
// You can also use the component gear menu (⋮) -> Reset to restore defaults.

using UnityEngine;

[DisallowMultipleComponent]
public class CommandCameraZoomPan : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional. If not set, will use Camera on this GameObject.")]
    public Camera commandCamera;

    [Tooltip("Player transform to center on when zooming.")]
    public Transform player;

    [Tooltip("Optional. If not set, uses Terrain.activeTerrain.")]
    public Terrain terrainBounds;

    [Header("Zoom")]
    [Tooltip("Key to cycle zoom levels.")]
    public KeyCode zoomKey = KeyCode.H;

    [Tooltip("Orthographic sizes to cycle through. Default: 1300, 700, 500")]
    public float[] zoomLevels = new float[] { 1300f, 700f, 500f };

    [Tooltip("Start index into zoomLevels. 0 = full map (default).")]
    public int startZoomIndex = 0;

    [Header("Panning")]
    [Tooltip("World units per second at zoom=700. Scales with current zoom size.")]
    public float panSpeedAt700 = 600f;

    [Tooltip("Allow panning at zoomLevels[0] (full map). Usually false.")]
    public bool allowPanningAtFullMap = false;

    [Tooltip("Extra padding from terrain edges (world units).")]
    public float boundsPadding = 0f;

    [Tooltip("Use unscaled delta time (recommended if you pause time in command mode).")]
    public bool useUnscaledTime = true;

    [Tooltip("If true, also allow Arrow keys for panning using the same X/Z mapping.")]
    public bool allowArrowKeys = true;

    private int _zoomIndex;

    void Reset()
    {
        commandCamera = GetComponent<Camera>();
        zoomLevels = new float[] { 1300f, 700f, 500f };
        startZoomIndex = 0;
    }

    void OnValidate()
    {
        // Auto-migrate the most common legacy set so you don't get stuck at 1000/700.
        if (zoomLevels != null && zoomLevels.Length == 3)
        {
            if (Mathf.Approximately(zoomLevels[0], 1300f) &&
                Mathf.Approximately(zoomLevels[1], 1000f) &&
                Mathf.Approximately(zoomLevels[2], 700f))
            {
                zoomLevels[1] = 700f;
                zoomLevels[2] = 500f;
            }
        }

        if (panSpeedAt700 < 0f) panSpeedAt700 = 0f;
        if (boundsPadding < 0f) boundsPadding = 0f;
    }

    void Awake()
    {
        if (commandCamera == null)
            commandCamera = GetComponent<Camera>();

        if (commandCamera == null)
        {
            Debug.LogError("[CommandCameraZoomPan] No Camera found on this GameObject.");
            enabled = false;
            return;
        }

        if (!commandCamera.orthographic)
        {
            Debug.LogWarning("[CommandCameraZoomPan] Command camera is not orthographic. This script expects an orthographic camera.");
        }

        if (terrainBounds == null)
            terrainBounds = Terrain.activeTerrain;

        // Ensure zoomLevels are sane & migrate legacy values one more time at runtime.
        MigrateLegacyZoomLevelsIfNeeded();
        EnsureZoomLevelsValid();

        _zoomIndex = Mathf.Clamp(startZoomIndex, 0, zoomLevels.Length - 1);

        ApplyZoom(_zoomIndex, centerOnPlayer: false);
        ClampToTerrainBounds();
    }

    void Update()
    {
        if (zoomLevels == null || zoomLevels.Length == 0) return;

        if (Input.GetKeyDown(zoomKey))
        {
            CycleZoom();
        }

        HandlePanning_CustomAxes();
    }

    private void CycleZoom()
    {
        _zoomIndex = (_zoomIndex + 1) % zoomLevels.Length;
        ApplyZoom(_zoomIndex, centerOnPlayer: true);
        ClampToTerrainBounds();
    }

    private void ApplyZoom(int index, bool centerOnPlayer)
    {
        commandCamera.orthographicSize = zoomLevels[Mathf.Clamp(index, 0, zoomLevels.Length - 1)];

        if (centerOnPlayer && player != null)
        {
            Vector3 pos = transform.position;
            pos.x = player.position.x;
            pos.z = player.position.z;
            transform.position = pos;
        }
    }

    // Custom axes requested:
    // - W/S moves X
    // - A/D moves Z (SWAPPED: A = +Z, D = -Z)
    private void HandlePanning_CustomAxes()
    {
        bool zoomedIn = _zoomIndex != 0;
        if (!zoomedIn && !allowPanningAtFullMap) return;

        float xAxis = 0f; // W/S -> X
        float zAxis = 0f; // A/D -> Z

        // WASD
        if (Input.GetKey(KeyCode.W)) xAxis += 1f;
        if (Input.GetKey(KeyCode.S)) xAxis -= 1f;

        // Swapped directions here:
        if (Input.GetKey(KeyCode.A)) zAxis += 1f; // A = +Z
        if (Input.GetKey(KeyCode.D)) zAxis -= 1f; // D = -Z

        // Optional arrows (same mapping concept):
        // Up/Down = X, Left/Right = Z
        if (allowArrowKeys)
        {
            if (Input.GetKey(KeyCode.UpArrow)) xAxis += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) xAxis -= 1f;

            // Keep arrows "intuitive": RightArrow = +Z, LeftArrow = -Z
            // (If you want these swapped too, tell me.)
            if (Input.GetKey(KeyCode.RightArrow)) zAxis += 1f;
            if (Input.GetKey(KeyCode.LeftArrow)) zAxis -= 1f;
        }

        if (Mathf.Approximately(xAxis, 0f) && Mathf.Approximately(zAxis, 0f)) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        // Speed scales with zoom so it feels consistent.
        // At 700 -> panSpeedAt700. At 500 -> slower. At 1300 -> faster.
        float size = Mathf.Max(1f, commandCamera.orthographicSize);
        float speed = panSpeedAt700 * (size / 700f);

        Vector3 delta = new Vector3(xAxis, 0f, zAxis) * speed * dt;
        transform.position += delta;

        ClampToTerrainBounds();
    }

    private void ClampToTerrainBounds()
    {
        if (terrainBounds == null) return;

        Vector3 tPos = terrainBounds.transform.position;
        Vector3 tSize = terrainBounds.terrainData.size;

        float minX = tPos.x + boundsPadding;
        float maxX = tPos.x + tSize.x - boundsPadding;
        float minZ = tPos.z + boundsPadding;
        float maxZ = tPos.z + tSize.z - boundsPadding;

        // Visible extents in world units for orthographic camera
        float halfH = commandCamera.orthographicSize;
        float halfW = commandCamera.orthographicSize * commandCamera.aspect;

        // If the view is bigger than the terrain, pin to center
        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;

        float clampedX;
        float clampedZ;

        if ((maxX - minX) <= (halfW * 2f))
            clampedX = centerX;
        else
            clampedX = Mathf.Clamp(transform.position.x, minX + halfW, maxX - halfW);

        if ((maxZ - minZ) <= (halfH * 2f))
            clampedZ = centerZ;
        else
            clampedZ = Mathf.Clamp(transform.position.z, minZ + halfH, maxZ - halfH);

        Vector3 p = transform.position;
        p.x = clampedX;
        p.z = clampedZ;
        transform.position = p;
    }

    public void RecenterOnPlayer()
    {
        if (player == null) return;
        Vector3 pos = transform.position;
        pos.x = player.position.x;
        pos.z = player.position.z;
        transform.position = pos;
        ClampToTerrainBounds();
    }

    public int CurrentZoomIndex => _zoomIndex;
    public float CurrentZoomSize => commandCamera != null ? commandCamera.orthographicSize : 0f;

    private void MigrateLegacyZoomLevelsIfNeeded()
    {
        if (zoomLevels == null) return;
        if (zoomLevels.Length == 3 &&
            Mathf.Approximately(zoomLevels[0], 1300f) &&
            Mathf.Approximately(zoomLevels[1], 1000f) &&
            Mathf.Approximately(zoomLevels[2], 700f))
        {
            zoomLevels[1] = 700f;
            zoomLevels[2] = 500f;
        }
    }

    private void EnsureZoomLevelsValid()
    {
        if (zoomLevels == null || zoomLevels.Length == 0)
        {
            zoomLevels = new float[] { 1300f, 700f, 500f };
            return;
        }

        // Clamp to positive sizes
        for (int i = 0; i < zoomLevels.Length; i++)
        {
            if (zoomLevels[i] < 1f) zoomLevels[i] = 1f;
        }
    }
}
