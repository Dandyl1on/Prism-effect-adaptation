using UnityEngine;


public interface ISandboxTask
{
    SandboxRunner.TaskMode TaskMode { get; }
    void SetTaskActive(bool active);
}

public class SandboxRunner : MonoBehaviour
{
    public enum InputMode { XR, MouseDebug }
    public enum EffectMode { None, Translation, Rotation, Skew }
    public enum TaskMode { OpenLoop, LineBisection, Landmark }

    [Header("Mode")]
    public InputMode inputMode = InputMode.MouseDebug;
    public EffectMode effectMode = EffectMode.None;
    public TaskMode taskMode = TaskMode.OpenLoop;

    [Header("References")]
    public Camera mainCam;
    public MonoBehaviour xrInputProvider;       // must implement IInputProvider
    public MonoBehaviour mouseInputProvider;    // must implement IInputProvider

    [Header("Tasks")]
    public MonoBehaviour openLoopTask;
    public MonoBehaviour lineBisectionTask;
    public MonoBehaviour landmarkTask;

    [Header("Effect Params")]
    public TranslationEffect translation = new TranslationEffect();
    public RotationEffect rotation = new RotationEffect();
    public SkewEffect skew = new SkewEffect();

    [Header("Debug")]
    public bool drawDebugRays = true;
    public float debugRayLength = 2.0f;

    IInputProvider _input;
    IEffectTransform _effect;

    InputMode _lastInputMode;
    EffectMode _lastEffectMode;
    TaskMode _lastTaskMode;

    void Start()
    {
        _lastInputMode = inputMode;
        _lastEffectMode = effectMode;
        _lastTaskMode = taskMode;

        SelectInput();
        SelectEffect();
        ApplyTaskMode();
    }

    void OnEnable()
    {
        SelectInput();
        SelectEffect();
        ApplyTaskMode();
    }

    void Update()
    {
        if (_lastInputMode != inputMode)
        {
            _lastInputMode = inputMode;
            SelectInput();
        }

        if (_lastEffectMode != effectMode)
        {
            _lastEffectMode = effectMode;
            SelectEffect();
        }

        if (_lastTaskMode != taskMode)
        {
            _lastTaskMode = taskMode;
            ApplyTaskMode();
        }

        if (_input == null) SelectInput();
        if (_effect == null) SelectEffect();

        if (_input == null || _effect == null || mainCam == null)
            return;

        _effect.ApplyCameraEffect(mainCam);

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
        if (mainCam != null)
            _effect?.ResetCameraEffect(mainCam);
    }

    public (Ray ray, Pose pose, bool confirm) GetTransformedInput()
    {
        if (_input == null) SelectInput();
        if (_effect == null) SelectEffect();

        if (_input == null || _effect == null)
            return (new Ray(Vector3.zero, Vector3.forward), new Pose(Vector3.zero, Quaternion.identity), false);

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

        if (_input == null && mb != null)
            Debug.LogError($"[SandboxRunner] Selected provider does not implement IInputProvider: {mb.name}");
    }

    void SelectEffect()
    {
        if (mainCam != null)
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

    void ApplyTaskMode()
    {
        SetTaskActive(openLoopTask, taskMode == TaskMode.OpenLoop);
        SetTaskActive(lineBisectionTask, taskMode == TaskMode.LineBisection);
        SetTaskActive(landmarkTask, taskMode == TaskMode.Landmark);
    }

    static void SetTaskActive(MonoBehaviour taskMb, bool active)
    {
        if (taskMb == null) return;

        if (taskMb is ISandboxTask task)
            task.SetTaskActive(active);
        else
            Debug.LogWarning($"[SandboxRunner] Task does not implement ISandboxTask: {taskMb.name}");
    }

    public Ray GetRawPointerRayForDebug()
    {
        return _input != null ? _input.GetPointerRay() : new Ray(Vector3.zero, Vector3.forward);
    }

    
}