// SkewEffect.cs  (repurposed as prism-like world shift)
using UnityEngine;

[System.Serializable]
public class SkewEffect : IEffectTransform
{
    [Header("Prism-like World Shift")]
    [Tooltip("Root containing all visuals that should be shifted (Board, targets, UI panels).")]
    public Transform worldRoot;

    [Tooltip("Shift amount in metres. Positive shifts visuals to the right.")]
    public float shiftMeters = 0.10f;

    private Vector3? _originalLocalPos;

    // This effect does NOT modify the hand ray/pose; it shifts the seen world.
    public Pose TransformPose(Pose rawPose) => rawPose;
    public Ray TransformRay(Ray rawRay) => rawRay;

    public void ApplyCameraEffect(Camera cam)
    {
        if (worldRoot == null) return;

        if (_originalLocalPos == null)
            _originalLocalPos = worldRoot.localPosition;

        worldRoot.localPosition = _originalLocalPos.Value + new Vector3(shiftMeters, 0f, 0f);
    }

    public void ResetCameraEffect(Camera cam)
    {
        if (worldRoot == null) return;

        if (_originalLocalPos != null)
            worldRoot.localPosition = _originalLocalPos.Value;

        _originalLocalPos = null;
    }
}