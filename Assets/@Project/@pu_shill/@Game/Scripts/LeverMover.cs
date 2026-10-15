using UnityEngine;
using UnityEngine.InputSystem;

public class LeverMover : MonoBehaviour
{
    [SerializeField] Transform pivot;   
    [SerializeField] Collider2D grabArea;    
    [SerializeField] float maxAngle = 90f; 

    public float Angle { get; private set; }
    public float Value { get; private set; }
    public float MaxAngle => maxAngle;

    Camera cam;
    bool dragging;

    void Awake() => cam = Camera.main;

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());

        if (mouse.leftButton.wasPressedThisFrame && grabArea.OverlapPoint(world))
            dragging = true;
        if (mouse.leftButton.wasReleasedThisFrame)
            dragging = false;

        if (dragging)
        {
            Vector2 dir = world - (Vector2)pivot.position;
            float angle = Mathf.Atan2(-dir.x, dir.y) * Mathf.Rad2Deg;
            angle = Mathf.Clamp(angle, -maxAngle, maxAngle);
            pivot.localRotation = Quaternion.Euler(0, 0, angle);

            Angle = angle;
            Value = angle / maxAngle;
        }
    }
}