using UnityEngine;
using UnityEngine.EventSystems;

public class DraggableUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private Transform        canvas;
    private RectTransform    canvasRect;
    private Transform        previousParent;
    private RectTransform    rect;
    private CanvasGroup      canvasGroup;
    private Vector2          dragOffset;

    private void Awake()
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>().rootCanvas;
        canvas = rootCanvas.transform;
        canvasRect = rootCanvas.GetComponent<RectTransform>();

        rect = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        previousParent = transform.parent;

        transform.SetParent(canvas);
        transform.SetAsLastSibling();
        
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
        dragOffset = (Vector2)rect.localPosition - localPoint;

        canvasGroup.alpha = 0.6f;
        canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        rect.localPosition = ClampToCanvas(localPoint + dragOffset);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
    }

    private Vector2 ClampToCanvas(Vector2 pos)
    {
        Vector2 canvasSize = canvasRect.rect.size;
        Vector2 panelSize = rect.rect.size;
        Vector2 pivot = rect.pivot;
        
        float minX = -canvasSize.x * 0.5f + panelSize.x * pivot.x;
        float maxX =  canvasSize.x * 0.5f - panelSize.x * (1f - pivot.x);
        float minY = -canvasSize.y * 0.5f + panelSize.y * pivot.y;
        float maxY =  canvasSize.y * 0.5f - panelSize.y * (1f - pivot.y);

        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        return pos;
    }
}