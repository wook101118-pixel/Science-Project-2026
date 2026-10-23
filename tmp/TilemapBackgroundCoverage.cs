// Clone-only background authoring. Existing source scenes and source assets are never saved.
// Unity.exe -batchmode -quit -projectPath <clone> -executeMethod TilemapBackgroundCoverage.Apply
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using sung_wook;

public static class TilemapBackgroundCoverage
{
    private const string Clone = "C:/Users/wook1/AppData/Local/Temp/CodexUnityVisualBuild";
    private const string PrefabPath = "Assets/@Project/@sung_wook/@Game/02_Prefabs/Modules/Terrain/HexBackground.prefab";
    private const string ReportPath = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/TilemapLayout/BackgroundCoverage.json";
    private static readonly BoundsInt TargetCells = new BoundsInt(-22, -15, 0, 72, 30, 1);

    [Serializable] private sealed class Sample
    {
        public string label;
        public Vector3Int cell;
        public Vector3 world;
        public bool occupied;
    }
    [Serializable] private sealed class Report
    {
        public string status = "running", startedUtc = DateTime.UtcNow.ToString("O"), finishedUtc, error;
        public string project, prefab = PrefabPath, tileAsset, tileGuid, beforePrefabSha256, afterPrefabSha256;
        public int beforeTiles, afterTiles, expectedTiles = 2160, beforeComponentCount, afterComponentCount;
        public Vector3Int originBefore, sizeBefore, originAfter, sizeAfter;
        public Vector3 gridCellSize, gridCellGap, prefabRootPosition, prefabRootScale, mapPosition, mapScale;
        public string gridLayout, gridSwizzle, beforeStage1Sha256, afterStage1Sha256, beforeStage1SceneFileSha256, afterStage1SceneFileSha256;
        public int beforeStage1Tiles, afterStage1Tiles, beforeColliderCount, afterColliderCount;
        public bool sameTile, white, sameComponents, sameGrid, sameTransforms, sameRenderer, noColliders, stage1Preserved, stage1SceneFileUnchanged;
        public Vector3 occupiedCenterWorldMin, occupiedCenterWorldMax, spriteWorldMin, spriteWorldMax;
        public Vector3 stage5CameraPosition, stage5ViewMin, stage5ViewMax;
        public float stage5OrthographicSize, checkedAspect = 16f / 9f;
        public bool allViewportEdgeCellsOccupied;
        public string stage1Check = "Edit Mode reload of the unchanged Stage01 scene before/after prefab authoring: count, occupied cells and tile asset identities. Subsequent runtime capture must confirm the final 3102-cell Stage01 override.";
        public List<Sample> authoredCorners = new List<Sample>();
        public List<Sample> stage5ViewportProbes = new List<Sample>();
    }

    public static void Apply()
    {
        var report = new Report();
        GameObject root = null;
        try
        {
            report.project = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName).Replace('\\', '/');
            if (!Application.isBatchMode || report.project != Clone || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run authoring only in the isolated CodexUnityVisualBuild batch project outside Play Mode.");
            report.beforePrefabSha256 = HashFile(PrefabPath);
            string stage1 = ElectricStageProgression.StageScenePath(1);
            report.beforeStage1SceneFileSha256 = HashFile(stage1);
            Scene originalScene = EditorSceneManager.OpenScene(stage1, OpenSceneMode.Single);
            Tilemap originalBackground = BackgroundIn(originalScene);
            report.beforeStage1Sha256 = Signature(originalBackground, out report.beforeStage1Tiles);
            if (report.beforeStage1Tiles != 3102) throw new InvalidOperationException("Expected Stage01's saved 3102-cell background override before authoring.");

            root = PrefabUtility.LoadPrefabContents(PrefabPath);
            Grid grid = root.GetComponent<Grid>();
            Tilemap map = root.GetComponentInChildren<Tilemap>(true);
            TilemapRenderer renderer = map != null ? map.GetComponent<TilemapRenderer>() : null;
            if (grid == null || map == null || renderer == null || map.name != "Background")
                throw new InvalidOperationException("Expected the existing HexBackground Grid/Background Tilemap/renderer hierarchy.");
            if (grid.cellLayout != GridLayout.CellLayout.Hexagon) throw new InvalidOperationException("Expected the existing hexagon grid.");
            report.beforeComponentCount = root.GetComponentsInChildren<Component>(true).Length;
            report.beforeColliderCount = root.GetComponentsInChildren<Collider2D>(true).Length;
            if (report.beforeColliderCount != 0) throw new InvalidOperationException("Background must have no colliders.");
            report.originBefore = map.origin; report.sizeBefore = map.size;
            report.gridCellSize = grid.cellSize; report.gridCellGap = grid.cellGap;
            report.gridLayout = grid.cellLayout.ToString(); report.gridSwizzle = grid.cellSwizzle.ToString();
            report.prefabRootPosition = root.transform.localPosition; report.prefabRootScale = root.transform.localScale;
            report.mapPosition = map.transform.localPosition; report.mapScale = map.transform.localScale;
            Vector3 anchor = map.tileAnchor;
            int sortingLayer = renderer.sortingLayerID, sortingOrder = renderer.sortingOrder;
            Material[] materials = renderer.sharedMaterials;
            TileBase tile = null;
            foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
            {
                TileBase candidate = map.GetTile(cell);
                if (candidate == null) continue;
                report.beforeTiles++;
                if (tile == null) tile = candidate;
                else if (tile != candidate) throw new InvalidOperationException("Existing background contains more than one tile asset.");
            }
            if (tile == null) throw new InvalidOperationException("Existing background has no tile asset to preserve.");
            report.tileAsset = AssetDatabase.GetAssetPath(tile);
            report.tileGuid = AssetDatabase.AssetPathToGUID(report.tileAsset);
            if (report.tileGuid != "b5d28352d8973614ab352d685fe07545") throw new InvalidOperationException("Unexpected background tile asset.");
            var tiles = new TileBase[TargetCells.size.x * TargetCells.size.y];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = tile;
            map.ClearAllTiles();
            map.SetTilesBlock(TargetCells, tiles);
            map.color = Color.white;
            map.CompressBounds();
            map.RefreshAllTiles();
            report.afterTiles = Count(map);
            report.originAfter = map.origin; report.sizeAfter = map.size;
            report.afterComponentCount = root.GetComponentsInChildren<Component>(true).Length;
            report.afterColliderCount = root.GetComponentsInChildren<Collider2D>(true).Length;
            report.sameComponents = report.afterComponentCount == report.beforeComponentCount;
            report.sameGrid = grid.cellSize == report.gridCellSize && grid.cellGap == report.gridCellGap
                && grid.cellLayout.ToString() == report.gridLayout && grid.cellSwizzle.ToString() == report.gridSwizzle;
            report.sameTransforms = root.transform.localPosition == report.prefabRootPosition && root.transform.localScale == report.prefabRootScale
                && map.transform.localPosition == report.mapPosition && map.transform.localScale == report.mapScale && map.tileAnchor == anchor;
            report.sameRenderer = renderer.sortingLayerID == sortingLayer && renderer.sortingOrder == sortingOrder && SameMaterials(materials, renderer.sharedMaterials);
            report.noColliders = report.afterColliderCount == 0;
            report.white = map.color == Color.white;
            report.sameTile = true;
            foreach (Vector3Int cell in TargetCells.allPositionsWithin) report.sameTile &= map.GetTile(cell) == tile;
            Bounds centers = new Bounds(map.GetCellCenterWorld(TargetCells.min), Vector3.zero);
            Bounds visuals = default;
            bool firstVisual = true;
            foreach (Vector3Int cell in TargetCells.allPositionsWithin)
            {
                centers.Encapsulate(map.GetCellCenterWorld(cell));
                Sprite sprite = map.GetSprite(cell);
                if (sprite == null) throw new InvalidOperationException("Existing background tile has no sprite.");
                Bounds bounds = sprite.bounds;
                foreach (Vector3 localCorner in new[] {
                    new Vector3(bounds.min.x, bounds.min.y, 0), new Vector3(bounds.max.x, bounds.min.y, 0),
                    new Vector3(bounds.min.x, bounds.max.y, 0), new Vector3(bounds.max.x, bounds.max.y, 0) })
                {
                    Vector3 local = map.GetCellCenterLocal(cell) + map.orientationMatrix.MultiplyPoint3x4(map.GetTransformMatrix(cell).MultiplyPoint3x4(localCorner));
                    Vector3 world = map.transform.TransformPoint(local);
                    if (firstVisual) { visuals = new Bounds(world, Vector3.zero); firstVisual = false; } else visuals.Encapsulate(world);
                }
            }
            report.occupiedCenterWorldMin = centers.min; report.occupiedCenterWorldMax = centers.max;
            report.spriteWorldMin = visuals.min; report.spriteWorldMax = visuals.max;
            foreach (Vector3Int cell in new[] { new Vector3Int(-22, -15, 0), new Vector3Int(49, -15, 0), new Vector3Int(-22, 14, 0), new Vector3Int(49, 14, 0) })
                report.authoredCorners.Add(new Sample { label = "Extreme occupied cell center", cell = cell, world = map.GetCellCenterWorld(cell), occupied = map.HasTile(cell) });
            if (report.afterTiles != report.expectedTiles || report.originAfter != TargetCells.min || report.sizeAfter != TargetCells.size
                || !report.sameTile || !report.white || !report.sameComponents || !report.sameGrid || !report.sameTransforms || !report.sameRenderer || !report.noColliders)
                throw new InvalidOperationException("Background extent or preservation checks failed before save.");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
            if (!saved) throw new IOException("Could not save the existing HexBackground prefab.");
            PrefabUtility.UnloadPrefabContents(root); root = null;
            report.afterPrefabSha256 = HashFile(PrefabPath);

            Scene updatedStage1 = EditorSceneManager.OpenScene(stage1, OpenSceneMode.Single);
            Tilemap updatedStage1Background = BackgroundIn(updatedStage1);
            report.afterStage1Sha256 = Signature(updatedStage1Background, out report.afterStage1Tiles);
            report.afterStage1SceneFileSha256 = HashFile(stage1);
            report.stage1SceneFileUnchanged = report.beforeStage1SceneFileSha256 == report.afterStage1SceneFileSha256;
            report.stage1Preserved = report.afterStage1Tiles == 3102 && report.beforeStage1Sha256 == report.afterStage1Sha256 && updatedStage1Background.color == Color.white;
            if (!report.stage1Preserved || !report.stage1SceneFileUnchanged)
                throw new InvalidOperationException("Stage01's saved background override was not preserved exactly.");

            Scene stage5 = EditorSceneManager.OpenScene(ElectricStageProgression.StageScenePath(5), OpenSceneMode.Single);
            Tilemap stage5Background = BackgroundIn(stage5);
            Camera camera = null;
            foreach (GameObject sceneRoot in stage5.GetRootGameObjects())
                foreach (Camera candidate in sceneRoot.GetComponentsInChildren<Camera>(true))
                    if (candidate.CompareTag("MainCamera")) camera = candidate;
            if (camera == null || !camera.orthographic) throw new InvalidOperationException("Expected Stage05's saved orthographic MainCamera.");
            report.stage5CameraPosition = camera.transform.position;
            report.stage5OrthographicSize = camera.orthographicSize;
            float halfHeight = camera.orthographicSize, halfWidth = halfHeight * report.checkedAspect;
            report.stage5ViewMin = new Vector3(camera.transform.position.x - halfWidth, camera.transform.position.y - halfHeight, 0);
            report.stage5ViewMax = new Vector3(camera.transform.position.x + halfWidth, camera.transform.position.y + halfHeight, 0);
            report.allViewportEdgeCellsOccupied = true;
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                foreach (Vector3 world in new[] {
                    new Vector3(Mathf.Lerp(report.stage5ViewMin.x, report.stage5ViewMax.x, t), report.stage5ViewMin.y, 0),
                    new Vector3(Mathf.Lerp(report.stage5ViewMin.x, report.stage5ViewMax.x, t), report.stage5ViewMax.y, 0),
                    new Vector3(report.stage5ViewMin.x, Mathf.Lerp(report.stage5ViewMin.y, report.stage5ViewMax.y, t), 0),
                    new Vector3(report.stage5ViewMax.x, Mathf.Lerp(report.stage5ViewMin.y, report.stage5ViewMax.y, t), 0) })
                {
                    Vector3Int cell = stage5Background.WorldToCell(world);
                    bool occupied = stage5Background.HasTile(cell);
                    report.stage5ViewportProbes.Add(new Sample { label = "Stage05 16:9 viewport edge", world = world, cell = cell, occupied = occupied });
                    report.allViewportEdgeCellsOccupied &= occupied;
                }
            }
            if (Count(stage5Background) != report.expectedTiles || !report.allViewportEdgeCellsOccupied)
                throw new InvalidOperationException("Extended background does not cover Stage05's current 16:9 camera edges.");
            report.status = "passed";
            Debug.Log("Saved white HexBackground coverage, preserved Stage01 override, and verified Stage05 viewport edges.");
        }
        catch (Exception error) { report.status = "failed"; report.error = error.ToString(); throw; }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }
    }
    private static Tilemap BackgroundIn(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
                if (map.name == "Background" && map.gameObject.activeInHierarchy) return map;
        throw new InvalidOperationException("Active saved Background Tilemap missing in " + scene.path);
    }
    private static int Count(Tilemap map)
    {
        int count = 0;
        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin) if (map.HasTile(cell)) count++;
        return count;
    }
    private static bool SameMaterials(Material[] a, Material[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static string Signature(Tilemap map, out int count)
    {
        count = 0;
        var text = new StringBuilder();
        foreach (Vector3Int cell in map.cellBounds.allPositionsWithin)
        {
            TileBase tile = map.GetTile(cell); if (tile == null) continue;
            count++;
            text.Append(cell.x.ToString(CultureInfo.InvariantCulture)).Append(',').Append(cell.y.ToString(CultureInfo.InvariantCulture)).Append(',').Append(cell.z.ToString(CultureInfo.InvariantCulture)).Append(':');
            text.Append(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tile))).Append('\n');
        }
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
    }
    private static string HashFile(string path)
    {
        using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }
}
