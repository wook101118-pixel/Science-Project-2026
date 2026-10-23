// Temporary editor authoring tool for the isolated validation copy only.
// Removes existing tiles and saves the existing prefab, including regenerated collision paths.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class Stage5FiftyVoltLayout
{
    private const string TerrainPath = "Assets/@Project/@sung_wook/@Game/02_Prefabs/Modules/Terrain/Stage05_Terrain.prefab";
    private const string Output = "C:/Users/wook1/GitBub-Unity/Science-Project-2026/Logs/Stage5FiftyVoltLayout.json";
    [Serializable] private sealed class Report
    {
        public string status, prefab, tileAsset, spriteAsset;
        public int beforeTiles, afterTiles, removedTiles, addedTiles, colliderPaths;
        public float leftTop = 2f, rightTop = 3f, gap = 1f;
        public List<Vector3Int> removed = new List<Vector3Int>();
        public bool sameTileAsset, sameComponents, collisionRegenerated;
    }

    public static void Apply()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/Temp/CodexUnityVisualBuild/"))
            throw new InvalidOperationException("Only author inside the isolated validation copy.");
        GameObject root = PrefabUtility.LoadPrefabContents(TerrainPath);
        try
        {
            Tilemap map = root.GetComponentInChildren<Tilemap>(true);
            TilemapCollider2D tiles = map.GetComponent<TilemapCollider2D>();
            CompositeCollider2D composite = map.GetComponent<CompositeCollider2D>();
            if (tiles == null || composite == null) throw new InvalidOperationException("Existing terrain colliders missing.");
            int components = root.GetComponentsInChildren<Component>(true).Length;
            TileBase tile = map.GetTile(new Vector3Int(-2, 1, 0));
            var report = new Report { prefab = TerrainPath, beforeTiles = Count(map), tileAsset = AssetDatabase.GetAssetPath(tile) };
            if (report.beforeTiles != 89) throw new InvalidOperationException("Expected the current 89-tile Stage05 terrain.");
            for (int x = -2; x <= 1; x++)
                for (int y = 2; y <= 3; y++) Remove(map, new Vector3Int(x, y, 0), report);
            for (int x = 4; x <= 6; x++) Remove(map, new Vector3Int(x, 3, 0), report);
            for (int y = 1; y <= 3; y++) Remove(map, new Vector3Int(2, y, 0), report);
            Remove(map, new Vector3Int(3, 3, 0), report);
            map.CompressBounds();
            tiles.ProcessTilemapChanges();
            Physics2D.SyncTransforms();
            composite.GenerateGeometry();
            report.afterTiles = Count(map);
            report.removedTiles = report.removed.Count;
            report.sameComponents = root.GetComponentsInChildren<Component>(true).Length == components;
            report.sameTileAsset = true;
            foreach (Vector3Int position in map.cellBounds.allPositionsWithin)
                if (map.HasTile(position)) report.sameTileAsset &= map.GetTile(position) == tile;
            if (tile is Tile concrete) report.spriteAsset = AssetDatabase.GetAssetPath(concrete.sprite);
            report.colliderPaths = composite.pathCount;
            report.collisionRegenerated = !tiles.hasTilemapChanges && composite.pathCount > 0;
            if (report.afterTiles != 74 || !report.sameComponents || !report.sameTileAsset || !report.collisionRegenerated)
                throw new InvalidOperationException("Terrain validation did not match the 74-tile layout.");
            PrefabUtility.SaveAsPrefabAsset(root, TerrainPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Could not save the existing terrain prefab.");
            report.status = "passed";
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, JsonUtility.ToJson(report, true));
            Debug.Log("Stage05 50V layout saved: " + JsonUtility.ToJson(report));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static int Count(Tilemap map)
    {
        int count = 0;
        foreach (Vector3Int position in map.cellBounds.allPositionsWithin) if (map.HasTile(position)) count++;
        return count;
    }

    private static void Remove(Tilemap map, Vector3Int cell, Report report)
    {
        if (!map.HasTile(cell)) throw new InvalidOperationException("Expected an existing tile at " + cell);
        map.SetTile(cell, null);
        report.removed.Add(cell);
    }
}
