// RotationEffect.cs
using UnityEngine;

[System.Serializable]
public class RotationEffect : IEffectTransform
{
    public float rotationDegrees = 20f;
    public Vector3 axisWorld = Vector3.up; // yaw by default

    public Pose TransformPose(Pose rawPose)
    {
        var q = Quaternion.AngleAxis(rotationDegrees, axisWorld.normalized);
        return new Pose(rawPose.position, q * rawPose.rotation);
    }

    public Ray TransformRay(Ray rawRay)
    {
        var q = Quaternion.AngleAxis(rotationDegrees, axisWorld.normalized);
        return new Ray(rawRay.origin, q * rawRay.direction);
    }

    public void ApplyCameraEffect(Camera cam) { }
    public void ResetCameraEffect(Camera cam) { }
}