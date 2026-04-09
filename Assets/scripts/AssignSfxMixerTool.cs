#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// MNR utility: finds AudioSources in loaded scenes and/or project prefabs,
/// then assigns them to the chosen SFX AudioMixerGroup.
/// 
/// Safe defaults:
/// - skips sources that already have an output group
/// - skips common music/BGM names
/// - includes inactive objects
/// 
/// Put this file in an Editor folder:
/// Assets/Editor/AssignSfxMixerTool.cs
/// </summary>
public class AssignSfxMixerTool : EditorWindow
{
    private AudioMixerGroup sfxGroup;

    [SerializeField] private bool processLoadedScenes = true;
    [SerializeField] private bool processProjectPrefabs = false;
    [SerializeField] private bool includeInactive = true;
    [SerializeField] private bool overrideExistingOutputGroup = false;

    [SerializeField]
    private string excludeKeywordsCsv =
        "music,bgm,theme,soundtrack,menu music,gameplay music,battle music,ambient music,musicmanager";

    private Vector2 scroll;
    private readonly List<string> previewLines = new List<string>();

    [MenuItem("Tools/MNR/Assign SFX Mixer Tool")]
    public static void ShowWindow()
    {
        GetWindow<AssignSfxMixerTool>("Assign SFX Mixer");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Assign SFX Mixer To AudioSources", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "This tool assigns a chosen SFX AudioMixerGroup to AudioSources in loaded scenes and/or prefabs.\n\n" +
            "Safe defaults:\n" +
            "- already-routed AudioSources are left alone\n" +
            "- common music names are skipped\n" +
            "- inactive objects are included",
            MessageType.Info);

        sfxGroup = (AudioMixerGroup)EditorGUILayout.ObjectField(
            "SFX Mixer Group",
            sfxGroup,
            typeof(AudioMixerGroup),
            false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Where To Search", EditorStyles.boldLabel);
        processLoadedScenes = EditorGUILayout.ToggleLeft("Loaded Scenes", processLoadedScenes);
        processProjectPrefabs = EditorGUILayout.ToggleLeft("Project Prefabs", processProjectPrefabs);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
        includeInactive = EditorGUILayout.ToggleLeft("Include Inactive Objects", includeInactive);
        overrideExistingOutputGroup = EditorGUILayout.ToggleLeft("Override Existing Output Group", overrideExistingOutputGroup);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Skip Keywords", EditorStyles.boldLabel);
        excludeKeywordsCsv = EditorGUILayout.TextField("Exclude Names / Clip Names", excludeKeywordsCsv);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(sfxGroup == null || (!processLoadedScenes && !processProjectPrefabs)))
        {
            if (GUILayout.Button("Preview Matches", GUILayout.Height(30)))
            {
                BuildPreview();
            }

            if (GUILayout.Button("Assign SFX Mixer", GUILayout.Height(36)))
            {
                AssignMixerGroup();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (previewLines.Count == 0)
        {
            EditorGUILayout.HelpBox("No preview yet.", MessageType.None);
        }
        else
        {
            foreach (string line in previewLines)
                EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    private void BuildPreview()
    {
        previewLines.Clear();

        HashSet<string> keywords = GetExcludeKeywords();
        int count = 0;

        if (processLoadedScenes)
        {
            foreach (AudioSource source in EnumerateSceneAudioSources(includeInactive))
            {
                if (!ShouldProcess(source, keywords))
                    continue;

                previewLines.Add("[Scene] " + GetHierarchyPath(source.transform));
                count++;
            }
        }

        if (processProjectPrefabs)
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    AudioSource[] sources = prefabRoot.GetComponentsInChildren<AudioSource>(true);
                    foreach (AudioSource source in sources)
                    {
                        if (!ShouldProcess(source, keywords))
                            continue;

                        previewLines.Add("[Prefab] " + path + " -> " + GetHierarchyPath(source.transform));
                        count++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        if (count == 0)
            previewLines.Add("No matching AudioSources found with current settings.");
        else
            previewLines.Insert(0, "Matches found: " + count);
    }

    private void AssignMixerGroup()
    {
        if (sfxGroup == null)
        {
            EditorUtility.DisplayDialog("Assign SFX Mixer", "Please assign an SFX Mixer Group first.", "OK");
            return;
        }

        HashSet<string> keywords = GetExcludeKeywords();

        int changedSceneSources = 0;
        int changedPrefabs = 0;
        int changedPrefabSources = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            if (processLoadedScenes)
            {
                foreach (AudioSource source in EnumerateSceneAudioSources(includeInactive))
                {
                    if (!ShouldProcess(source, keywords))
                        continue;

                    Undo.RecordObject(source, "Assign SFX Mixer Group");
                    source.outputAudioMixerGroup = sfxGroup;
                    EditorUtility.SetDirty(source);
                    changedSceneSources++;
                }

                if (changedSceneSources > 0)
                    EditorSceneManager.MarkAllScenesDirty();
            }

            if (processProjectPrefabs)
            {
                string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
                foreach (string guid in prefabGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    bool prefabChanged = false;

                    try
                    {
                        AudioSource[] sources = prefabRoot.GetComponentsInChildren<AudioSource>(true);
                        foreach (AudioSource source in sources)
                        {
                            if (!ShouldProcess(source, keywords))
                                continue;

                            source.outputAudioMixerGroup = sfxGroup;
                            prefabChanged = true;
                            changedPrefabSources++;
                        }

                        if (prefabChanged)
                        {
                            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                            changedPrefabs++;
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        BuildPreview();

        EditorUtility.DisplayDialog(
            "Assign SFX Mixer Complete",
            "Updated scene AudioSources: " + changedSceneSources + "\n" +
            "Updated prefabs: " + changedPrefabs + "\n" +
            "Updated prefab AudioSources: " + changedPrefabSources,
            "OK");
    }

    private bool ShouldProcess(AudioSource source, HashSet<string> keywords)
    {
        if (source == null)
            return false;

        if (!overrideExistingOutputGroup && source.outputAudioMixerGroup != null)
            return false;

        if (source.outputAudioMixerGroup == sfxGroup)
            return false;

        string objectName = source.gameObject.name.ToLowerInvariant();
        string clipName = source.clip != null ? source.clip.name.ToLowerInvariant() : string.Empty;

        foreach (string keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                continue;

            if (objectName.Contains(keyword) || clipName.Contains(keyword))
                return false;
        }

        return true;
    }

    private HashSet<string> GetExcludeKeywords()
    {
        return new HashSet<string>(
            excludeKeywordsCsv
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim().ToLowerInvariant())
                .Where(k => !string.IsNullOrWhiteSpace(k)));
    }

    private static IEnumerable<AudioSource> EnumerateSceneAudioSources(bool includeInactiveObjects)
    {
        int sceneCount = SceneManager.sceneCount;

        for (int i = 0; i < sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                AudioSource[] sources = root.GetComponentsInChildren<AudioSource>(includeInactiveObjects);
                foreach (AudioSource source in sources)
                    yield return source;
            }
        }
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return "<null>";

        List<string> parts = new List<string>();
        Transform current = target;

        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }
}
#endif
