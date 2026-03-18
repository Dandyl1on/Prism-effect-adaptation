using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Valve.VR;

public interface ISandboxTask
{
SandboxRunner.TaskMode TaskMode { get; }
void SetTaskActive(bool active);
}

public class SandboxRunner : MonoBehaviour
{
public enum EffectMode { None, Translation, Rotation, Skew }
public enum TaskMode { OpenLoop, LineBisection, Landmark, Exposure }
public enum Handedness { Left, Right }

[Header("Mode")]
[SerializeField] private EffectMode effectMode = EffectMode.None;
[Tooltip("Active mode. Exposure is entered via key or automatically.")]
[SerializeField] private TaskMode taskMode = TaskMode.OpenLoop;

[Header("Scene References")]
private Camera mainCam;
private Transform visualWorldRoot;


[Header("XR Input")]
[SerializeField] private Handedness activeHand = Handedness.Right;

[Header("Calibration")]
private Transform rigRoot;
private Transform hmd;
private Transform boardMid;
[SerializeField] private bool calibrateOnStart = false;
private float calibrateOnStartDelaySeconds = 0.25f;
private SteamVR_Action_Boolean _calibrateAction;
private SteamVR_Input_Sources calibrationHand = SteamVR_Input_Sources.RightHand;

[Header("Tasks")]
private MonoBehaviour openLoopTask;
private MonoBehaviour lineBisectionTask;
private MonoBehaviour landmarkTask;
private MonoBehaviour exposureTask;

[Header("Effects")]
[SerializeField] private TranslationEffect translation = new();
[SerializeField] private RotationEffect rotation = new();
[SerializeField] private SkewEffect skew = new();

[Header("Experiment Flow")]
[SerializeField] private KeyCode enterExposureKey = KeyCode.E;
[SerializeField] private KeyCode restartBaselineKey = KeyCode.R;

private bool autoReturnFromExposure = true;
private float exposureReturnDelaySeconds = 1f;

[Header("Debug")]
[SerializeField] private bool drawDebugRays = true;

private LineRenderer _rawRayLine;
private LineRenderer _transformedRayLine;
private Transform _rayDebugRoot;

private IEffectTransform _effect;

private Transform _leftControllerTransform;
private Transform _rightControllerTransform;
private SteamVR_Action_Boolean _confirmAction;
private bool _confirmDown;

public EffectMode CurrentEffectMode => effectMode;
public TaskMode CurrentTaskMode => taskMode;
private EffectMode _lastEffectMode;
private TaskMode _lastTaskMode;

private TaskMode _measurementTaskBeforeExposure = TaskMode.OpenLoop;

private float _pendingExposureReturnTime = -1f;
private bool _waitingForExposureReturn;

void Start()
{
    _lastEffectMode = effectMode;
    _lastTaskMode = taskMode;

    AutoAssignReferences();
    AutoAssignXRInput();
    SelectEffect();
    ApplyTaskMode();

    if (calibrateOnStart)
        StartCoroutine(CalibrateOnStartRoutine());

    if (IsMeasurementTask(taskMode))
        BeginMeasurementBlock(taskMode, "Baseline");
}

IEnumerator CalibrateOnStartRoutine()
{
    // Delay startup calibration until tracking has initialized.
    yield return null;
    yield return new WaitForEndOfFrame();

    if (calibrateOnStartDelaySeconds > 0f)
        yield return new WaitForSeconds(calibrateOnStartDelaySeconds);

    CalibrateHeight();
}

void Update()
{
    HandleKeyboardShortcuts();

    if (_waitingForExposureReturn && Time.time >= _pendingExposureReturnTime)
    {
        _waitingForExposureReturn = false;
        ReturnFromExposureToPost();
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

    AutoAssignXRInput();
    UpdateXRConfirm();
    UpdateCalibrationInput();

    if (_effect == null || mainCam == null)
        return;

    _effect.ApplyCameraEffect(mainCam);

    UpdateDebugLines();
}

void OnDisable()
{
    if (mainCam != null)
        _effect?.ResetCameraEffect(mainCam);

    SetDebugLinesActive(false);
}

void Awake()
{
    AutoAssignReferences();
    AutoAssignXRInput();
}

void Reset()
{
    AutoAssignReferences();
    AutoAssignXRInput();
}

void OnValidate()
{
    if (!Application.isPlaying)
    {
        AutoAssignReferences();
        AutoAssignXRInput();
    }
}

void AutoAssignReferences()
{
    if (mainCam == null)
    {
        var camObj =
            GameObject.Find("Camera") ??
            GameObject.Find("Camera (eye)") ??
            GameObject.Find("Main Camera");

        if (camObj != null)
            mainCam = camObj.GetComponent<Camera>();

        if (mainCam == null)
            mainCam = Camera.main;

        if (mainCam == null)
            mainCam = FindObjectOfType<Camera>();
    }

    if (visualWorldRoot == null)
    {
        var obj = GameObject.Find("WorldRoot");
        if (obj != null)
            visualWorldRoot = obj.transform;
    }

    if (rigRoot == null)
    {
        var rigObj = GameObject.Find("[CameraRig]") ?? GameObject.Find("CameraRig");
        if (rigObj != null)
            rigRoot = rigObj.transform;
    }

    if (hmd == null && mainCam != null)
        hmd = mainCam.transform;

    if (rigRoot == null && hmd != null)
        rigRoot = hmd.root;

    if (boardMid == null)
    {
        var boardObj =
            GameObject.Find("BoardMid") ??
            GameObject.Find("Board Mid") ??
            GameObject.Find("MidPointMarker") ??
            GameObject.Find("MidpointMarker") ??
            GameObject.Find("Midpoint");
        if (boardObj != null)
            boardMid = boardObj.transform;
    }

    if (openLoopTask == null)
        openLoopTask = GetComponent<OpenLoopPointingTask>();

    if (lineBisectionTask == null)
        lineBisectionTask = GetComponent<LineBisectionTask>();

    if (landmarkTask == null)
        landmarkTask = GetComponent<LandmarkTask>();

    if (exposureTask == null)
        exposureTask = GetComponent<ExposureTask>();

}

void AutoAssignXRInput()
{
    if (_leftControllerTransform == null)
    {
        var leftObj = GameObject.Find("Controller (left)");
        if (leftObj != null)
            _leftControllerTransform = leftObj.transform;
    }

    if (_rightControllerTransform == null)
    {
        var rightObj = GameObject.Find("Controller (right)");
        if (rightObj != null)
            _rightControllerTransform = rightObj.transform;
    }

    if (_confirmAction == null)
    {
        // Prefer the generated SteamVR action if it exists
        _confirmAction = SteamVR_Actions.default_Trigger;

        // Fallback to path lookup
        if (_confirmAction == null)
            _confirmAction = SteamVR_Input.GetBooleanAction("/actions/default/in/Trigger");
    }

    if (_calibrateAction == null)
    {
        _calibrateAction = SteamVR_Actions.default_Calibrate;

        if (_calibrateAction == null)
            _calibrateAction = SteamVR_Input.GetBooleanAction("/actions/default/in/Calibrate");
    }
}

Transform GetActiveControllerTransform()
{
    return activeHand == Handedness.Left ? _leftControllerTransform : _rightControllerTransform;
}

Transform GetActivePointerVisual()
{
    Transform controller = GetActiveControllerTransform();
    if (controller == null) return null;

    Transform model = controller.Find("Model");
    return model != null ? model : controller;
}

void UpdateXRConfirm()
{
    bool down = false;

    if (_confirmAction != null)
    {
        var hand = activeHand == Handedness.Left
            ? SteamVR_Input_Sources.LeftHand
            : SteamVR_Input_Sources.RightHand;

        down = _confirmAction.GetStateDown(hand);
    }

    _confirmDown = down;
}

void UpdateCalibrationInput()
{
    if (_calibrateAction == null)
        return;

    if (_calibrateAction.GetStateDown(calibrationHand))
    {
        CalibrateHeight();
    }
}

[ContextMenu("Calibrate Height Now")]
public void CalibrateHeight()
{
    AutoAssignReferences();

    if (!rigRoot || !hmd || !boardMid)
    {
        Debug.LogError("[SandboxRunner] Missing calibration references (rigRoot/hmd/boardMid).");
        return;
    }

    float deltaY = boardMid.position.y - hmd.position.y;

    Vector3 p = rigRoot.position;
    p.y += deltaY;
    rigRoot.position = p;

    Debug.Log($"[SandboxRunner] Applied calibration deltaY={deltaY:0.000}m. HMD is now at board mid height.");
}

void SelectEffect()
{
    if (mainCam != null)
        _effect?.ResetCameraEffect(mainCam);

    skew.visualWorldRoot = visualWorldRoot;
    skew.visualPointerRoot = GetActivePointerVisual();

    _effect = effectMode switch
    {
        EffectMode.None => new NoEffect(),
        EffectMode.Translation => translation,
        EffectMode.Rotation => rotation,
        EffectMode.Skew => skew,
        _ => new NoEffect()
    };
}

void UpdateDebugLines()
{
    if (!drawDebugRays)
    {
        SetDebugLinesActive(false);
        return;
    }

    EnsureDebugLines();

    var raw = GetRawPointerRay();
    var trn = _effect != null ? _effect.TransformRay(raw) : raw;

    SetDebugLine(_rawRayLine, raw.origin, raw.origin + raw.direction * 2f);
    SetDebugLine(_transformedRayLine, trn.origin, trn.origin + trn.direction * 2f);

    SetDebugLinesActive(true);
}

void EnsureDebugLines()
{
    if (_rayDebugRoot == null)
    {
        var existing = GameObject.Find("RayDebug");
        if (existing != null)
        {
            _rayDebugRoot = existing.transform;
        }
        else
        {
            var go = new GameObject("RayDebug");
            _rayDebugRoot = go.transform;
        }
    }

    if (_rawRayLine == null)
        _rawRayLine = GetOrCreateDebugLine("RawRayLine");

    if (_transformedRayLine == null)
        _transformedRayLine = GetOrCreateDebugLine("TransformedRayLine");
}

LineRenderer GetOrCreateDebugLine(string name)
{
    Transform child = _rayDebugRoot.Find(name);
    GameObject go;

    if (child != null)
    {
        go = child.gameObject;
    }
    else
    {
        go = new GameObject(name);
        go.transform.SetParent(_rayDebugRoot, false);
    }

    var lr = go.GetComponent<LineRenderer>();
    if (lr == null)
        lr = go.AddComponent<LineRenderer>();

    lr.positionCount = 2;
    lr.useWorldSpace = true;
    lr.widthMultiplier = 0.01f;
    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    lr.receiveShadows = false;
    lr.alignment = LineAlignment.View;

    // Simple built-in material choice
    if (lr.sharedMaterial == null)
        lr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));

    if (name == "RawRayLine")
    {
        lr.startColor = Color.white;
        lr.endColor = Color.white;
    }
    else
    {
        lr.startColor = Color.yellow;
        lr.endColor = Color.yellow;
    }

    return lr;
}

void SetDebugLine(LineRenderer lr, Vector3 a, Vector3 b)
{
    if (lr == null) return;
    lr.SetPosition(0, a);
    lr.SetPosition(1, b);
}

void SetDebugLinesActive(bool active)
{
    if (_rawRayLine != null)
        _rawRayLine.enabled = active;

    if (_transformedRayLine != null)
        _transformedRayLine.enabled = active;
}

void ApplyTaskMode()
{
    SetTaskActive(openLoopTask, taskMode == TaskMode.OpenLoop);
    SetTaskActive(lineBisectionTask, taskMode == TaskMode.LineBisection);
    SetTaskActive(landmarkTask, taskMode == TaskMode.Landmark);
    SetTaskActive(exposureTask, taskMode == TaskMode.Exposure);
}

static void SetTaskActive(MonoBehaviour taskMb, bool active)
{
    if (taskMb == null) return;

    if (taskMb is ISandboxTask task)
        task.SetTaskActive(active);
    else
        Debug.LogWarning($"[SandboxRunner] Task does not implement ISandboxTask: {taskMb.name}");
}

public (Ray ray, Pose pose, bool confirm) GetTransformedInput()
{
    AutoAssignXRInput();

    if (_effect == null)
        SelectEffect();

    Transform controller = GetActiveControllerTransform();
    if (controller == null || _effect == null)
        return (new Ray(Vector3.zero, Vector3.forward), new Pose(Vector3.zero, Quaternion.identity), false);

    var rawPose = new Pose(controller.position, controller.rotation);
    var rawRay = new Ray(controller.position, controller.forward);
    var confirm = _confirmDown;

    var ray = _effect.TransformRay(rawRay);
    var pose = _effect.TransformPose(rawPose);

    return (ray, pose, confirm);
}

Ray GetRawPointerRay()
{
    AutoAssignXRInput();

    Transform controller = GetActiveControllerTransform();
    if (controller == null)
        return new Ray(Vector3.zero, Vector3.forward);

    return new Ray(controller.position, controller.forward);
}

void HandleKeyboardShortcuts()
{
    if (Input.GetKeyDown(restartBaselineKey))
    {
        RestartBaselineFromAnywhere();
    }

    if (Input.GetKeyDown(enterExposureKey))
    {
        if (taskMode != TaskMode.Exposure && IsMeasurementTask(taskMode))
            BeginExposure();
    }
}

void RestartBaselineFromAnywhere()
{
    _waitingForExposureReturn = false;
    _pendingExposureReturnTime = -1f;

    TaskMode baselineTask = taskMode == TaskMode.Exposure
        ? _measurementTaskBeforeExposure
        : taskMode;

    if (!IsMeasurementTask(baselineTask))
        baselineTask = TaskMode.OpenLoop;

    taskMode = baselineTask;
    ApplyTaskMode();

    BeginMeasurementBlock(taskMode, "Baseline");
}

public void BeginExposure()
{
    if (!IsMeasurementTask(taskMode))
        return;

    _measurementTaskBeforeExposure = taskMode;

    taskMode = TaskMode.Exposure;
    ApplyTaskMode();

    TryInvokeNoArg(exposureTask, "StartExposureBlock");
}

public void NotifyExposureCompleted()
{
    if (!autoReturnFromExposure)
        return;

    _waitingForExposureReturn = true;
    _pendingExposureReturnTime = Time.time + Mathf.Max(0f, exposureReturnDelaySeconds);
}

public void ReturnFromExposureToPost()
{
    _waitingForExposureReturn = false;

    if (!IsMeasurementTask(_measurementTaskBeforeExposure))
        _measurementTaskBeforeExposure = TaskMode.OpenLoop;

    taskMode = _measurementTaskBeforeExposure;
    ApplyTaskMode();

    BeginMeasurementBlock(taskMode, "Post");
}

void BeginMeasurementBlock(TaskMode measurementTask, string blockName)
{
    var targetTask = GetTaskComponent(measurementTask);
    if (targetTask == null) return;

    taskMode = measurementTask;
    ApplyTaskMode();

    TryInvokeNoArg(targetTask, "ClearSummaries");
    TryInvokeBlockMethod(targetTask, "StartNewBlock", blockName);
}

MonoBehaviour GetTaskComponent(TaskMode mode)
{
    return mode switch
    {
        TaskMode.OpenLoop => openLoopTask,
        TaskMode.LineBisection => lineBisectionTask,
        TaskMode.Landmark => landmarkTask,
        TaskMode.Exposure => exposureTask,
        _ => null
    };
}

bool IsMeasurementTask(TaskMode mode)
{
    return mode == TaskMode.OpenLoop ||
           mode == TaskMode.LineBisection ||
           mode == TaskMode.Landmark;
}

static void TryInvokeNoArg(MonoBehaviour target, string methodName)
{
    if (target == null) return;

    var method = target.GetType().GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        Type.EmptyTypes,
        null);

    method?.Invoke(target, null);
}

static void TryInvokeBlockMethod(MonoBehaviour target, string methodName, string enumValueName)
{
    if (target == null) return;

    var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    foreach (var method in methods)
    {
        if (method.Name != methodName) continue;

        var ps = method.GetParameters();
        if (ps.Length != 1) continue;

        var paramType = ps[0].ParameterType;
        if (!paramType.IsEnum) continue;

        try
        {
            object enumValue = Enum.Parse(paramType, enumValueName);
            method.Invoke(target, new[] { enumValue });
            return;
        }
        catch { return; }
    }
}
}
