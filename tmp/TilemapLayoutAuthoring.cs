// Temporary Unity editor tool; execute only in the isolated validation project.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using sung_wook;

public static class TilemapLayoutAuthoring
{
    private const string Base = "Assets/@Project/@sung_wook/@Game/";
    private const string Terrain = Base + "02_Prefabs/Modules/Terrain/";
    private const string Task = "C:/Users/wook1/GitBub-Unity/Science-Project-2026";
    private static readonly Color FloorColor = new Color(0.60f, 0.72f, 0.78f, 1f);
    [Serializable] private sealed class MapChange
    {
        public int stage, beforeCells, afterCells, collisionPaths;
        public string name, tile;
        public Vector3 beforePosition, afterPosition, beforeScale, afterScale;
        public bool groundTag, collisionReady;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running", unity, startedUtc = DateTime.UtcNow.ToString("O");
        public string[] modifiedAssets;
        public List<MapChange> maps = new List<MapChange>();
        public List<string> errors = new List<string>();
        public bool tutorialTerrainIndependent, tutorialMascotRetained;
    }
    private static Report report;
    private static readonly List<string> modified = new List<string>();

    public static void Apply()
    {
        string project = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
        if (!Application.isBatchMode || project != "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild")
            throw new InvalidOperationException("Use only the isolated validation copy.");
        report = new Report { unity = Application.unityVersion };
        try
        {
            AuthorTutorial(); // Detach its saved overrides before changing the shared Stage02 course.
            for (int stage = 2; stage <= 5; stage++) AuthorPrefab(stage);
            SyncLegacyAnchors(3);
            SyncLegacyAnchors(4);
            AssetDatabase.SaveAssets();
            report.modifiedAssets = modified.ToArray();
            report.status = "passed";
        }
        catch (Exception error) { report.status = "failed"; report.errors.Add(error.ToString()); throw; }
        finally
        {
            Directory.CreateDirectory(Task + "/Logs/TilemapLayout");
            File.WriteAllText(Task + "/Logs/TilemapLayout/Authoring.json", JsonUtility.ToJson(report, true));
        }
    }

    private static void AuthorTutorial()
    {
        string scenePath = ElectricStageProgression.StageScenePath(1);
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        GameObject terrain = All<Tilemap>(scene).First(m => m.gameObject.activeInHierarchy && m.name == "PlatForm").transform.parent.gameObject;
        if (!PrefabUtility.IsAnyPrefabInstanceRoot(terrain)) throw new InvalidOperationException("Tutorial terrain prefab root missing.");
        PrefabUtility.UnpackPrefabInstance(terrain, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        terrain.name = "Stage01_Terrain";
        Tilemap map = terrain.GetComponentInChildren<Tilemap>(true);
        MapChange change = Before(map, 1);
        if (change.beforeCells != 47) throw new InvalidOperationException("Expected the saved 47-cell tutorial surface.");
        TileBase tile = FindTile(map);
        // Keep all authored top surfaces. Fill only below the lowest existing cell in each column.
        var lowest = new Dictionary<int, int>();
        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
            if (map.HasTile(cell)) lowest[cell.x] = lowest.TryGetValue(cell.x, out int y) ? Mathf.Min(y, cell.y) : cell.y;
        foreach (var column in lowest)
            for (int y = -8; y < column.Value; y++) Solid(map, new Vector3Int(column.Key, y, 0), tile);
        Finish(map, change);
        string path = Terrain + "Stage01_Terrain.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(terrain, path, InteractionMode.AutomatedAction, out bool saved);
        if (!saved) throw new InvalidOperationException("Could not save independent tutorial terrain.");
        report.tutorialTerrainIndependent = true;
        Transform mascot = All<Transform>(scene).FirstOrDefault(t => t.name == "Firefly Generated Image");
        if (mascot == null) throw new InvalidOperationException("Saved tutorial artwork missing.");
        mascot.position = new Vector3(-6f, 2f, mascot.position.z);
        mascot.localScale = Vector3.one * 3f;
        report.tutorialMascotRetained = mascot.gameObject.activeSelf && mascot.GetComponent<SpriteRenderer>() != null;
        EditorUtility.SetDirty(mascot);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save tutorial scene.");
        modified.Add(path); modified.Add(path + ".meta"); modified.Add(scenePath);
    }

    private static void AuthorPrefab(int stage)
    {
        string path = Terrain + "Stage" + stage.ToString("00") + "_Terrain.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
            {
                if (!map.gameObject.activeInHierarchy) continue;
                MapChange change = Before(map, stage);
                TileBase tile = FindTile(map);
                if (stage == 2)
                {
                    for (int x = -13; x <= 10; x++) Solid(map, new Vector3Int(x, -6, 0), tile);
                    Solid(map, new Vector3Int(7, -2, 0), tile);
                }
                else if (stage == 5)
                {
                    for (int x = -10; x <= 10; x++)
                        if (map.HasTile(new Vector3Int(x, -5, 0))) Solid(map, new Vector3Int(x, -6, 0), tile);
                    Solid(map, new Vector3Int(7, -4, 0), tile);
                    Solid(map, new Vector3Int(8, -4, 0), tile);
                }
                else if (map.name.StartsWith("Route ", StringComparison.Ordinal) || map.name == "Goal Platform Tilemap")
                {
                    float oldTop = map.transform.position.y + map.transform.lossyScale.y;
                    float newTop = oldTop + (stage == 3 && map.name == "Route 02 Tilemap" ? .8f : 0f);
                    Vector3 scale = map.transform.localScale;
                    scale.y = .8f;
                    map.transform.localScale = scale;
                    Vector3 position = map.transform.position;
                    position.y = newTop - .8f;
                    map.transform.position = position;
                }
                else if (map.name == "Starting Floor Tilemap")
                {
                    for (int x = -13; x <= -1; x++) Solid(map, new Vector3Int(x, -6, 0), tile);
                }
                Finish(map, change);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved) throw new InvalidOperationException("Could not save " + path);
            modified.Add(path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void SyncLegacyAnchors(int stage)
    {
        string path = ElectricStageProgression.StageScenePath(stage);
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        foreach (Tilemap map in All<Tilemap>(scene))
        {
            if (!map.gameObject.activeInHierarchy || !(map.name.StartsWith("Route ", StringComparison.Ordinal) || map.name == "Goal Platform Tilemap")) continue;
            string name = map.name.Replace(" Tilemap", "");
            BoxCollider2D anchor = All<BoxCollider2D>(scene).FirstOrDefault(c => c.name == name && !c.enabled);
            if (anchor == null) throw new InvalidOperationException("Missing disabled authoring anchor " + name);
            Bounds bounds = WorldBounds(map);
            anchor.transform.position = bounds.center;
            anchor.offset = Vector2.zero;
            Vector3 scale = anchor.transform.lossyScale;
            anchor.size = new Vector2(bounds.size.x / Mathf.Abs(scale.x), bounds.size.y / Mathf.Abs(scale.y));
            EditorUtility.SetDirty(anchor.transform); EditorUtility.SetDirty(anchor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save authoring anchors.");
        modified.Add(path);
    }

    private static MapChange Before(Tilemap map, int stage) => new MapChange {
        stage = stage, name = map.name, beforeCells = Count(map), beforePosition = map.transform.position,
        beforeScale = map.transform.localScale, tile = AssetDatabase.GetAssetPath(FindTile(map))
    };
    private static void Finish(Tilemap map, MapChange change)
    {
        map.color = FloorColor;
        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
            if (map.HasTile(cell)) map.SetColliderType(cell, Tile.ColliderType.Grid);
        map.CompressBounds();
        TilemapCollider2D collider = map.GetComponent<TilemapCollider2D>();
        if (collider == null || !map.CompareTag("Ground")) throw new InvalidOperationException("Missing terrain collision or Ground tag.");
        collider.ProcessTilemapChanges(); Physics2D.SyncTransforms();
        CompositeCollider2D composite = map.GetComponent<CompositeCollider2D>();
        if (composite != null) composite.GenerateGeometry();
        change.collisionPaths = composite != null ? composite.pathCount : collider.shapeCount;
        change.groundTag = map.CompareTag("Ground");
        change.collisionReady = !collider.hasTilemapChanges && change.collisionPaths > 0;
        change.afterCells = Count(map); change.afterPosition = map.transform.position; change.afterScale = map.transform.localScale;
        if (!change.collisionReady) throw new InvalidOperationException("No regenerated collision geometry: " + map.name);
        EditorUtility.SetDirty(map); EditorUtility.SetDirty(map.transform); EditorUtility.SetDirty(collider);
        if (composite != null) EditorUtility.SetDirty(composite);
        report.maps.Add(change);
    }
    private static TileBase FindTile(Tilemap map)
    {
        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
            if (map.HasTile(cell)) return map.GetTile(cell);
        throw new InvalidOperationException("Empty terrain map: " + map.name);
    }
    private static int Count(Tilemap map) { int n = 0; foreach (Vector3Int c in map.cellBounds.allPositionsWithin) if (map.HasTile(c)) n++; return n; }
    private static void Solid(Tilemap map, Vector3Int cell, TileBase tile) { map.SetTile(cell, tile); map.SetColliderType(cell, Tile.ColliderType.Grid); }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));
    private static Bounds WorldBounds(Tilemap map)
    {
        Bounds local = map.localBounds;
        return new Bounds(map.transform.TransformPoint(local.center), Vector3.Scale(local.size, map.transform.lossyScale));
    }
}
