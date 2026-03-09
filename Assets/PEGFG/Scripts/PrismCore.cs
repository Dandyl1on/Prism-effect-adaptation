using UnityEngine;

public interface IInputProvider
{
    Pose GetPointerPose();
    Ray GetPointerRay();
    bool ConfirmPressedThisFrame();
}

public interface IEffectTransform
{
    Pose TransformPose(Pose rawPose);
    Ray TransformRay(Ray rawRay);
    void ApplyCameraEffect(Camera cam);
    void ResetCameraEffect(Camera cam);
}

public class NoEffect : IEffectTransform
{
    public Pose TransformPose(Pose rawPose) => rawPose;
    public Ray TransformRay(Ray rawRay) => rawRay;
    public void ApplyCameraEffect(Camera cam) { }
    public void ResetCameraEffect(Camera cam) { }
}

[System.Serializable]
public class TranslationEffect : IEffectTransform
{
    public Vector3 offsetWorld = new Vector3(0.12f, 0f, 0f);

    public Pose TransformPose(Pose rawPose)
        => new Pose(rawPose.position + offsetWorld, rawPose.rotation);

    public Ray TransformRay(Ray rawRay)
        => new Ray(rawRay.origin + offsetWorld, rawRay.direction);

    public void ApplyCameraEffect(Camera cam) { }
    public void ResetCameraEffect(Camera cam) { }
}

[System.Serializable]
public class RotationEffect : IEffectTransform
{
    public float rotationDegrees = 20f;
    public Vector3 axisWorld = Vector3.up;

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

[System.Serializable]
public class SkewEffect : IEffectTransform
{
    public enum SkewUnits { Meters, Degrees, PrismDiopters }

    [Header("Prism-like Frustum Skew (Stereo Projection Shear)")]
    public SkewUnits units = SkewUnits.Meters;

    [Tooltip("Meters: lateral shift at reference distance (m).\nDegrees: deviation angle.\nPrismDiopters: Δ.")]
    public float value = 0.10f;

    [Tooltip("Reference distance to target plane (e.g., board) in metres.")]
    public float referenceDistanceMeters = 2.0f;

    private bool _hasOriginal;
    private Matrix4x4 _origMono;
    private Matrix4x4 _origLeft;
    private Matrix4x4 _origRight;

    public Pose TransformPose(Pose rawPose) => rawPose;
    public Ray TransformRay(Ray rawRay) => rawRay;

    public void ApplyCameraEffect(Camera cam)
    {
        if (cam == null) return;

        float shiftMeters = ComputeShiftMeters();
        float d = Mathf.Max(1e-4f, referenceDistanceMeters);
        float shear = shiftMeters / d;

        if (cam.stereoEnabled)
        {
            if (!_hasOriginal)
            {
                _origLeft = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                _origRight = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
                _hasOriginal = true;
            }

            var left = _origLeft;
            var right = _origRight;

            left.m02 += shear;
            right.m02 += shear;

            cam.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, left);
            cam.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, right);
        }
        else
        {
            if (!_hasOriginal)
            {
                _origMono = cam.projectionMatrix;
                _hasOriginal = true;
            }

            var p = _origMono;
            p.m02 += shear;
            cam.projectionMatrix = p;
        }
    }

    public void ResetCameraEffect(Camera cam)
    {
        if (cam == null || !_hasOriginal) return;

        if (cam.stereoEnabled)
        {
            cam.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, _origLeft);
            cam.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, _origRight);
            cam.ResetStereoProjectionMatrices();
        }
        else
        {
            cam.projectionMatrix = _origMono;
            cam.ResetProjectionMatrix();
        }

        _hasOriginal = false;
    }

    float ComputeShiftMeters()
    {
        switch (units)
        {
            case SkewUnits.Meters:
                return value;
            case SkewUnits.Degrees:
                return referenceDistanceMeters * Mathf.Tan(value * Mathf.Deg2Rad);
            case SkewUnits.PrismDiopters:
                return referenceDistanceMeters * (value / 100f);
            default:
                return value;
        }
    }
}