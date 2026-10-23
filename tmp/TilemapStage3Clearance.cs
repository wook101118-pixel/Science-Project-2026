// Temporary authoring command: copy only into the isolated clone Assets/Editor.
// Unity -batchmode -quit -projectPath <clone> -executeMethod TilemapStage3Clearance.Apply
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using sung_wook;

public static class TilemapStage3Clearance
{
    private const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    private const string Task = "C:/Users/wook1/GitBub-Unity/Science-Project-2026";
    private const string Terrain = "Assets/@Project/@sung_wook/@Game/02_Prefabs/Modules/Terrain/Stage03_Terrain.prefab";
    [Serializable] private sealed class Report
    {
        public string status = "running", unity, startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc, error;
        public string[] savedAssets;
        public Vector3 shelfBeforePosition, shelfAfterPosition, shelfAfterScale;
        public Vector3 stoneBeforeLocalPosition, stoneAfterLocalPosition, wrapperBeforePosition, wrapperAfterPosition;
        public Vector2 motionBeforeStart, motionBeforeEnd, motionAfterStart, motionAfterEnd;
        public float shelfLeft, shelfRight, shelfTop, shelfBottom, stoneWidth, stoneHeight, playerDiameter;
        public float oldMinimumGap, newMinimumGap, playerVerticalMargin, waitCenter = 4.3f, waitHorizontalMargin;
        public float oneWayDuration, startDelay, startPause, endPause;
        public int shelfCells, colliderShapes;
        public bool motionAmplitudeUnchanged, motionTimingUnchanged, stoneColliderUnchanged, stoneTagUnchanged, wrapperUnchanged, anchorSynchronized;
    }
    private static Report report;
    public static void Apply()
    {
        string project = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
        if (!Application.isBatchMode || project != Clone) throw new InvalidOperationException("Use only the isolated authoring clone.");
        report = new Report { unity = Application.unityVersion };
        string scenePath = ElectricStageProgression.StageScenePath(3);
        try
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(Terrain);
            try
            {
                Tilemap map = contents.GetComponentsInChildren<Tilemap>(true).Single(m => m.name == "Route 02 Tilemap");
                report.shelfBeforePosition = map.transform.position;
                Require(Near(map.transform.position.x, 4f) && Near(map.transform.position.y, -2.6f)
                    && Near(map.transform.lossyScale.x, 1f) && Near(map.transform.lossyScale.y, .8f), "Unexpected saved shelf geometry; do not apply twice.");
                TileBase tile = map.GetTile(Vector3Int.zero);
                Require(tile != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tile)) == "3409f095d95fe8149bf33a22b0093674", "Unexpected normal terrain tile.");
                map.ClearAllTiles();
                for (int x = 0; x < 3; x++)
                {
                    var cell = new Vector3Int(x, 0, 0);
                    map.SetTile(cell, tile); map.SetColliderType(cell, Tile.ColliderType.Grid);
                }
                Vector3 position = map.transform.position; position.x = 3.8f; map.transform.position = position;
                Vector3 scale = map.transform.localScale; scale.x = .8f; map.transform.localScale = scale;
                map.CompressBounds();
                var collider = map.GetComponent<TilemapCollider2D>();
                Require(collider != null && collider.enabled && map.CompareTag("Ground"), "Shelf collision configuration missing.");
                collider.ProcessTilemapChanges(); Physics2D.SyncTransforms();
                Require(collider.shapeCount == 3, "Expected three actual shelf tile collision shapes.");
                report.colliderShapes = collider.shapeCount; report.shelfCells = 3;
                report.shelfAfterPosition = map.transform.position; report.shelfAfterScale = map.transform.localScale;
                EditorUtility.SetDirty(map); EditorUtility.SetDirty(map.transform); EditorUtility.SetDirty(collider);
                PrefabUtility.SaveAsPrefabAsset(contents, Terrain, out bool saved);
                Require(saved, "Could not save widened shelf prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var stone = all.Single(t => t.name == "Obstacle Blue Falling Stone 02" && t.gameObject.activeInHierarchy);
            var motion = stone.GetComponent<ElectricObstacleMotion2D>();
            var box = stone.GetComponent<BoxCollider2D>();
            Require(motion != null && box != null && !box.isTrigger && !stone.CompareTag("Ground"), "Saved non-bouncy moving stone missing.");
            var data = new SerializedObject(motion);
            var start = data.FindProperty("startLocalPosition"); var end = data.FindProperty("endLocalPosition");
            report.motionBeforeStart = start.vector2Value; report.motionBeforeEnd = end.vector2Value;
            report.stoneBeforeLocalPosition = stone.localPosition;
            report.wrapperBeforePosition = stone.parent.position;
            report.oneWayDuration = data.FindProperty("oneWayDuration").floatValue;
            report.startDelay = data.FindProperty("startDelay").floatValue;
            report.startPause = data.FindProperty("startPause").floatValue;
            report.endPause = data.FindProperty("endPause").floatValue;
            Require(Near(report.motionBeforeStart.y, 0f) && Near(report.motionBeforeEnd.y, -3.6f)
                && Near(stone.localPosition.y, 0f) && Near(stone.parent.position.x, 5.3f)
                && Near(stone.parent.position.y, 2.8f) && Near(stone.parent.lossyScale.y, 1f), "Unexpected absolute-local motion; do not apply twice.");
            Vector2 oldSize = box.size, oldOffset = box.offset; Vector3 oldScale = stone.localScale; string oldTag = stone.tag;
            Physics2D.SyncTransforms();
            report.stoneWidth = box.bounds.size.x; report.stoneHeight = box.bounds.size.y;
            start.vector2Value += Vector2.up * .8f; end.vector2Value += Vector2.up * .8f;
            data.ApplyModifiedPropertiesWithoutUndo();
            Vector3 childPosition = stone.localPosition; childPosition.y += .8f; stone.localPosition = childPosition;
            EditorUtility.SetDirty(motion); EditorUtility.SetDirty(stone);
            PrefabUtility.RecordPrefabInstancePropertyModifications(motion);
            PrefabUtility.RecordPrefabInstancePropertyModifications(stone);

            Tilemap shelf = all.Select(t => t.GetComponent<Tilemap>()).Where(m => m != null && m.gameObject.activeInHierarchy).Single(m => m.name == "Route 02 Tilemap");
            Bounds shelfBounds = WorldBounds(shelf);
            report.shelfLeft = shelfBounds.min.x; report.shelfRight = shelfBounds.max.x;
            report.shelfTop = shelfBounds.max.y; report.shelfBottom = shelfBounds.min.y;
            var anchor = all.Select(t => t.GetComponent<BoxCollider2D>()).Where(c => c != null && c.gameObject.activeInHierarchy).Single(c => c.name == "Route 02" && !c.enabled);
            anchor.transform.position = shelfBounds.center; anchor.offset = Vector2.zero;
            Vector3 anchorScale = anchor.transform.lossyScale;
            anchor.size = new Vector2(shelfBounds.size.x / Mathf.Abs(anchorScale.x), shelfBounds.size.y / Mathf.Abs(anchorScale.y));
            EditorUtility.SetDirty(anchor.transform); EditorUtility.SetDirty(anchor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
            report.anchorSynchronized = Near(anchor.transform.position.x, 5f) && Near(anchor.transform.position.y, -2.2f)
                && Near(anchor.size.x * Mathf.Abs(anchorScale.x), 2.4f) && Near(anchor.size.y * Mathf.Abs(anchorScale.y), .8f);

            data.Update(); report.motionAfterStart = start.vector2Value; report.motionAfterEnd = end.vector2Value;
            report.stoneAfterLocalPosition = stone.localPosition; report.wrapperAfterPosition = stone.parent.position;
            var ball = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ElectricBall2D>(true)).Single(b => b.gameObject.activeInHierarchy);
            Physics2D.SyncTransforms(); report.playerDiameter = ball.GetComponent<Collider2D>().bounds.size.y;
            float oldLowestCenter = Mathf.Min(stone.parent.TransformPoint(report.motionBeforeStart).y, stone.parent.TransformPoint(report.motionBeforeEnd).y);
            float newLowestCenter = Mathf.Min(stone.parent.TransformPoint(report.motionAfterStart).y, stone.parent.TransformPoint(report.motionAfterEnd).y);
            report.oldMinimumGap = oldLowestCenter - report.stoneHeight * .5f - report.shelfTop;
            report.newMinimumGap = newLowestCenter - report.stoneHeight * .5f - report.shelfTop;
            report.playerVerticalMargin = report.newMinimumGap - report.playerDiameter;
            float stoneLeft = stone.parent.TransformPoint(report.motionAfterEnd).x - report.stoneWidth * .5f;
            report.waitHorizontalMargin = stoneLeft - (report.waitCenter + report.playerDiameter * .5f);
            report.motionAmplitudeUnchanged = Vector2.Distance(report.motionBeforeEnd - report.motionBeforeStart, report.motionAfterEnd - report.motionAfterStart) < .0001f;
            report.motionTimingUnchanged = Near(data.FindProperty("oneWayDuration").floatValue, report.oneWayDuration)
                && Near(data.FindProperty("startDelay").floatValue, report.startDelay)
                && Near(data.FindProperty("startPause").floatValue, report.startPause)
                && Near(data.FindProperty("endPause").floatValue, report.endPause);
            report.stoneColliderUnchanged = box.size == oldSize && box.offset == oldOffset && stone.localScale == oldScale;
            report.stoneTagUnchanged = stone.tag == oldTag;
            report.wrapperUnchanged = stone.parent.position == report.wrapperBeforePosition;
            Require(Near(report.shelfLeft, 3.8f) && Near(report.shelfRight, 6.2f) && Near(report.shelfTop, -1.8f)
                && Near(report.shelfBottom, -2.6f), "Saved shelf bounds mismatch.");
            Require(report.motionAmplitudeUnchanged && report.motionTimingUnchanged && report.stoneColliderUnchanged
                && report.stoneTagUnchanged && report.wrapperUnchanged && report.anchorSynchronized, "Unintended motion or anchor change.");
            Require(report.playerVerticalMargin > .47f && report.waitHorizontalMargin > .08f, "Insufficient physical player clearance.");
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Could not save Stage03 scene.");
            AssetDatabase.SaveAssets(); report.savedAssets = new[] { Terrain, scenePath };
            report.status = "passed";
        }
        catch (Exception error) { report.status = "failed"; report.error = error.ToString(); throw; }
        finally
        {
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Task + "/Logs/TilemapLayout");
            File.WriteAllText(Task + "/Logs/TilemapLayout/Stage3Clearance.json", JsonUtility.ToJson(report, true));
        }
    }
    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < .0001f;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Bounds WorldBounds(Tilemap map) => new Bounds(map.transform.TransformPoint(map.localBounds.center), Vector3.Scale(map.localBounds.size, map.transform.lossyScale));
}
