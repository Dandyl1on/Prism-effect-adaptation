// NoEffect.cs
using UnityEngine;

public class NoEffect : IEffectTransform
{
    public Pose TransformPose(Pose rawPose) => rawPose;
    public Ray TransformRay(Ray rawRay) => rawRay;
    public void ApplyCameraEffect(Camera cam) { }
    public void ResetCameraEffect(Camera cam) { }
}