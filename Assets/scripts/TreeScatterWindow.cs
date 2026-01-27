#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class TreeScatterWindow : EditorWindow
{
    [Header("What to place")]
    public GameObject prefab;
    public int count = 200;

    [Header("Where to place (world space box)")]
    public Vector3 center = Vector3.zero;
    public Vector2 sizeXZ = new Vector2(200f, 200f);
    public float raycastHeight = 200f;

    [Header("Ground")]
    public LayerMask groundMask = ~0; // set to ground2 in the UI
    public bool alignToNormal = false;
    [Range(0f, 80f)] public float maxSlope = 35f;

    [Header("Randomness")]
    public bool randomYaw = true;
    public Vector2 uniformScaleRange = new Vector2(0.9f, 1.2f);

    [Header("Spacing (optional)")]
    public bool useMinSpacing = true;
    public float minSpacing = 2.0f;
    public int maxAttemptsPerTree = 25;

    [Header("Appearance (optional)")]
    public bool assignMaterial = false;
    public Material[] materialOptions;
    public bool randomMaterial = true;

    public bool applyColorTint = false;
    public bool randomTint = true;
    public Color tintA = new Color(0.85f, 0.95f, 0.85f, 1f);
    public Color tintB = new Color(0.55f, 0.80f, 0.55f, 1f);

    [Tooltip("Shader property name for color. Common: '_BaseColor' (URP/HDRP) or '_Color' (Built-in Standard).")]
    public string colorProperty = "_BaseColor";

    [Header("Parent")]
    public Transform parentOverride;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [MenuItem("Tools/Scatter/Tree Scatter")]
    public static void Open()
    {
        GetWindow<TreeScatterWindow>("Tree Scatter");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Tree Scatter Tool (Raycast onto ground)", EditorStyles.boldLabel);

        prefab = (GameObject)EditorGUILayout.ObjectField("Tree Prefab", prefab, typeof(GameObject), false);
        count = Mathf.Max(0, EditorGUILayout.IntField("Count", count));

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Placement Area (World)", EditorStyles.boldLabel);
        center = EditorGUILayout.Vector3Field("Center", center);
        sizeXZ = EditorGUILayout.Vector2Field("Size XZ", sizeXZ);
        raycastHeight = Mathf.Max(1f, EditorGUILayout.FloatField("Raycast Height", raycastHeight));

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Ground", EditorStyles.boldLabel);
        groundMask = LayerMaskField("Ground Mask", groundMask);
        alignToNormal = EditorGUILayout.Toggle("Align To Normal", alignToNormal);
        maxSlope = EditorGUILayout.Slider("Max Slope", maxSlope, 0f, 80f);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Randomness", EditorStyles.boldLabel);
        randomYaw = EditorGUILayout.Toggle("Random Y Rotation", randomYaw);
        uniformScaleRange = EditorGUILayout.Vector2Field("Scale Range", uniformScaleRange);
        if (uniformScaleRange.x > uniformScaleRange.y)
            uniformScaleRange = new Vector2(uniformScaleRange.y, uniformScaleRange.x);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Spacing", EditorStyles.boldLabel);
        useMinSpacing = EditorGUILayout.Toggle("Use Min Spacing", useMinSpacing);
        using (new EditorGUI.DisabledScope(!useMinSpacing))
        {
            minSpacing = Mathf.Max(0f, EditorGUILayout.FloatField("Min Spacing", minSpacing));
            maxAttemptsPerTree = Mathf.Clamp(EditorGUILayout.IntField("Attempts / Tree", maxAttemptsPerTree), 1, 500);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);

        assignMaterial = EditorGUILayout.Toggle("Assign Material", assignMaterial);
        using (new EditorGUI.DisabledScope(!assignMaterial))
        {
            SerializedObject so = new SerializedObject(this);
            SerializedProperty matsProp = so.FindProperty("materialOptions");
            EditorGUILayout.PropertyField(matsProp, new GUIContent("Material Options"), true);
            randomMaterial = EditorGUILayout.Toggle("Random Material", randomMaterial);
            so.ApplyModifiedProperties();

            if (!randomMaterial)
                EditorGUILayout.HelpBox("If Random Material is off, the first Material Option will be used (if any).", MessageType.None);
        }

        applyColorTint = EditorGUILayout.Toggle("Apply Color Tint", applyColorTint);
        using (new EditorGUI.DisabledScope(!applyColorTint))
        {
            randomTint = EditorGUILayout.Toggle("Random Tint", randomTint);
            tintA = EditorGUILayout.ColorField("Tint A", tintA);
            tintB = EditorGUILayout.ColorField("Tint B", tintB);
            colorProperty = EditorGUILayout.TextField("Color Property", colorProperty);

            EditorGUILayout.HelpBox("Most URP/HDRP shaders use _BaseColor. Built-in Standard uses _Color.", MessageType.None);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Parent", EditorStyles.boldLabel);
        parentOverride = (Transform)EditorGUILayout.ObjectField("Parent (optional)", parentOverride, typeof(Transform), true);

        EditorGUILayout.Space(12);
        using (new EditorGUI.DisabledScope(prefab == null || count <= 0))
        {
            if (GUILayout.Button("Scatter Trees", GUILayout.Height(32)))
            {
                Scatter();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Tip: Set Ground Mask to your ground layer (e.g., ground2).\n" +
            "After scattering, bake NavMesh from your NavMeshBaker.\n\n" +
            "If trees should block movement, give them colliders and bake with 'Physics Colliders' or add NavMeshObstacles.",
            MessageType.Info
        );
    }

    private void Scatter()
    {
        if (prefab == null || count <= 0) return;

        // Create parent
        Transform parent = parentOverride;
        if (parent == null)
        {
            var existing = GameObject.Find("Trees_Scattered");
            if (existing == null) existing = new GameObject("Trees_Scattered");
            parent = existing.transform;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        // Keep a list of placed points for min spacing.
        List<Vector3> placed = new List<Vector3>(count);

        int placedCount = 0;
        int attemptsTotal = 0;

        // Area bounds in XZ
        float halfX = Mathf.Abs(sizeXZ.x) * 0.5f;
        float halfZ = Mathf.Abs(sizeXZ.y) * 0.5f;

        while (placedCount < count && attemptsTotal < count * maxAttemptsPerTree * 2)
        {
            attemptsTotal++;

            float x = Random.Range(center.x - halfX, center.x + halfX);
            float z = Random.Range(center.z - halfZ, center.z + halfZ);

            Vector3 origin = new Vector3(x, center.y + raycastHeight, z);

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, raycastHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
                continue;

            // Slope filter
            float slope = Vector3.Angle(hit.normal, Vector3.up);
            if (slope > maxSlope)
                continue;

            Vector3 pos = hit.point;

            // Spacing check
            if (useMinSpacing && minSpacing > 0f)
            {
                bool tooClose = false;
                float minSqr = minSpacing * minSpacing;

                for (int i = 0; i < placed.Count; i++)
                {
                    if ((placed[i] - pos).sqrMagnitude < minSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;
            }

            Quaternion rot = Quaternion.identity;

            if (alignToNormal)
            {
                // Align 'up' to normal, then apply random yaw around that normal
                rot = Quaternion.FromToRotation(Vector3.up, hit.normal);
                if (randomYaw)
                    rot = Quaternion.AngleAxis(Random.Range(0f, 360f), hit.normal) * rot;
            }
            else
            {
                float yaw = randomYaw ? Random.Range(0f, 360f) : 0f;
                rot = Quaternion.Euler(0f, yaw, 0f);
            }

            float s = Random.Range(uniformScaleRange.x, uniformScaleRange.y);

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(go, "Scatter Tree");
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = new Vector3(s, s, s);

            ApplyAppearance(go);

            placed.Add(pos);
            placedCount++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"TreeScatter: Placed {placedCount}/{count} trees under '{parent.name}'. Attempts: {attemptsTotal}");
        if (placedCount < count)
            Debug.LogWarning("TreeScatter: Could not place all trees. Increase area size, reduce min spacing, increase max slope, or raise attempts.");
    }

    private void ApplyAppearance(GameObject instance)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return;

        // 1) Material override (sharedMaterial keeps it editor-friendly and consistent)
        if (assignMaterial && materialOptions != null && materialOptions.Length > 0)
        {
            Material chosen = materialOptions[0];
            if (randomMaterial && materialOptions.Length > 1)
                chosen = materialOptions[Random.Range(0, materialOptions.Length)];

            foreach (var r in renderers)
                r.sharedMaterial = chosen;
        }

        // 2) Per-instance tint using MaterialPropertyBlock (does NOT create new materials)
        if (applyColorTint)
        {
            Color tint = randomTint ? Color.Lerp(tintA, tintB, Random.value) : tintA;

            var mpb = new MaterialPropertyBlock();

            int propId = Shader.PropertyToID(colorProperty);
            // If user typed a property that doesn't exist, try common ones.
            foreach (var r in renderers)
            {
                r.GetPropertyBlock(mpb);

                if (r.sharedMaterial != null)
                {
                    if (r.sharedMaterial.HasProperty(propId))
                    {
                        mpb.SetColor(propId, tint);
                    }
                    else if (r.sharedMaterial.HasProperty(BaseColorId))
                    {
                        mpb.SetColor(BaseColorId, tint);
                    }
                    else if (r.sharedMaterial.HasProperty(ColorId))
                    {
                        mpb.SetColor(ColorId, tint);
                    }
                }

                r.SetPropertyBlock(mpb);
            }
        }
    }

    // Simple LayerMask field UI
    private static LayerMask LayerMaskField(string label, LayerMask selected)
    {
        var layers = new List<string>();
        var layerNumbers = new List<int>();

        for (int i = 0; i < 32; i++)
        {
            string layerName = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(layerName))
            {
                layers.Add(layerName);
                layerNumbers.Add(i);
            }
        }

        int maskWithoutEmpty = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            int layer = layerNumbers[i];
            if ((selected.value & (1 << layer)) != 0)
                maskWithoutEmpty |= (1 << i);
        }

        maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers.ToArray());

        int mask = 0;
        for (int i = 0; i < layerNumbers.Count; i++)
        {
            if ((maskWithoutEmpty & (1 << i)) != 0)
                mask |= (1 << layerNumbers[i]);
        }

        selected.value = mask;
        return selected;
    }
}
#endif
