// TerrainLayerOptimizer.cs
// Drop into: Assets/Editor/TerrainLayerOptimizer.cs
// Unity Editor tool to reduce terrain paint layer overlap and optionally merge "variant" layers into core layers.
// Works by editing alphamaps (splatmaps): transfers weights from source layers to target layers,
// then clamps each alphamap pixel to MaxLayersPerPixel (default 4) and renormalizes.
// Compatible with URP Terrain/Lit and helps reduce extra "Add Pass" blending cost.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class TerrainLayerOptimizer : EditorWindow
{
    [Serializable]
    private class MappingRule
    {
        public string sourceLayerName;
        public string targetLayerName;
        public bool enabled = true;
    }

    private Terrain _terrain;
    private int _maxLayersPerPixel = 4;
    private float _minWeightEpsilon = 0.001f;

    // Rules are name-based so it survives layer reorder.
    [SerializeField] private List<MappingRule> _rules = new List<MappingRule>();

    private Vector2 _scroll;

    [MenuItem("Tools/Terrain/Layer Optimizer")]
    public static void ShowWindow()
    {
        var win = GetWindow<TerrainLayerOptimizer>("Terrain Layer Optimizer");
        win.minSize = new Vector2(520, 480);
        win.Show();
    }

    private void OnEnable()
    {
        // Try to auto-pick selected terrain
        if (_terrain == null)
        {
            var go = Selection.activeGameObject;
            if (go != null) _terrain = go.GetComponent<Terrain>();
            if (_terrain == null && Terrain.activeTerrain != null) _terrain = Terrain.activeTerrain;
        }

        if (_rules == null) _rules = new List<MappingRule>();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Terrain Layer Optimizer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This tool edits Terrain alphamaps (paint data). It can:\n" +
            "1) Merge 'variant' layers into core layers via mapping rules.\n" +
            "2) Clamp each painted pixel to Max Layers Per Pixel (default 4).\n\n" +
            "Tip: Start with a small test terrain or duplicate your TerrainData asset for backup.",
            MessageType.Info);

        EditorGUILayout.Space(6);

        _terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", _terrain, typeof(Terrain), true);

        using (new EditorGUI.DisabledScope(_terrain == null))
        {
            _maxLayersPerPixel = EditorGUILayout.IntSlider("Max Layers Per Pixel", _maxLayersPerPixel, 1, 8);
            _minWeightEpsilon = EditorGUILayout.Slider("Min Weight Epsilon", _minWeightEpsilon, 0.0f, 0.05f);

            EditorGUILayout.Space(8);

            DrawLayerList();

            EditorGUILayout.Space(10);
            DrawRulesUI();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Analyze (How Many Pixels > Max?)", GUILayout.Height(30)))
            {
                Analyze();
            }
            if (GUILayout.Button("Optimize (Apply Rules + Clamp)", GUILayout.Height(30)))
            {
                Optimize();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Create Backup Copy of TerrainData (Duplicate Asset)", GUILayout.Height(26)))
            {
                DuplicateTerrainDataAsset();
            }
        }
    }

    private void DrawLayerList()
    {
        if (_terrain == null || _terrain.terrainData == null) return;

        var td = _terrain.terrainData;
        var layers = td.terrainLayers;

        EditorGUILayout.LabelField("Current Terrain Layers", EditorStyles.boldLabel);

        if (layers == null || layers.Length == 0)
        {
            EditorGUILayout.HelpBox("No Terrain Layers found on this TerrainData.", MessageType.Warning);
            return;
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var tl = layers[i];
                string name = tl != null ? tl.name : "(Missing Layer)";
                EditorGUILayout.LabelField($"{i}: {name}");
            }
        }
    }

    private void DrawRulesUI()
    {
        EditorGUILayout.LabelField("Merge Rules (Source → Target)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Enabled rules move paint weight from Source to Target before clamping.\n" +
            "Example: Grass_B → Grass_A, Heather_A → Grass_A, Tidal_Pools_B → Pebbles_B.",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Preset: Your 7 Layers (Safe Default)"))
        {
            LoadPresetForUserLayers();
        }
        if (GUILayout.Button("Add Rule"))
        {
            _rules.Add(new MappingRule { sourceLayerName = "", targetLayerName = "", enabled = true });
        }
        if (GUILayout.Button("Clear Rules"))
        {
            if (EditorUtility.DisplayDialog("Clear Rules", "Remove all rules?", "Yes", "No"))
                _rules.Clear();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_rules.Count == 0)
            {
                EditorGUILayout.LabelField("No rules. (Clamping still works without rules.)");
            }

            for (int i = 0; i < _rules.Count; i++)
            {
                var r = _rules[i];
                EditorGUILayout.BeginHorizontal();

                r.enabled = EditorGUILayout.Toggle(r.enabled, GUILayout.Width(18));
                r.sourceLayerName = EditorGUILayout.TextField(r.sourceLayerName, GUILayout.MinWidth(120));
                EditorGUILayout.LabelField("→", GUILayout.Width(18));
                r.targetLayerName = EditorGUILayout.TextField(r.targetLayerName, GUILayout.MinWidth(120));

                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    _rules.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void LoadPresetForUserLayers()
    {
        // Based on the user's shown layer names:
        // Grass_A, Grass_B, Grass_Soil_A, Pebbles_B, Tidal_Pools_B, Cliff_Mossy_E, Heather_A
        _rules = new List<MappingRule>
        {
            new MappingRule { enabled = true, sourceLayerName = "Grass_B",       targetLayerName = "Grass_A" },
            new MappingRule { enabled = true, sourceLayerName = "Heather_A",     targetLayerName = "Grass_A" },
            new MappingRule { enabled = true, sourceLayerName = "Tidal_Pools_B", targetLayerName = "Pebbles_B" },
        };
    }

    private void Analyze()
    {
        if (!ValidateTerrain(out var td)) return;

        var (maps, w, h, layers) = GetAlphamaps(td);
        int overMax = 0;
        int total = w * h;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int count = 0;
                for (int l = 0; l < layers; l++)
                {
                    if (maps[y, x, l] > _minWeightEpsilon) count++;
                }
                if (count > _maxLayersPerPixel) overMax++;
            }
        }

        float pct = (total > 0) ? (overMax * 100f / total) : 0f;
        EditorUtility.DisplayDialog(
            "Terrain Layer Analysis",
            $"Alphamap size: {w}×{h}\nLayers: {layers}\n\n" +
            $"Pixels with > {_maxLayersPerPixel} active layers (>{_minWeightEpsilon:0.###}): {overMax:n0} ({pct:0.00}%)\n\n" +
            "If this % is high and your terrain appears in multiple passes (Add Pass), optimizing usually helps.",
            "OK");
    }

    private void Optimize()
    {
        if (!ValidateTerrain(out var td)) return;

        if (!EditorUtility.DisplayDialog(
            "Optimize Terrain Alphamaps",
            "This will modify the TerrainData paint (alphamaps).\n\n" +
            "Recommended: click the backup button first (duplicates TerrainData asset).\n\nProceed?",
            "Proceed", "Cancel"))
            return;

        Undo.RegisterCompleteObjectUndo(td, "Optimize Terrain Alphamaps");

        var (maps, w, h, layerCount) = GetAlphamaps(td);

        // Build name->index map
        var layerNameToIndex = BuildLayerNameToIndex(td);

        // Build executable mapping: sourceIndex -> targetIndex
        var indexRules = new List<(int src, int dst)>();
        foreach (var r in _rules)
        {
            if (r == null || !r.enabled) continue;
            if (string.IsNullOrWhiteSpace(r.sourceLayerName) || string.IsNullOrWhiteSpace(r.targetLayerName)) continue;

            if (!layerNameToIndex.TryGetValue(r.sourceLayerName.Trim(), out int src))
            {
                Debug.LogWarning($"[TerrainLayerOptimizer] Source layer not found: '{r.sourceLayerName}'");
                continue;
            }
            if (!layerNameToIndex.TryGetValue(r.targetLayerName.Trim(), out int dst))
            {
                Debug.LogWarning($"[TerrainLayerOptimizer] Target layer not found: '{r.targetLayerName}'");
                continue;
            }
            if (src == dst) continue;

            indexRules.Add((src, dst));
        }

        int pixelsChanged = 0;
        int pixelsOverMaxBefore = 0;
        int pixelsOverMaxAfter = 0;

        // Temp arrays to avoid allocations
        float[] weights = new float[layerCount];
        int[] idx = new int[layerCount];

        try
        {
            for (int y = 0; y < h; y++)
            {
                if (y % 32 == 0)
                {
                    float p = (float)y / h;
                    EditorUtility.DisplayProgressBar("Optimizing Terrain Alphamaps", $"Row {y}/{h}", p);
                }

                for (int x = 0; x < w; x++)
                {
                    // Copy
                    for (int l = 0; l < layerCount; l++)
                        weights[l] = maps[y, x, l];

                    // Count >max before
                    int activeBefore = 0;
                    for (int l = 0; l < layerCount; l++)
                        if (weights[l] > _minWeightEpsilon) activeBefore++;
                    if (activeBefore > _maxLayersPerPixel) pixelsOverMaxBefore++;

                    // Apply mapping rules
                    if (indexRules.Count > 0)
                    {
                        foreach (var (src, dst) in indexRules)
                        {
                            float wsrc = weights[src];
                            if (wsrc <= 0f) continue;
                            weights[dst] += wsrc;
                            weights[src] = 0f;
                        }
                    }

                    // Clamp to max layers
                    // Build indices
                    for (int l = 0; l < layerCount; l++) idx[l] = l;

                    // Sort indices by weight descending
                    Array.Sort(idx, (a, b) => weights[b].CompareTo(weights[a]));

                    // Zero out beyond max (only if they matter)
                    for (int k = _maxLayersPerPixel; k < layerCount; k++)
                    {
                        int li = idx[k];
                        if (weights[li] > 0f) weights[li] = 0f;
                    }

                    // Renormalize
                    float sum = 0f;
                    for (int l = 0; l < layerCount; l++) sum += weights[l];

                    if (sum <= 0.000001f)
                    {
                        // If everything is zero, force first layer to 1
                        weights[0] = 1f;
                        for (int l = 1; l < layerCount; l++) weights[l] = 0f;
                        sum = 1f;
                    }
                    else
                    {
                        float inv = 1f / sum;
                        for (int l = 0; l < layerCount; l++) weights[l] *= inv;
                    }

                    // Count >max after
                    int activeAfter = 0;
                    for (int l = 0; l < layerCount; l++)
                        if (weights[l] > _minWeightEpsilon) activeAfter++;
                    if (activeAfter > _maxLayersPerPixel) pixelsOverMaxAfter++;

                    // Detect changes and write back
                    bool changed = false;
                    for (int l = 0; l < layerCount; l++)
                    {
                        float oldV = maps[y, x, l];
                        float newV = weights[l];
                        if (Mathf.Abs(oldV - newV) > 0.0005f)
                        {
                            changed = true;
                            break;
                        }
                    }

                    if (changed)
                    {
                        pixelsChanged++;
                        for (int l = 0; l < layerCount; l++)
                            maps[y, x, l] = weights[l];
                    }
                }
            }

            td.SetAlphamaps(0, 0, maps);
            EditorUtility.SetDirty(td);

            Debug.Log($"[TerrainLayerOptimizer] Done. Pixels changed: {pixelsChanged:n0}. " +
                      $"Pixels > max before: {pixelsOverMaxBefore:n0}, after: {pixelsOverMaxAfter:n0}.");
            EditorUtility.DisplayDialog("Optimize Complete",
                $"Done!\n\nPixels changed: {pixelsChanged:n0}\n" +
                $"Pixels with > {_maxLayersPerPixel} active layers before: {pixelsOverMaxBefore:n0}\n" +
                $"Pixels with > {_maxLayersPerPixel} active layers after: {pixelsOverMaxAfter:n0}\n\n" +
                "Next: Re-check Frame Debugger to see fewer Terrain/Lit (Add Pass) events.",
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private bool ValidateTerrain(out TerrainData td)
    {
        td = null;
        if (_terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain", "Assign a Terrain first.", "OK");
            return false;
        }
        td = _terrain.terrainData;
        if (td == null)
        {
            EditorUtility.DisplayDialog("No TerrainData", "Selected Terrain has no TerrainData.", "OK");
            return false;
        }
        if (td.terrainLayers == null || td.terrainLayers.Length == 0)
        {
            EditorUtility.DisplayDialog("No Layers", "TerrainData has no Terrain Layers.", "OK");
            return false;
        }
        if (td.alphamapLayers != td.terrainLayers.Length)
        {
            // Usually equal, but just in case.
            Debug.LogWarning("[TerrainLayerOptimizer] alphamapLayers != terrainLayers.Length. Will use alphamapLayers.");
        }
        return true;
    }

    private static (float[,,] maps, int w, int h, int layers) GetAlphamaps(TerrainData td)
    {
        int w = td.alphamapWidth;
        int h = td.alphamapHeight;
        int layers = td.alphamapLayers;
        var maps = td.GetAlphamaps(0, 0, w, h); // [h,w,layers]
        return (maps, w, h, layers);
    }

    private static Dictionary<string, int> BuildLayerNameToIndex(TerrainData td)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var layers = td.terrainLayers;
        for (int i = 0; i < layers.Length; i++)
        {
            var tl = layers[i];
            if (tl == null) continue;

            // TerrainLayer.name is the asset name.
            string n = tl.name;
            if (!dict.ContainsKey(n))
                dict.Add(n, i);
        }
        return dict;
    }

    private void DuplicateTerrainDataAsset()
    {
        if (!ValidateTerrain(out var td)) return;

        string path = AssetDatabase.GetAssetPath(td);
        if (string.IsNullOrEmpty(path))
        {
            EditorUtility.DisplayDialog("Cannot Duplicate", "TerrainData is not an asset on disk (maybe generated at runtime).", "OK");
            return;
        }

        string newPath = AssetDatabase.GenerateUniqueAssetPath(path.Replace(".asset", "_Backup.asset"));
        AssetDatabase.CopyAsset(path, newPath);
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Backup Created",
            $"Backup TerrainData created:\n{newPath}\n\n" +
            "To use it: assign the backup asset into your Terrain's TerrainData field.",
            "OK");
    }
}
#endif
