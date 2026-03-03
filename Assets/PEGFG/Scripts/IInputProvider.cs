// IInputProvider.cs
using UnityEngine;

public interface IInputProvider
{
    Pose GetPointerPose();          // position + rotation
    Ray GetPointerRay();            // origin + direction
    bool ConfirmPressedThisFrame(); // trigger/click
}