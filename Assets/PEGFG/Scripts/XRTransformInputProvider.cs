// XRTransformInputProvider.cs
using UnityEngine;
using Valve.VR;

public class XRTransformInputProvider : MonoBehaviour, IInputProvider
{
    [Header("Tracked Controller Transform")]
    public Transform controllerTransform; // assign right controller (ray origin)

    [Header("Confirm Input (SteamVR)")]
    [Tooltip("Bind this to your boolean action, e.g. SteamVR_Actions.default_Trigger")]
    public SteamVR_Action_Boolean confirmAction;

    [Tooltip("If true, either hand can confirm (mirrored binding).")]
    public bool mirroredHands = true;

    [Tooltip("If mirroredHands is false, only this hand will be checked.")]
    public SteamVR_Input_Sources singleHand = SteamVR_Input_Sources.RightHand;

    [Header("Fallback Confirm (Keyboard)")]
    public bool enableKeyboardFallback = true;
    public KeyCode fallbackConfirmKey = KeyCode.Space;

    bool _confirmDown;

    void Update()
    {
        // Compute confirm DOWN this frame.
        bool down = false;

        if (confirmAction != null)
        {
            if (mirroredHands)
            {
                down |= confirmAction.GetStateDown(SteamVR_Input_Sources.LeftHand);
                down |= confirmAction.GetStateDown(SteamVR_Input_Sources.RightHand);
            }
            else
            {
                down |= confirmAction.GetStateDown(singleHand);
            }
        }

        if (enableKeyboardFallback)
            down |= Input.GetKeyDown(fallbackConfirmKey);

        _confirmDown = down;
    }

    public Pose GetPointerPose()
    {
        if (controllerTransform == null)
            return new Pose(Vector3.zero, Quaternion.identity);

        return new Pose(controllerTransform.position, controllerTransform.rotation);
    }

    public Ray GetPointerRay()
    {
        if (controllerTransform == null)
            return new Ray(Vector3.zero, Vector3.forward);

        return new Ray(controllerTransform.position, controllerTransform.forward);
    }

    public bool ConfirmPressedThisFrame() => _confirmDown;
}