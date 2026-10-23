using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace sung_wook
{
    /// <summary>
    /// Stage 02의 사각 타일을 기존 스테이지의 지지면에 맞춰 저장한다.
    /// 원본 장치와 이동 장애물은 유지하고, 정적인 발판의 충돌만 Tilemap으로 옮긴다.
    /// </summary>
    public static class ElectricStageTerrainBuilder
    {
        private const string StageMaterialGuid = "a97c105638bdf8b4a8650670310a4cd3";
        private const int DefaultSortingOrder = 12;

        public static void Configure(Scene scene, int stage, TileBase platformTile)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("Terrain configuration requires a loaded scene.", nameof(scene));
            if (stage < 2 || stage > 5)
                throw new ArgumentOutOfRangeException(nameof(stage));
            if (platformTile == null)
                throw new ArgumentNullException(nameof(platformTile));

            if (stage == 2)
            {
                CompressTilemaps(scene);
                return;
            }

            if (stage == 5)
            {
                ApplyPaletteToExistingCourse(scene, platformTile);
                return;
            }

            Transform stageRoot = Find(scene, "Stage " + stage.ToString("00"));
            Transform gameMaps = Find(scene, "GameMaps");
            if (stageRoot == null || gameMaps == null)
                throw new InvalidOperationException("Stage root or GameMaps is missing in " + scene.name + ".");

            string terrainName = "Stage_" + stage.ToString("00") + "_Terrain";
            Transform terrain = gameMaps.Find(terrainName);
            if (terrain == null)
            {
                var created = new GameObject(terrainName, typeof(Grid));
                created.transform.SetParent(gameMaps, false);
                terrain = created.transform;
            }
            terrain.gameObject.SetActive(true);
            // A unit world-space grid lets each shelf retain its authored world bounds.
            terrain.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Vector3 parentScale = gameMaps.lossyScale;
            terrain.localScale = new Vector3(Inverse(parentScale.x), Inverse(parentScale.y), Inverse(parentScale.z));
            Grid grid = terrain.GetComponent<Grid>();
            if (grid == null) grid = terrain.gameObject.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);
            grid.cellGap = Vector3.zero;
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(StageMaterialGuid));
            CreateStarterFloor(terrain, platformTile, material);
            CreateRightBoundary(terrain, platformTile, material);

            Transform mapLayout = stageRoot.Find("Map Layout - Stage " + stage.ToString("00"));
            var sources = new List<BoxCollider2D>();
            if (mapLayout != null)
                sources.AddRange(mapLayout.GetComponentsInChildren<BoxCollider2D>(true));
            Transform goalPlatform = stageRoot.Find("Goal Platform");
            if (goalPlatform != null)
                sources.AddRange(goalPlatform.GetComponentsInChildren<BoxCollider2D>(true));

            foreach (BoxCollider2D source in sources)
            {
                if (!IsStaticTerrain(source)) continue;
                string tilemapName = source.name + " Tilemap";
                // The stored source collider stays disabled after conversion. Its transform
                // and size are still authoritative if the installer is run again.
                if (!source.enabled && terrain.Find(tilemapName) == null) continue;
                Bounds bounds = WorldBounds(source);
                if (bounds.size.x <= 0.0001f || bounds.size.y <= 0.0001f) continue;

                int columns = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x));
                Tilemap map = GetOrCreateTilemap(terrain, tilemapName, source.gameObject.layer, source.gameObject.tag);
                map.transform.SetPositionAndRotation(new Vector3(bounds.min.x, bounds.min.y, source.transform.position.z), Quaternion.identity);
                map.transform.localScale = new Vector3(bounds.size.x / columns, bounds.size.y, 1f);
                map.ClearAllTiles();
                for (int x = 0; x < columns; x++) SetSolidTile(map, new Vector3Int(x, 0, 0), platformTile);

                SpriteRenderer originalRenderer = source.GetComponent<SpriteRenderer>();
                ConfigureRenderer(map, originalRenderer != null ? originalRenderer.sharedMaterial : material,
                    originalRenderer != null ? originalRenderer.sortingLayerID : 0,
                    originalRenderer != null ? originalRenderer.sortingOrder : DefaultSortingOrder);
                TilemapCollider2D tileCollider = map.GetComponent<TilemapCollider2D>();
                tileCollider.sharedMaterial = source.sharedMaterial;
                tileCollider.enabled = true;
                map.CompressBounds();
                tileCollider.ProcessTilemapChanges();

                source.enabled = false;
                if (originalRenderer != null) originalRenderer.enabled = false;
                EditorUtility.SetDirty(source);
                if (originalRenderer != null) EditorUtility.SetDirty(originalRenderer);
            }

            // Stage 04's temporary one-block support is replaced by the shared start floor.
            if (stage == 4)
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name.StartsWith("Ground Temp", StringComparison.Ordinal))
                    {
                        foreach (BoxCollider2D collider in root.GetComponents<BoxCollider2D>()) collider.enabled = false;
                        foreach (SpriteRenderer renderer in root.GetComponents<SpriteRenderer>()) renderer.enabled = false;
                    }

            Physics2D.SyncTransforms();
            CompressTilemaps(scene);
        }

        private static bool IsStaticTerrain(BoxCollider2D source)
        {
            if (source == null || source.isTrigger || !source.gameObject.activeInHierarchy) return false;
            if (source.GetComponentInParent<ElectricObstacleMotion2D>() != null) return false;
            Rigidbody2D body = source.attachedRigidbody;
            if (body != null && body.bodyType != RigidbodyType2D.Static) return false;
            // Do not convert experimental equipment if it is later placed inside Map Layout.
            return source.GetComponentInParent<ContactCharger2D>() == null
                && source.GetComponentInParent<UniformField2D>() == null
                && source.GetComponentInParent<ElectricGoal2D>() == null
                && source.GetComponentInParent<ElectricStar2D>() == null;
        }

        private static void CreateStarterFloor(Transform terrain, TileBase tile, Material material)
        {
            Tilemap map = GetOrCreateTilemap(terrain, "Starting Floor Tilemap", 0, "Ground");
            map.transform.localPosition = Vector3.zero;
            map.transform.localRotation = Quaternion.identity;
            map.transform.localScale = Vector3.one;
            map.ClearAllTiles();
            // The field shaft in Stage 04 remains empty above y = -4.
            for (int x = -13; x <= -1; x++) SetSolidTile(map, new Vector3Int(x, -5, 0), tile);
            ConfigureRenderer(map, material, 0, DefaultSortingOrder);
            map.CompressBounds();
            map.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
        }

        private static void CreateRightBoundary(Transform terrain, TileBase tile, Material material)
        {
            Tilemap map = GetOrCreateTilemap(terrain, "Right Boundary Tilemap", 0, "Ground");
            map.transform.localPosition = Vector3.zero;
            map.transform.localRotation = Quaternion.identity;
            map.transform.localScale = Vector3.one;
            map.ClearAllTiles();
            // Like Stage 02, the boundary starts at x = 10; both exits stay to its left.
            for (int y = -5; y <= 2; y++) SetSolidTile(map, new Vector3Int(10, y, 0), tile);
            ConfigureRenderer(map, material, 0, DefaultSortingOrder);
            map.CompressBounds();
            map.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
        }

        private static Tilemap GetOrCreateTilemap(Transform terrain, string name, int layer, string tag)
        {
            Transform child = terrain.Find(name);
            GameObject obj = child != null ? child.gameObject
                : new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer), typeof(TilemapCollider2D));
            if (child == null) obj.transform.SetParent(terrain, false);
            obj.SetActive(true);
            obj.layer = layer;
            obj.tag = tag;
            Tilemap map = obj.GetComponent<Tilemap>();
            if (map == null) map = obj.AddComponent<Tilemap>();
            if (obj.GetComponent<TilemapRenderer>() == null) obj.AddComponent<TilemapRenderer>();
            if (obj.GetComponent<TilemapCollider2D>() == null) obj.AddComponent<TilemapCollider2D>();
            map.tileAnchor = new Vector3(0.5f, 0.5f, 0f);
            map.color = Color.white;
            return map;
        }

        private static void SetSolidTile(Tilemap map, Vector3Int position, TileBase tile)
        {
            map.SetTile(position, tile);
            // Use the complete cell, even if an imported sprite has no physics outline.
            map.SetColliderType(position, Tile.ColliderType.Grid);
        }

        private static void ConfigureRenderer(Tilemap map, Material material, int layer, int order)
        {
            TilemapRenderer renderer = map.GetComponent<TilemapRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.sortingLayerID = layer;
            renderer.sortingOrder = order;
        }

        private static void ApplyPaletteToExistingCourse(Scene scene, TileBase tile)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
                {
                    if (!map.gameObject.activeInHierarchy || map.name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        map.CompressBounds();
                        continue;
                    }
                    // Preserve Stage 05's authored cell positions and its grid transform.
                    BoundsInt bounds = map.cellBounds;
                    foreach (Vector3Int position in bounds.allPositionsWithin)
                        if (map.HasTile(position)) SetSolidTile(map, position, tile);
                    map.CompressBounds();
                    TilemapCollider2D collider = map.GetComponent<TilemapCollider2D>();
                    if (collider != null) collider.ProcessTilemapChanges();
                    EditorUtility.SetDirty(map);
                }
        }

        private static Bounds WorldBounds(BoxCollider2D source)
        {
            Vector2 center = source.offset;
            Vector2 half = source.size * 0.5f;
            Vector3 first = source.transform.TransformPoint(center - half);
            Bounds result = new Bounds(first, Vector3.zero);
            result.Encapsulate(source.transform.TransformPoint(center + new Vector2(half.x, -half.y)));
            result.Encapsulate(source.transform.TransformPoint(center + new Vector2(-half.x, half.y)));
            result.Encapsulate(source.transform.TransformPoint(center + half));
            return result;
        }

        private static Transform Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == name) return child;
            return null;
        }

        private static void CompressTilemaps(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
                {
                    map.CompressBounds();
                    EditorUtility.SetDirty(map);
                }
        }

        private static float Inverse(float value) => Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }
}
