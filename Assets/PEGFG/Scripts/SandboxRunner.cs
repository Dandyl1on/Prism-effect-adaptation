// SandboxRunner.cs
using UnityEngine;

public class SandboxRunner : MonoBehaviour
{
    public enum InputMode { XR, MouseDebug }
    public enum EffectMode { None, Translation, Rotation, Skew }
    public enum TaskMode { OpenLoop } // add LineBisection/Landmark later

    [Header("Mode")]
    public InputMode inputMode = InputMode.MouseDebug;
    public EffectMode effectMode = EffectMode.None;
    public TaskMode taskMode = TaskMode.OpenLoop;

    [Header("References")]
    public Camera mainCam;
    public MonoBehaviour xrInputProvider;       // must implement IInputProvider
    public MonoBehaviour mouseInputProvider;    // must implement IInputProvider

    [Header("Effect Params")]
    public TranslationEffect translation = new TranslationEffect();
    public RotationEffect rotation = new RotationEffect();
    public SkewEffect skew = new SkewEffect();

    [Header("Debug")]
    public bool drawDebugRays = true;
    public float debugRayLength = 2.0f;

    IInputProvider _input;
    IEffectTransform _effect;

    void OnEnable()
    {
        SelectInput();
        SelectEffect();
    }

    void Update()
    {
        if (_input == null) SelectInput();
        if (_effect == null) SelectEffect();

        // Apply camera-level effects (skew)
        _effect.ApplyCameraEffect(mainCam);

        // Debug rays: raw vs transformed
        if (drawDebugRays)
        {
            var raw = _input.GetPointerRay();
            var trn = _effect.TransformRay(raw);

            Debug.DrawRay(raw.origin, raw.direction * debugRayLength, Color.white);
            Debug.DrawRay(trn.origin, trn.direction * debugRayLength, Color.yellow);
        }
    }

    void OnDisable()
    {
        _effect?.ResetCameraEffect(mainCam);
    }

    public (Ray ray, Pose pose, bool confirm) GetTransformedInput()
    {
        var rawRay = _input.GetPointerRay();
        var rawPose = _input.GetPointerPose();
        var confirm = _input.ConfirmPressedThisFrame();

        var ray = _effect.TransformRay(rawRay);
        var pose = _effect.TransformPose(rawPose);
        return (ray, pose, confirm);
    }

    void SelectInput()
    {
        _input = null;
        var mb = (inputMode == InputMode.XR) ? xrInputProvider : mouseInputProvider;
        _input = mb as IInputProvider;
    }

    void SelectEffect()
    {
        // Reset any previous camera override
        _effect?.ResetCameraEffect(mainCam);

        _effect = effectMode switch
        {
            EffectMode.None => new NoEffect(),
            EffectMode.Translation => translation,
            EffectMode.Rotation => rotation,
            EffectMode.Skew => skew,
            _ => new NoEffect()
        };
    }

        public Ray GetRawPointerRayForDebug()
    {
        return _input != null ? _input.GetPointerRay() : new Ray(Vector3.zero, Vector3.forward);
    }
}