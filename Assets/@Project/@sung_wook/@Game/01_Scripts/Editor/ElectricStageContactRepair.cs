using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace sung_wook
{
                 
                                             
                                                   
                  
    [InitializeOnLoad]
    public static class ElectricStageContactRepair
    {
        private const string SessionStamp = "sung_wook.ElectricBounce.ContactLayoutRepaired.v1";
        private const string UndoLabel = "Repair Electric Bounce Stage 02 contact layout";
        private const string RootName = "Electric Bounce Stages 02-05";
        private const float PositionTolerance = 0.0001f;
        private static readonly Vector3 BrokenStageOffset = new Vector3(4.99f, 1.03f, 0f);
        private static readonly Vector3 SpawnPosition = new Vector3(-3f, -3.35f, 0f);
        private static readonly Vector3 RestPosition = new Vector3(-4.3f, -3.7f, 0f);
        private static readonly Vector3 ContactPosition = new Vector3(-3.4f, -3.7f, 0f);
        private static bool retryScheduled;

        static ElectricStageContactRepair()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ScheduleAutoRepair();
        }

                                                                        
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.path == ElectricBounceSceneInstaller.ScenePath) ScheduleAutoRepair();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) ScheduleAutoRepair();
        }

        private static void ScheduleAutoRepair()
        {
            if (Application.isBatchMode || retryScheduled || SessionState.GetBool(SessionStamp, false)) return;
            retryScheduled = true;
            EditorApplication.delayCall += AutoRepairWhenIdle;
        }

                     
                                                    
                                                        
                      
        private static void AutoRepairWhenIdle()
        {
            retryScheduled = false;
            if (Application.isBatchMode || SessionState.GetBool(SessionStamp, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleAutoRepair();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            Scene scene = SceneManager.GetActiveScene();
            if (!TryGetKnownLayout(scene, out _, out _, out _)) return;
            bool changed = TryRepair(scene, out string message);
                                                            
            SessionState.SetBool(SessionStamp, true);
            if (changed) Debug.Log(message);
        }

        [MenuItem("Tools/sung_wook/Electric Bounce/Repair Stage 02 Contact Layout")]
        public static void RepairCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("2번 스테이지 접촉 배치는 Edit Mode에서 복구할 수 있습니다.");
                return;
            }
            if (!TryGetKnownLayout(scene, out _, out _, out string reason))
            {
                Debug.LogWarning("접촉 배치를 변경하지 않았습니다: " + reason);
                return;
            }
            TryRepair(scene, out string message);
            SessionState.SetBool(SessionStamp, true);
            Debug.Log(message);
        }

                                                              
        public static void RepairForBatch()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("RepairForBatch requires an explicitly launched batch editor.");
            Scene scene = EditorSceneManager.OpenScene(ElectricBounceSceneInstaller.ScenePath, OpenSceneMode.Single);
            if (!TryGetKnownLayout(scene, out _, out _, out string reason))
                throw new InvalidOperationException("Stage 02 contact repair skipped: " + reason);
            TryRepair(scene, out string message);
            Debug.Log(message);
        }

                     
                                                                
                                                      
                      
        public static bool TryRepair(Scene scene, out string message)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                message = "Play Mode에서는 씬 배치를 변경하지 않습니다.";
                return false;
            }
            if (!TryGetKnownLayout(scene, out Transform stage, out Transform contact, out message)) return false;
            if (SamePosition(stage.localPosition, Vector3.zero))
            {
                message = "2번 스테이지 접촉 배치는 이미 정상입니다. 추가 변경은 없습니다.";
                return false;
            }

            bool wasDirty = scene.isDirty;
            Vector3 before = contact.position;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            Undo.RecordObject(stage, UndoLabel);
            stage.localPosition = Vector3.zero;
            EditorUtility.SetDirty(stage);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);

            bool saved = !wasDirty && EditorSceneManager.SaveScene(scene);
            message = "2번 스테이지 접촉 배치 복구: 전극 접촉점 " + before.ToString("F2")
                + " → " + contact.position.ToString("F2")
                + ". 기존 플레이어·레버 참조를 유지했습니다. "
                + (saved ? "씬을 저장했습니다." : "열린 씬의 미저장 변경을 유지했습니다. 확인 후 저장하세요.");
            return true;
        }

                     
                                                     
                                                
                      
        private static bool TryGetKnownLayout(Scene scene, out Transform stage,
            out Transform contact, out string reason)
        {
            stage = null;
            contact = null;
            reason = "원본 sungwook 씬의 알려진 설치 구조가 아닙니다.";
            if (!scene.IsValid() || !scene.isLoaded || scene.path != ElectricBounceSceneInstaller.ScenePath) return false;

            Transform root = null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name != RootName) continue;
                if (root != null) return false;                               
                root = candidate.transform;
            }
            if (root == null || !HasDefaultTransform(root)) return false;
            ElectricStageSelector selector = root.GetComponent<ElectricStageSelector>();
            if (selector == null || selector.Ball == null || selector.Lever == null
                || selector.StageRoots == null || selector.StageRoots.Length != 4) return false;

            stage = root.Find("Stage 02");
            Transform spawn = root.Find("Restart Spawn");
            if (stage == null || spawn == null || selector.StageRoots[0] != stage.gameObject
                || selector.SpawnPoint != spawn || !SamePosition(spawn.localPosition, SpawnPosition)) return false;
            if (!SameRotationAndScale(stage) || (!SamePosition(stage.localPosition, BrokenStageOffset)
                && !SamePosition(stage.localPosition, Vector3.zero)))
            {
                reason = "2번 스테이지가 알려진 오류 위치와 다르게 편집되어 있습니다.";
                return false;
            }

            Transform rest = stage.Find("Rest Position");
            contact = stage.Find("Contact Position");
            Transform electrode = stage.Find("Contact Electrode");
            if (rest == null || contact == null || electrode == null
                || !SamePosition(rest.localPosition, RestPosition)
                || !SamePosition(contact.localPosition, ContactPosition)
                || !SamePosition(electrode.localPosition, RestPosition)) return false;

            ContactCharger2D charger = electrode.GetComponent<ContactCharger2D>();
            BoxCollider2D collider = electrode.GetComponent<BoxCollider2D>();
            if (charger == null || collider == null || collider.isTrigger) return false;
            var properties = new SerializedObject(charger);
            if (!HasReference(properties, "ball", selector.Ball)
                || !HasReference(properties, "lever", selector.Lever)
                || !HasReference(properties, "restPoint", rest)
                || !HasReference(properties, "contactPoint", contact)) return false;

            reason = string.Empty;
            return true;
        }

        private static bool HasReference(SerializedObject properties, string name, UnityEngine.Object expected)
        {
            SerializedProperty property = properties.FindProperty(name);
            return property != null && property.objectReferenceValue == expected;
        }

        private static bool HasDefaultTransform(Transform value)
        {
            return SamePosition(value.localPosition, Vector3.zero) && SameRotationAndScale(value);
        }

        private static bool SameRotationAndScale(Transform value)
        {
            return Quaternion.Angle(value.localRotation, Quaternion.identity) <= PositionTolerance
                && SamePosition(value.localScale, Vector3.one);
        }

        private static bool SamePosition(Vector3 actual, Vector3 expected)
        {
            return (actual - expected).sqrMagnitude <= PositionTolerance * PositionTolerance;
        }
    }
}
