// CommandCameraZoomPan.cs
// Drop this onto your CommandCamera GameObject.
//
// Zoom (H): cycles through the zoomLevels array.
// Rule requested:
// - When zoom index == 0 (full/default zoom), force X back to 250.
// - When zoom index == 0 (full/default zoom), force Z back to 487.
// - When zoom index == 0, camera size is the Element 0 size (default 1350).
// - Zoomed-in levels still center on the player.
//
// Panning (only when zoomed in, unless allowPanningAtFullMap = true):
//   W = +Z (up on screen)
//   A = -X (left on screen)
//   S = -Z (down on screen)
//   D = +X (right on screen)
// Arrow keys follow the same screen-space mapping.

using UnityEngine;

[DisallowMultipleComponent]
public class CommandCameraZoomPan : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional. If not set, will use Camera on this GameObject.")]
    public Camera commandCamera;

    [Tooltip("Player transform to center on when zooming in.")]
    public Transform player;

    [Tooltip("Optional. If not set, uses Terrain.activeTerrain.")]
    public Terrain terrainBounds;

    [Header("Zoom")]
    [Tooltip("Key to cycle zoom levels.")]
    public KeyCode zoomKey = KeyCode.H;

    [Tooltip("Orthographic sizes to cycle through. Element 0 is the default/full zoom.")]
    public float[] zoomLevels = new float[] { 1350f, 1100f, 700f, 500f };

    [Tooltip("Start index into zoomLevels. 0 = full map (default).")]
    public int startZoomIndex = 0;

    [Header("Panning")]
    [Tooltip("World units per second at zoom=700. Scales with current zoom size.")]
    public float panSpeedAt700 = 600f;

    [Tooltip("Allow panning at zoomLevels[0] (full map). Usually false.")]
    public bool allowPanningAtFullMap = false;

    [Tooltip("Extra padding from terrain edges (world units).")]
    public float boundsPadding = 100f;

    [Tooltip("Use unscaled delta time (recommended if you pause time in command mode).")]
    public bool useUnscaledTime = true;

    [Tooltip("If true, also allow Arrow keys for panning using the same X/Z mapping.")]
    public bool allowArrowKeys = true;

    [Header("Enter Command Mode View")]
    [Tooltip("When entering command mode, snap the camera to a fixed overview instead of leaving it wherever it was.")]
    public bool applyEntryViewOnCommandModeEnter = true;

    [Tooltip("World position to use when command mode opens.")]
    public Vector3 commandModeEntryPosition = new Vector3(250f, 600f, 487f);

    [Tooltip("Euler rotation to use when command mode opens.")]
    public Vector3 commandModeEntryEuler = new Vector3(90f, 0f, 0f);

    [Tooltip("Orthographic size to use when command mode opens.")]
    public float commandModeEntryOrthoSize = 1350f;

    [Tooltip("If true, sets the current zoom index to whichever zoom level is closest to the entry ortho size.")]
    public bool syncZoomIndexToClosestLevelOnEntry = true;

    [Tooltip("If true, terrain clamping is applied immediately after snapping to the entry view.")]
    public bool clampAfterEntryView = false;

    [Header("Default Zoom Rule")]
    [Tooltip("When zoom index is 0, force X back to this value.")]
    public bool forceXAtZoomIndex0 = true;

    [Tooltip("When zoom index is 0, force Z back to this value.")]
    public bool forceZAtZoomIndex0 = true;

    [Tooltip("Forced X value when zoom index is 0.")]
    public float zoomIndex0X = 250f;

    [Tooltip("Forced Z value when zoom index is 0.")]
    public float zoomIndex0Z = 487f;

    private int _zoomIndex;

    void Reset()
    {
        commandCamera = GetComponent<Camera>();
        zoomLevels = new float[] { 1350f, 1100f, 700f, 500f };
        startZoomIndex = 0;

        applyEntryViewOnCommandModeEnter = true;
        commandModeEntryPosition = new Vector3(250f, 600f, 487f);
        commandModeEntryEuler = new Vector3(90f, 0f, 0f);
        commandModeEntryOrthoSize = 1350f;
        syncZoomIndexToClosestLevelOnEntry = true;
        clampAfterEntryView = false;

        forceXAtZoomIndex0 = true;
        forceZAtZoomIndex0 = true;
        zoomIndex0X = 250f;
        zoomIndex0Z = 487f;
    }

    void OnValidate()
    {
        if (panSpeedAt700 < 0f) panSpeedAt700 = 0f;
        if (boundsPadding < 0f) boundsPadding = 0f;
        if (commandModeEntryOrthoSize < 1f) commandModeEntryOrthoSize = 1f;

        EnsureZoomLevelsValid();
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
            Debug.LogWarning("[CommandCameraZoomPan] Command camera is not orthographic. This script expects an orthographic camera.");

        if (terrainBounds == null)
            terrainBounds = Terrain.activeTerrain;

        EnsureZoomLevelsValid();

        _zoomIndex = Mathf.Clamp(startZoomIndex, 0, zoomLevels.Length - 1);
        ApplyZoom(_zoomIndex, centerOnPlayer: false);

        if (_zoomIndex == 0)
            ApplyZoomIndexZeroRule();
        else
            ClampToTerrainBounds();
    }

    void Update()
    {
        if (zoomLevels == null || zoomLevels.Length == 0) return;

        if (Input.GetKeyDown(zoomKey))
            CycleZoom();

        HandlePanning_CustomAxes();

        if (_zoomIndex == 0)
            ApplyZoomIndexZeroRule();
    }

    private void CycleZoom()
    {
        _zoomIndex = (_zoomIndex + 1) % zoomLevels.Length;

        bool isDefaultZoom = (_zoomIndex == 0);
        ApplyZoom(_zoomIndex, centerOnPlayer: !isDefaultZoom);

        if (isDefaultZoom)
            ApplyZoomIndexZeroRule();
        else
            ClampToTerrainBounds();
    }

    private void ApplyZoom(int index, bool centerOnPlayer)
    {
        int safeIndex = Mathf.Clamp(index, 0, zoomLevels.Length - 1);
        commandCamera.orthographicSize = zoomLevels[safeIndex];

        if (centerOnPlayer && player != null)
        {
            Vector3 pos = transform.position;
            pos.x = player.position.x;
            pos.z = player.position.z;
            transform.position = pos;
        }
    }

    private void ApplyZoomIndexZeroRule()
    {
        if (commandCamera == null) return;

        if (zoomLevels != null && zoomLevels.Length > 0)
            commandCamera.orthographicSize = zoomLevels[0];
        else
            commandCamera.orthographicSize = commandModeEntryOrthoSize;

        Vector3 pos = transform.position;

        if (forceXAtZoomIndex0)
            pos.x = zoomIndex0X;

        if (forceZAtZoomIndex0)
            pos.z = zoomIndex0Z;

        transform.position = pos;
        transform.rotation = Quaternion.Euler(commandModeEntryEuler);
    }

    public void ApplyCommandModeEntryView()
    {
        if (commandCamera == null)
            commandCamera = GetComponent<Camera>();

        if (commandCamera == null) return;

        Vector3 entryPos = commandModeEntryPosition;
        if (forceXAtZoomIndex0)
            entryPos.x = zoomIndex0X;
        if (forceZAtZoomIndex0)
            entryPos.z = zoomIndex0Z;

        transform.position = entryPos;
        transform.rotation = Quaternion.Euler(commandModeEntryEuler);
        commandCamera.orthographicSize = Mathf.Max(1f, commandModeEntryOrthoSize);

        if (syncZoomIndexToClosestLevelOnEntry)
            _zoomIndex = FindClosestZoomIndex(commandCamera.orthographicSize);

        if (_zoomIndex == 0)
            ApplyZoomIndexZeroRule();
        else if (clampAfterEntryView)
            ClampToTerrainBounds();
    }

    private int FindClosestZoomIndex(float targetSize)
    {
        if (zoomLevels == null || zoomLevels.Length == 0)
            return 0;

        int bestIndex = 0;
        float bestDelta = Mathf.Abs(zoomLevels[0] - targetSize);

        for (int i = 1; i < zoomLevels.Length; i++)
        {
            float delta = Mathf.Abs(zoomLevels[i] - targetSize);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void HandlePanning_CustomAxes()
    {
        bool zoomedIn = _zoomIndex != 0;
        if (!zoomedIn && !allowPanningAtFullMap) return;

        float xAxis = 0f;
        float zAxis = 0f;

        // Top-down camera (X=90) screen-space mapping:
        // W = up = +Z
        // A = left = -X
        // S = down = -Z
        // D = right = +X
        if (Input.GetKey(KeyCode.W)) zAxis += 1f;
        if (Input.GetKey(KeyCode.S)) zAxis -= 1f;

        if (Input.GetKey(KeyCode.A)) xAxis -= 1f;
        if (Input.GetKey(KeyCode.D)) xAxis += 1f;

        if (allowArrowKeys)
        {
            if (Input.GetKey(KeyCode.UpArrow)) zAxis += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) zAxis -= 1f;
            if (Input.GetKey(KeyCode.LeftArrow)) xAxis -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) xAxis += 1f;
        }

        if (Mathf.Approximately(xAxis, 0f) && Mathf.Approximately(zAxis, 0f)) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float size = Mathf.Max(1f, commandCamera.orthographicSize);
        float speed = panSpeedAt700 * (size / 700f);

        Vector3 delta = new Vector3(xAxis, 0f, zAxis) * speed * dt;
        transform.position += delta;

        ClampToTerrainBounds();
    }

    private void ClampToTerrainBounds()
    {
        if (terrainBounds == null || commandCamera == null) return;

        Vector3 tPos = terrainBounds.transform.position;
        Vector3 tSize = terrainBounds.terrainData.size;

        float minX = tPos.x + boundsPadding;
        float maxX = tPos.x + tSize.x - boundsPadding;
        float minZ = tPos.z + boundsPadding;
        float maxZ = tPos.z + tSize.z - boundsPadding;

        float halfH = commandCamera.orthographicSize;
        float halfW = commandCamera.orthographicSize * commandCamera.aspect;

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

    private void EnsureZoomLevelsValid()
    {
        if (zoomLevels == null || zoomLevels.Length == 0)
        {
            zoomLevels = new float[] { 1350f, 1100f, 700f, 500f };
            return;
        }

        for (int i = 0; i < zoomLevels.Length; i++)
        {
            if (zoomLevels[i] < 1f)
                zoomLevels[i] = 1f;
        }
    }
}
