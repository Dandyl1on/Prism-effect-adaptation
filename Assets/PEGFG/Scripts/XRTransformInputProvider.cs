// XRTransformInputProvider.cs
using UnityEngine;

public class XRTransformInputProvider : MonoBehaviour, IInputProvider
{
    [Header("Tracked Controller Transform")]
    public Transform controllerTransform;

    [Header("Confirm Input (simple)")]
    public KeyCode fallbackConfirmKey = KeyCode.Space; // Use proper XR input later

    bool _confirmDown;

    void Update()
    {
        // Replace this with SteamVR action / XR input later.
        _confirmDown = Input.GetKeyDown(fallbackConfirmKey);
    }

    public Pose GetPointerPose()
        => new Pose(controllerTransform.position, controllerTransform.rotation);

    public Ray GetPointerRay()
        => new Ray(controllerTransform.position, controllerTransform.forward);

    public bool ConfirmPressedThisFrame()
        => _confirmDown;
}