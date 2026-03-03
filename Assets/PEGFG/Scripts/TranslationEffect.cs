// TranslationEffect.cs
using UnityEngine;

[System.Serializable]
public class TranslationEffect : IEffectTransform
{
    public Vector3 offsetWorld = new Vector3(0.12f, 0f, 0f); // 12 cm right by default

    public Pose TransformPose(Pose rawPose)
        => new Pose(rawPose.position + offsetWorld, rawPose.rotation);

    public Ray TransformRay(Ray rawRay)
        => new Ray(rawRay.origin + offsetWorld, rawRay.direction);

    public void ApplyCameraEffect(Camera cam) { }
    public void ResetCameraEffect(Camera cam) { }
}