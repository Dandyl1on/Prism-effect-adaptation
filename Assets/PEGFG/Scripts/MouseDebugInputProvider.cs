// MouseDebugInputProvider.cs
using UnityEngine;

public class MouseDebugInputProvider : MonoBehaviour, IInputProvider
{
    [Header("Debug Hand")]
    public Transform debugHand;                 // Move this with WASD, rotate with mouse
    public float moveSpeed = 1.5f;
    public float rotateSpeed = 120f;

    [Header("Buttons")]
    public int confirmMouseButton = 0;          // Left click

    bool _confirmDown;

    void Update()
    {
        // Movement (WASD + QE up/down)
        float x = Input.GetAxisRaw("Horizontal"); // A/D
        float z = Input.GetAxisRaw("Vertical");   // W/S
        float y = 0f;
        if (Input.GetKey(KeyCode.E)) y += 1f;
        if (Input.GetKey(KeyCode.Q)) y -= 1f;

        Vector3 localMove = new Vector3(x, y, z).normalized * moveSpeed * Time.deltaTime;
        debugHand.Translate(localMove, Space.Self);

        // Rotation (hold right mouse to rotate)
        if (Input.GetMouseButton(1))
        {
            float yaw = Input.GetAxis("Mouse X") * rotateSpeed * Time.deltaTime;
            float pitch = -Input.GetAxis("Mouse Y") * rotateSpeed * Time.deltaTime;
            debugHand.Rotate(Vector3.up, yaw, Space.World);
            debugHand.Rotate(Vector3.right, pitch, Space.Self);
        }

        _confirmDown = Input.GetMouseButtonDown(confirmMouseButton);
    }

    public Pose GetPointerPose()
        => new Pose(debugHand.position, debugHand.rotation);

    public Ray GetPointerRay()
        => new Ray(debugHand.position, debugHand.forward);

    public bool ConfirmPressedThisFrame()
        => _confirmDown;
}