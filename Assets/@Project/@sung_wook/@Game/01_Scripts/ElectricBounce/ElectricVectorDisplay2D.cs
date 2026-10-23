using UnityEngine;

namespace sung_wook
{
                 
                                                   
                                                      
                                               
                  
    [DisallowMultipleComponent]
    public sealed class ElectricVectorDisplay2D : MonoBehaviour
    {
        [SerializeField] private ElectricStageSelector selector;
        [SerializeField] private Sprite segmentSprite;
        [SerializeField] private Material spriteMaterial;
        [SerializeField] private Camera displayCamera;
        [SerializeField] private Color fieldColor = new Color(0.72f, 0.28f, 1f);
        [SerializeField] private Color forceColor = new Color(1f, 0.55f, 0.05f);
        [SerializeField, Min(0.1f)] private float arrowLength = 1.2f;
        [SerializeField, Min(0.02f)] private float lineWidth = 0.07f;
        [SerializeField, Min(0.05f)] private float separation = 0.4f;

        private Transform graphicsRoot;
        private SpriteRenderer[] fieldSegments;
        private SpriteRenderer[] forceSegments;
        private GameObject sampledRoot;
        private Vector3 fieldLabelPosition;
        private Vector3 forceLabelPosition;
        private GUIStyle labelStyle;

        public Vector2 DisplayedField { get; private set; }
        public Vector2 DisplayedForce { get; private set; }
        public bool IsFieldVisible { get; private set; }
        public bool IsForceVisible { get; private set; }
        public bool IsForcePreview { get; private set; }

                                                                       
        public void Configure(ElectricStageSelector stageSelector)
        {
            Configure(stageSelector, segmentSprite, spriteMaterial);
        }

        public void Configure(ElectricStageSelector stageSelector, Sprite squareSprite,
            Material sharedSpriteMaterial)
        {
            selector = stageSelector;
            segmentSprite = squareSprite;
            spriteMaterial = sharedSpriteMaterial;
            sampledRoot = null;
            DestroyGraphics();
        }

        private void LateUpdate() => RefreshDisplay();

                                                                            
        public void RefreshDisplay()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || selector == null
                || selector.Ball == null || !selector.Ball.CanReceiveForce
                || !TryGetSample(out ElectricForceSample sample) || !sample.IsInRange)
            {
                HideArrows();
                return;
            }

            if (!EnsureGraphics())
            {
                HideArrows();
                return;
            }

            DisplayedField = sample.Field;
            DisplayedForce = sample.Force;
            IsForcePreview = selector.SelectedStage == 3;
            IsFieldVisible = IsNonzeroFinite(DisplayedField);
            IsForceVisible = Mathf.Abs(sample.SignedCharge) > 0.0001f
                && IsNonzeroFinite(DisplayedForce);

                                                              
            Vector2 fieldDirection = IsFieldVisible ? DisplayedField.normalized : Vector2.up;
            Vector2 perpendicular = new Vector2(-fieldDirection.y, fieldDirection.x);
            Vector3 center = new Vector3(sample.Position.x, sample.Position.y,
                selector.Ball.transform.position.z - 0.05f);
            DrawArrow(fieldSegments, center + (Vector3)(perpendicular * separation),
                DisplayedField, fieldColor, IsFieldVisible, out fieldLabelPosition);
            DrawArrow(forceSegments, center - (Vector3)(perpendicular * separation),
                DisplayedForce, forceColor, IsForceVisible, out forceLabelPosition);
        }

        private bool TryGetSample(out ElectricForceSample sample)
        {
            sampledRoot = selector.CurrentStageRoot;
                                                       
            return selector.TryGetCurrentForceSample(out sample);
        }

                                                                      
        private bool EnsureGraphics()
        {
            if (graphicsRoot != null) return true;
                                                               
            if (segmentSprite == null && sampledRoot != null)
            {
                foreach (SpriteRenderer renderer in sampledRoot.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.sprite == null) continue;
                    segmentSprite = renderer.sprite;
                    if (spriteMaterial == null) spriteMaterial = renderer.sharedMaterial;
                    break;
                }
            }
            if (segmentSprite == null) return false;

            GameObject root = new GameObject("Electric Vector Graphics");
            root.hideFlags = HideFlags.DontSave;
            root.layer = gameObject.layer;
            graphicsRoot = root.transform;
            graphicsRoot.SetParent(transform, false);
            graphicsRoot.rotation = Quaternion.identity;
            Vector3 parentScale = transform.lossyScale;
            graphicsRoot.localScale = new Vector3(SafeInverse(parentScale.x),
                SafeInverse(parentScale.y), SafeInverse(parentScale.z));
            fieldSegments = CreateSegments("E", fieldColor);
            forceSegments = CreateSegments("F", forceColor);
            return true;
        }

                                                          
        private SpriteRenderer[] CreateSegments(string prefix, Color color)
        {
            var segments = new SpriteRenderer[3];
            for (int i = 0; i < segments.Length; i++)
            {
                GameObject part = new GameObject(prefix + " Segment " + i);
                part.hideFlags = HideFlags.DontSave;
                part.layer = gameObject.layer;
                part.transform.SetParent(graphicsRoot, false);
                SpriteRenderer renderer = part.AddComponent<SpriteRenderer>();
                renderer.sprite = segmentSprite;
                if (spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;
                renderer.color = color;
                renderer.sortingOrder = 2000;
                renderer.enabled = false;
                segments[i] = renderer;
            }
            return segments;
        }

                                                                              
        private void DrawArrow(SpriteRenderer[] segments, Vector3 origin, Vector2 vector,
            Color color, bool visible, out Vector3 labelPosition)
        {
            labelPosition = origin;
            SetVisible(segments, visible);
            if (!visible) return;
            Vector3 direction = vector.normalized;
            Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0f);
            Vector3 start = origin + direction * 0.35f;
            Vector3 end = start + direction * arrowLength;
            float headLength = Mathf.Min(0.3f, arrowLength * 0.35f);
            float headWidth = headLength * 0.65f;
            DrawSegment(segments[0], start, end, color);
            DrawSegment(segments[1], end,
                end - direction * headLength + perpendicular * headWidth, color);
            DrawSegment(segments[2], end,
                end - direction * headLength - perpendicular * headWidth, color);
            labelPosition = end + direction * 0.28f;
        }

                                                                         
        private void DrawSegment(SpriteRenderer renderer, Vector3 start, Vector3 end, Color color)
        {
            Vector3 offset = end - start;
            renderer.transform.position = (start + end) * 0.5f;
            renderer.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg);
            Vector3 spriteSize = segmentSprite.bounds.size;
            renderer.transform.localScale = new Vector3(
                offset.magnitude / Mathf.Max(0.001f, spriteSize.x),
                lineWidth / Mathf.Max(0.001f, spriteSize.y), 1f);
            renderer.color = color;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || (!IsFieldVisible && !IsForceVisible)) return;
            Camera camera = displayCamera != null ? displayCamera : Camera.main;
            if (camera == null) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 16
                };
            }
            if (IsFieldVisible) DrawLabel(camera, fieldLabelPosition, "E", fieldColor);
            if (IsForceVisible) DrawLabel(camera, forceLabelPosition,
                IsForcePreview ? "F*" : "F", forceColor);
        }

        private void DrawLabel(Camera camera, Vector3 position, string text, Color color)
        {
            Vector3 screen = camera.WorldToScreenPoint(position);
            if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width
                || screen.y < 0f || screen.y > Screen.height) return;
            labelStyle.normal.textColor = color;
            GUI.Label(new Rect(screen.x - 18f, Screen.height - screen.y - 14f, 36f, 28f),
                text, labelStyle);
        }

        private static bool IsNonzeroFinite(Vector2 vector)
        {
            return !float.IsNaN(vector.x) && !float.IsNaN(vector.y)
                && !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y)
                && vector.sqrMagnitude > 0.000001f;
        }

        private static float SafeInverse(float value) => Mathf.Abs(value) < 0.0001f ? 1f : 1f / value;

        private static void SetVisible(SpriteRenderer[] segments, bool value)
        {
            if (segments == null) return;
            foreach (SpriteRenderer segment in segments)
                if (segment != null) segment.enabled = value;
        }

        private void HideArrows()
        {
            DisplayedField = Vector2.zero;
            DisplayedForce = Vector2.zero;
            IsFieldVisible = false;
            IsForceVisible = false;
            IsForcePreview = false;
            SetVisible(fieldSegments, false);
            SetVisible(forceSegments, false);
        }

        private void OnDisable() => HideArrows();

        private void OnDestroy() => DestroyGraphics();

                                                                          
        private void DestroyGraphics()
        {
            HideArrows();
            if (graphicsRoot != null)
            {
                if (Application.isPlaying) Destroy(graphicsRoot.gameObject);
                else DestroyImmediate(graphicsRoot.gameObject);
            }
            graphicsRoot = null;
            fieldSegments = null;
            forceSegments = null;
        }
    }
}
