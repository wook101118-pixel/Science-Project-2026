// Temporary authoring utility. Run only inside the isolated Unity validation copy.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using sung_wook;

public static class SfxSceneBuilder
{
    private const string Root = "Assets/@Project/@sung_wook/@Game/";
    private const string TransitionPath = Root + "02_Prefabs/UI/SceneTransition.prefab";
    private const string PluckGuid = "dc430f920d844120be565ad54f1f5956";
    private const string SelectGuid = "4487cd014ac14bd6b1d3196f797f9c08";
    private const string ConfirmationGuid = "a75b84593e25485daadd2cc995d146b8";

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string transitionPrefab;
        public int gameplaySources;
        public int uiSources;
        public int cues;
        public List<string> changedSceneObjects = new List<string>();
    }

    public static void Build()
    {
        if (!Application.isBatchMode ||
            !Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"))
            throw new InvalidOperationException("Author only in the isolated validation project.");

        var report = new Report { transitionPrefab = TransitionPath };
        AudioClip pluck = ExistingClip(PluckGuid);
        AudioClip select = ExistingClip(SelectGuid);
        AudioClip confirmation = ExistingClip(ConfirmationGuid);

        GameObject transition = PrefabUtility.LoadPrefabContents(TransitionPath);
        try
        {
            Transform gameplay = FindOrCreateChild(transition.transform, "Gameplay SFX");
            Transform ui = FindOrCreateChild(transition.transform, "UI SFX");
            AudioSource[] gameplaySources = EnsureSources(gameplay.gameObject, 6, false);
            AudioSource uiSource = EnsureSources(ui.gameObject, 1, true)[0];
            ElectricSfxPlayer player = transition.GetComponent<ElectricSfxPlayer>();
            if (player == null) player = transition.AddComponent<ElectricSfxPlayer>();
            var serialized = new SerializedObject(player);
            SerializedProperty sourceList = Required(serialized, "gameplaySources");
            sourceList.arraySize = gameplaySources.Length;
            for (int index = 0; index < gameplaySources.Length; index++)
                sourceList.GetArrayElementAtIndex(index).objectReferenceValue = gameplaySources[index];
            Required(serialized, "uiSource").objectReferenceValue = uiSource;
            Required(serialized, "masterVolume").floatValue = 0.7f;
            SerializedProperty cues = Required(serialized, "cues");
            cues.arraySize = 8;
            SetCue(cues, 0, SfxCue.Bounce, pluck, 0.32f, 0.9f, 0.085f);
            SetCue(cues, 1, SfxCue.Charge, select, 0.4f, 1.15f, 0.07f);
            SetCue(cues, 2, SfxCue.Star, pluck, 0.6f, 1.5f, 0.06f);
            SetCue(cues, 3, SfxCue.Launch, confirmation, 0.4f, 1.4f, 0.1f);
            SetCue(cues, 4, SfxCue.Record, select, 0.45f, 1f, 0.06f);
            SetCue(cues, 5, SfxCue.LearningComplete, confirmation, 0.4f, 1.12f, 0.1f);
            SetCue(cues, 6, SfxCue.StageClear, confirmation, 0.65f, 1f, 0.1f);
            SetCue(cues, 7, SfxCue.UiClick, select, 0.45f, 1f, 0.04f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (PrefabUtility.SaveAsPrefabAsset(transition, TransitionPath) == null)
                throw new InvalidOperationException("Could not save the authored SFX sources and references.");
            report.gameplaySources = gameplaySources.Length;
            report.uiSources = 1;
            report.cues = cues.arraySize;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(transition);
        }

        Scene menu = EditorSceneManager.OpenScene(Root + "00_Scenes/MainMenu.unity", OpenSceneMode.Single);
        Camera menuCamera = null;
        foreach (GameObject sceneRoot in menu.GetRootGameObjects())
            foreach (Camera camera in sceneRoot.GetComponentsInChildren<Camera>(true))
                if (camera.CompareTag("MainCamera")) menuCamera = camera;
        if (menuCamera == null) throw new InvalidOperationException("The existing Main Menu camera was not found.");
        if (menuCamera.GetComponent<AudioListener>() == null)
        {
            AudioListener listener = menuCamera.gameObject.AddComponent<AudioListener>();
            if (!EditorSceneManager.SaveScene(menu)) throw new InvalidOperationException("Could not save menu AudioListener.");
            report.changedSceneObjects.Add(menu.path + " :: added AudioListener " + Id(listener) + " to " + menuCamera.name);
        }

        for (int number = 1; number <= 5; number++)
        {
            Scene stage = EditorSceneManager.OpenScene(Root + "00_Scenes/Stage/Stage_" + number.ToString("00") + ".unity", OpenSceneMode.Single);
            bool changed = false;
            foreach (GameObject sceneRoot in stage.GetRootGameObjects())
            {
                foreach (SoundManager legacy in sceneRoot.GetComponentsInChildren<SoundManager>(true))
                {
                    if (legacy.enabled)
                    {
                        legacy.enabled = false;
                        EditorUtility.SetDirty(legacy);
                        changed = true;
                        report.changedSceneObjects.Add(stage.path + " :: disabled legacy SoundManager " + Id(legacy));
                    }
                    foreach (AudioSource source in legacy.GetComponents<AudioSource>())
                    {
                        if (source.clip != null || !source.playOnAwake) continue;
                        source.playOnAwake = false;
                        EditorUtility.SetDirty(source);
                        changed = true;
                        report.changedSceneObjects.Add(stage.path + " :: cleared empty legacy AudioSource playOnAwake " + Id(source));
                    }
                    if (legacy.gameObject.activeSelf)
                    {
                        if (legacy.transform.parent != null || legacy.gameObject.name != "SoundManager")
                            throw new InvalidOperationException("The unused legacy SoundManager is not its expected dedicated scene root.");
                        legacy.gameObject.SetActive(false);
                        EditorUtility.SetDirty(legacy.gameObject);
                        changed = true;
                        report.changedSceneObjects.Add(stage.path + " :: deactivated unused legacy SoundManager root " + Id(legacy.gameObject));
                    }
                }
            }
            if (changed && !EditorSceneManager.SaveScene(stage))
                throw new InvalidOperationException("Could not save legacy SFX cleanup for " + stage.path);
        }

        AssetDatabase.SaveAssets();
        report.passed = true;
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/SfxAuthoringAudit.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log("SFX authoring completed: " + output);
    }

    private static string Id(UnityEngine.Object value)
    {
        return GlobalObjectId.GetGlobalObjectIdSlow(value).targetObjectId.ToString();
    }

    private static AudioClip ExistingClip(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) throw new InvalidOperationException("An existing SFX clip is missing: " + guid);
        return clip;
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        var created = new GameObject(name);
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static AudioSource[] EnsureSources(GameObject target, int count, bool isUi)
    {
        var sources = new List<AudioSource>(target.GetComponents<AudioSource>());
        if (sources.Count > count)
            throw new InvalidOperationException("Unexpected extra authored AudioSources on " + target.name);
        while (sources.Count < count) sources.Add(target.AddComponent<AudioSource>());
        foreach (AudioSource source in sources)
        {
            source.clip = null;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.volume = 1f;
            source.pitch = 1f;
            source.ignoreListenerPause = isUi;
            EditorUtility.SetDirty(source);
        }
        return sources.ToArray();
    }

    private static SerializedProperty Required(SerializedObject owner, string name)
    {
        SerializedProperty property = owner.FindProperty(name);
        if (property == null) throw new InvalidOperationException("Missing ElectricSfxPlayer serialized field: " + name);
        return property;
    }

    private static void SetCue(SerializedProperty cues, int index, SfxCue cue, AudioClip clip, float volume, float pitch, float interval)
    {
        SerializedProperty element = cues.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("cue").intValue = (int)cue;
        element.FindPropertyRelative("clip").objectReferenceValue = clip;
        element.FindPropertyRelative("volume").floatValue = volume;
        element.FindPropertyRelative("pitch").floatValue = pitch;
        element.FindPropertyRelative("minimumInterval").floatValue = interval;
    }
}
