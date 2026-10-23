using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace sung_wook
{
                                                                     
    public static class ElectricStageModuleBuilder
    {
        private const string Game = "Assets/@Project/@sung_wook/@Game/";
        private const string Modules = Game + "02_Prefabs/Modules/";
        private const string HudPath = Game + "02_Prefabs/UI/StageHUD.prefab";
        private const string PanelPath = Game + "02_Prefabs/UI/Learning_Objective_Panel.prefab";
        private static readonly List<string> saved = new List<string>();
        private static Vector3 leverPosition;

        [MenuItem("Tools/sung_wook/Electric Bounce/Save Stage 02-05 Modules")]
        public static void BuildAndSaveAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before saving stage modules.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save pending scene edits before running the module builder.");

            string previous = SceneManager.GetActiveScene().path;
            saved.Clear();
            Directory.CreateDirectory(Modules + "Electric");
            Directory.CreateDirectory(Modules + "Movement");
            Directory.CreateDirectory(Modules + "Terrain");
            Directory.CreateDirectory(Modules + "Progress");
            AssetDatabase.Refresh();
            for (int stage = 2; stage <= 5; stage++)
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath(stage), OpenSceneMode.Single);
                TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(Game + "09_Tile/Normal_Block_0.asset");
                if (tile == null) throw new InvalidOperationException("Stage 2 platform tile is missing.");
                Transform root = Find(scene, "Stage " + stage.ToString("00"));
                if (root == null) throw new InvalidOperationException("Stage root missing: " + stage);
                Canvas canvas;
                if (stage == 2)
                {
                    canvas = Find(scene, "Canvas").GetComponent<Canvas>();
                                                                                        
                                                                                    
                    if (PrefabUtility.IsPartOfPrefabInstance(canvas.gameObject))
                        PrefabUtility.UnpackPrefabInstance(canvas.gameObject,
                            PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                    ConfigureHud(canvas.gameObject, stage);
                    SaveModule(canvas.transform.Find("Learning_Objective_Panel").gameObject, PanelPath);
                    SaveModule(canvas.gameObject, HudPath);
                    leverPosition = Find(scene, "Lever").position;
                    SaveModule(Find(scene, "Lever").gameObject, Modules + "Electric/StageLever.prefab");
                    SaveModule(Find(scene, "GameMaps").Find("Hexagon").gameObject,
                        Modules + "Terrain/HexBackground.prefab");
                }
                else
                {
                    Transform oldCanvas = Find(scene, "Canvas");
                    if (oldCanvas != null) Object.DestroyImmediate(oldCanvas.gameObject);
                    GameObject hud = (GameObject)PrefabUtility.InstantiatePrefab(
                        AssetDatabase.LoadAssetAtPath<GameObject>(HudPath), scene);
                    hud.name = "Canvas";
                    canvas = hud.GetComponent<Canvas>();
                    hud.GetComponent<ElectricStageHudView>().PreviewStage(stage);
                    foreach (TMP_Text text in hud.GetComponentsInChildren<TMP_Text>(true))
                    {
                        EditorUtility.SetDirty(text);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(text);
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(hud.GetComponent<ElectricStageHudView>());
                    ReplaceLever(scene);
                    ReplaceBackground(scene);
                    foreach (ElectricLearningStatusView view in root.GetComponentsInChildren<ElectricLearningStatusView>(true))
                        if (view.TryGetComponent(out Canvas oldWorldCanvas)) oldWorldCanvas.enabled = false;
                }

                ConfigureBootstrap(scene, root, stage);
                                                                                 
                                                                                 
                if (stage == 5) Find(scene, "Main Camera").GetComponent<Camera>().orthographicSize = 9.2f;
                MakeMechanism<ContactCharger2D>(root, "ContactChargingStation", "ContactChargingStation");
                MakeMechanism<DistanceLauncher2D>(root, "DistanceForceLauncher", "DistanceForceLauncher");
                MakeMechanism<UniformField2D>(root, "UniformElectricField", "UniformElectricField");
                MakeMechanism<VoltageLauncher2D>(root, "VanDeGraaffLauncher", "VanDeGraaffLauncher",
                    "Scene Art - Stage 05/Van de Graaff - Scene Artwork", "Scene Art - Stage 05/Output Cable");
                MakeMovingModules(root);
                foreach (ElectricStar2D star in root.GetComponentsInChildren<ElectricStar2D>(true))
                    SaveModule(star.gameObject, Modules + "Progress/Stage" + stage.ToString("00") + "_Star.prefab");
                foreach (ElectricGoal2D goal in root.GetComponentsInChildren<ElectricGoal2D>(true))
                    SaveModule(goal.gameObject, Modules + "Progress/Stage" + stage.ToString("00") + "_Exit.prefab");

                ElectricStageTerrainBuilder.Configure(scene, stage, tile);
                Transform maps = Find(scene, "GameMaps");
                var grids = new List<Grid>();
                foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                    grids.AddRange(sceneRoot.GetComponentsInChildren<Grid>(true));
                foreach (Grid grid in grids)
                {
                    if (grid.name == "Hexagon" || grid.name == "Rectangle") continue;
                    bool activeTerrain = false;
                    foreach (Tilemap map in grid.GetComponentsInChildren<Tilemap>())
                        activeTerrain |= map.GetComponent<TilemapCollider2D>() != null;
                    if (activeTerrain) SaveModule(grid.gameObject,
                        Modules + "Terrain/Stage" + stage.ToString("00") + "_Terrain.prefab");
                }
                foreach (Tilemap map in maps.GetComponentsInChildren<Tilemap>(true)) map.CompressBounds();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed: " + scene.path);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/StageModuleAssets.txt", saved);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            Debug.Log("Saved reusable stage modules, reference UI and tilemaps for Stage 02-05.");
        }

        public static void BuildBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("BuildBatch requires a batch editor.");
            try { BuildAndSaveAll(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static string ScenePath(int stage) => Game + "00_Scenes/Stage/Stage_" + stage.ToString("00") + ".unity";

        private static Transform Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root.transform;
            return null;
        }

        private static void ConfigureBootstrap(Scene scene, Transform stageRoot, int stage)
        {
            GameObject manager = Find(scene, "GameManager").gameObject;
            var bootstrap = manager.GetComponent<ElectricStageSceneBootstrap>();
            if (bootstrap == null) bootstrap = manager.AddComponent<ElectricStageSceneBootstrap>();
            var properties = new SerializedObject(bootstrap);
            properties.FindProperty("stageNumber").intValue = stage;
            properties.FindProperty("ball").objectReferenceValue = Find(scene, "Player").GetComponent<ElectricBall2D>();
            properties.FindProperty("lever").objectReferenceValue = Find(scene, "Lever").GetComponent<StageLever2D>();
            properties.FindProperty("stageRoot").objectReferenceValue = stageRoot.gameObject;
            properties.FindProperty("spawnPoint").objectReferenceValue = null;
            properties.ApplyModifiedPropertiesWithoutUndo();
            var progression = manager.GetComponent<ElectricStageProgression>();
            if (progression == null) progression = manager.AddComponent<ElectricStageProgression>();
            var progressionData = new SerializedObject(progression);
            Transform canvas = Find(scene, "Canvas");
            progressionData.FindProperty("completionView").objectReferenceValue = canvas != null
                ? canvas.GetComponentInChildren<ElectricGameCompleteView>(true) : null;
            progressionData.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ReplaceLever(Scene scene)
        {
            Transform previous = Find(scene, "Lever");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var lever = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(Modules + "Electric/StageLever.prefab"), scene);
            lever.name = "Lever";
            lever.transform.position = leverPosition;
                                                                                                 
            foreach (string name in new[] { "GearLever", "GearLever (1)" })
            {
                Transform legacy = Find(scene, name);
                if (legacy == null) continue;
                foreach (Renderer renderer in legacy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            }
        }

        private static void ReplaceBackground(Scene scene)
        {
            Transform maps = Find(scene, "GameMaps");
            Transform previous = maps.Find("Hexagon");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var background = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(Modules + "Terrain/HexBackground.prefab"), maps);
            background.name = "Hexagon";
            background.transform.SetAsFirstSibling();
        }

        private static void ConfigureHud(GameObject canvas, int stage)
        {
            Transform hud = canvas.transform.Find("HUD");
            Transform footer = null;
            foreach (Transform child in canvas.transform) if (child.name.Trim() == "HDD") footer = child;
            if (footer == null) throw new InvalidOperationException("Stage 2 footer is missing.");
            footer.name = "HDD";
            ConfigureBar((RectTransform)hud, true, 88f);
            ConfigureBar((RectTransform)footer, false, 64f);
            var layout = hud.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 8, 8);
            layout.spacing = 20f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            Layout(hud.Find("Stage_Number_Container").gameObject, 80f, 0f);
            Layout(hud.Find("StageName").gameObject, 650f, 0f);
            Layout(hud.Find("Quest_Container").gameObject, 600f, 1f);
            var footerLayout = footer.GetComponent<HorizontalLayoutGroup>();
            footerLayout.padding = new RectOffset(24, 24, 6, 6);
            footerLayout.childControlWidth = footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = footerLayout.childForceExpandHeight = true;
            TMP_Text stageNumber = hud.Find("Stage_Number_Container/Stage_Text").GetComponent<TMP_Text>();
            TMP_Text stageName = hud.Find("StageName").GetComponent<TMP_Text>();
            TMP_Text quest = hud.Find("Quest_Container/Quest_Text").GetComponent<TMP_Text>();
            RectTransform questRect = quest.rectTransform;
            questRect.anchorMin = Vector2.zero;
            questRect.anchorMax = Vector2.one;
            questRect.offsetMin = new Vector2(16f, 2f);
            questRect.offsetMax = new Vector2(-16f, -2f);
            TMP_Text controls = footer.GetComponentInChildren<TMP_Text>();
            TMP_Text body = canvas.transform.Find("Learning_Objective_Panel/Content/Body_Text").GetComponent<TMP_Text>();
            foreach (TMP_Text text in new[] { stageNumber, stageName, quest, controls, body })
            {
                text.enableAutoSizing = true;
                text.fontSizeMin = text == body ? 18f : 22f;
                text.fontSizeMax = text == stageNumber ? 60f : text == stageName ? 38f : text == body ? 30f : 28f;
                text.raycastTarget = false;
                text.overflowMode = TextOverflowModes.Truncate;
                text.alignment = text == body ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            }
            body.margin = new Vector4(20f, 12f, 20f, 12f);
            body.color = Color.white;
            canvas.transform.Find("Learning_Objective_Panel/Content/Head_Text")
                .GetComponentInChildren<TMP_Text>().color = Color.white;
            var view = canvas.GetComponent<ElectricStageHudView>();
            if (view == null) view = canvas.AddComponent<ElectricStageHudView>();
            var data = new SerializedObject(view);
            SetReference(data, "stageNumberText", stageNumber);
            SetReference(data, "stageNameText", stageName);
            SetReference(data, "questText", quest);
            SetReference(data, "controlsText", controls);
            SetReference(data, "objectiveBodyText", body);
            data.ApplyModifiedPropertiesWithoutUndo();
            view.PreviewStage(stage);
        }

        private static void ConfigureBar(RectTransform rect, bool top, float height)
        {
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }

        private static void Layout(GameObject target, float width, float flexible)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null) element = target.AddComponent<LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = width;
            element.flexibleWidth = flexible;
            element.minHeight = element.preferredHeight = 72f;
        }

        private static void SetReference(SerializedObject data, string name, Object value)
            => data.FindProperty(name).objectReferenceValue = value;

        private static void MakeMechanism<T>(Transform stage, string name, string assetName,
            params string[] extraPaths) where T : MonoBehaviour
        {
            T device = stage.GetComponentInChildren<T>(true);
            if (device == null) return;
            if (PrefabUtility.IsPartOfPrefabInstance(device))
            {
                saved.Add(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(device.gameObject));
                return;
            }
            var wrapper = new GameObject(name).transform;
            wrapper.SetParent(stage, false);
            wrapper.position = device.transform == stage ? DevicePivot(device) : device.transform.position;
            if (device.transform == stage)
            {
                T copy = wrapper.gameObject.AddComponent<T>();
                EditorUtility.CopySerialized(device, copy);
                Object.DestroyImmediate(device);
                device = copy;
            }
            else device.transform.SetParent(wrapper, true);

            var data = new SerializedObject(device);
            var iterator = data.GetIterator();
            var members = new HashSet<Transform>();
            while (iterator.NextVisible(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                Transform member = TransformOf(iterator.objectReferenceValue);
                if (member != null && member != stage && member.IsChildOf(stage)
                    && !member.IsChildOf(wrapper)) members.Add(member);
            }
            foreach (Transform member in members)
                if (!member.IsChildOf(wrapper)) member.SetParent(wrapper, true);
            foreach (string path in extraPaths)
            {
                Transform member = stage.Find(path);
                if (member != null) member.SetParent(wrapper, true);
            }
            ElectricStageSceneBootstrap bootstrap = Find(stage.gameObject.scene, "GameManager")
                .GetComponent<ElectricStageSceneBootstrap>();
            if (device is ContactCharger2D contact) contact.ConnectParticipants(bootstrap.Ball, bootstrap.Lever);
            if (device is DistanceLauncher2D distance) distance.ConnectParticipants(bootstrap.Ball, bootstrap.Lever);
            if (device is UniformField2D field) field.ConnectParticipants(bootstrap.Ball, bootstrap.Lever);
            if (device is VoltageLauncher2D voltage) voltage.ConnectParticipants(bootstrap.Ball, bootstrap.Lever);
            SaveModule(wrapper.gameObject, Modules + "Electric/" + assetName + ".prefab");
        }

        private static Vector3 DevicePivot(MonoBehaviour device)
        {
            var data = new SerializedObject(device);
            foreach (string field in new[] { "launchZone", "source" })
            {
                SerializedProperty property = data.FindProperty(field);
                Transform target = property == null ? null : TransformOf(property.objectReferenceValue);
                if (target != null) return target.position;
            }
            return device.transform.position;
        }

        private static void MakeMovingModules(Transform stage)
        {
            int index = 0;
            foreach (ElectricObstacleMotion2D motion in stage.GetComponentsInChildren<ElectricObstacleMotion2D>(true))
            {
                index++;
                if (PrefabUtility.IsPartOfPrefabInstance(motion))
                {
                    saved.Add(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(motion.gameObject));
                    continue;
                }
                Vector3 oldLocal = motion.transform.localPosition;
                Transform parent = motion.transform.parent;
                var wrapper = new GameObject("MovingPlatform_" + index.ToString("00")).transform;
                wrapper.SetParent(parent, false);
                wrapper.localPosition = oldLocal;
                var data = new SerializedObject(motion);
                data.FindProperty("startLocalPosition").vector2Value -= (Vector2)oldLocal;
                data.FindProperty("endLocalPosition").vector2Value -= (Vector2)oldLocal;
                data.ApplyModifiedPropertiesWithoutUndo();
                motion.transform.SetParent(wrapper, true);
                SaveModule(wrapper.gameObject, Modules + "Movement/MovingPlatform_" + index.ToString("00") + ".prefab");
            }
        }

        private sealed class ExternalReference
        {
            public Component component;
            public string property;
            public Object value;
        }

        private static Transform TransformOf(Object value)
            => value is Component component ? component.transform : value is GameObject go ? go.transform : null;

        private static void SaveModule(GameObject root, string path)
        {
                                                                                                 
            var external = new List<ExternalReference>();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) throw new InvalidOperationException("Missing script in module " + root.name);
                var data = new SerializedObject(component);
                var property = data.GetIterator();
                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Transform target = TransformOf(property.objectReferenceValue);
                    if (target == null || !target.gameObject.scene.IsValid() || target == root.transform
                        || target.IsChildOf(root.transform) || property.propertyPath == "m_Father") continue;
                    external.Add(new ExternalReference { component = component,
                        property = property.propertyPath, value = property.objectReferenceValue });
                    property.objectReferenceValue = null;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            if (PrefabUtility.GetOutermostPrefabInstanceRoot(root) == root
                && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == path)
                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
            else
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, path, InteractionMode.AutomatedAction, out bool success);
                if (!success) throw new IOException("Prefab save failed: " + path);
            }
            foreach (ExternalReference reference in external)
            {
                var data = new SerializedObject(reference.component);
                data.FindProperty(reference.property).objectReferenceValue = reference.value;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(reference.component);
            }
            saved.Add(path);
        }
    }
}
