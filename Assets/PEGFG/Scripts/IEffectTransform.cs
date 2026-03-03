// IEffectTransform.cs
using UnityEngine;

public interface IEffectTransform
{
    // For hand/pose based effects
    Pose TransformPose(Pose rawPose);
    Ray TransformRay(Ray rawRay);

    // For camera/frustum based effects (skew)
    void ApplyCameraEffect(Camera cam);
    void ResetCameraEffect(Camera cam);
}