using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif
using XRHands = UnityEngine.XR.Hands;
using XRManagement = UnityEngine.XR.Management;
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
public enum XRBackend { SteamVR, OpenXR }
public enum OpenXRTrackingMode { Controllers, Hands }

[Header("Mode")]
[SerializeField] private EffectMode effectMode = EffectMode.None;
[Tooltip("Active mode. Exposure is entered via key or automatically.")]
[SerializeField] private TaskMode taskMode = TaskMode.OpenLoop;

[Header("Scene References")]
 [SerializeField] private Camera mainCam;
 [SerializeField] private Transform visualWorldRoot;
 [SerializeField] private TextMeshProUGUI statReadout;


[Header("XR Input")]
[SerializeField] private XRBackend xrBackend = XRBackend.OpenXR;
[SerializeField] private OpenXRTrackingMode openXRTrackingMode = OpenXRTrackingMode.Hands;
[SerializeField] private Handedness activeHand = Handedness.Right;
[SerializeField] private float handDwellSeconds = 2f;
[SerializeField, Tooltip("Lower values are more responsive. 0 disables smoothing.")]
private float handRaySmoothingSeconds = 0.06f;
[SerializeField, HideInInspector] private GameObject openXRLeftHandTrackingPrefab;
[SerializeField, HideInInspector] private GameObject openXRRightHandTrackingPrefab;

[Header("Calibration")]
[SerializeField] private Transform rigRoot;
[SerializeField] private Transform hmd;
[SerializeField] private Transform boardMid;
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
private Transform _leftControllerRayOriginTransform;
private Transform _rightControllerRayOriginTransform;
private Transform _leftPointerVisualTransform;
private Transform _rightPointerVisualTransform;
private Transform _leftHandVisualTransform;
private Transform _rightHandVisualTransform;
private GameObject _openXRHandVisualizerRoot;
private SteamVR_Action_Boolean _confirmAction;
private bool _confirmDown;
private bool _confirmPressedLastFrame;
private bool _calibrateDownLastFrame;
private bool _handPointingActive;
private float _handPointingStartTime = -1f;
private float _handDwellProgress01;
private bool _handDwellTriggered;
private bool _smoothedHandRayInitialized;
private Handedness _smoothedHandRayHand;
private Vector3 _smoothedHandRayOrigin;
private Vector3 _smoothedHandRayDirection = Vector3.forward;

public EffectMode CurrentEffectMode => effectMode;
public TaskMode CurrentTaskMode => taskMode;
public bool IsHandPointing => _handPointingActive;
public float HandDwellProgress01 => _handDwellProgress01;
private EffectMode _lastEffectMode;
private TaskMode _lastTaskMode;
private Handedness _lastActiveHand;
private XRBackend _lastXRBackend;
private OpenXRTrackingMode _lastOpenXRTrackingMode;

private TaskMode _measurementTaskBeforeExposure = TaskMode.OpenLoop;

private float _pendingExposureReturnTime = -1f;
private bool _waitingForExposureReturn;
private XRHands.XRHandSubsystem _xrHandSubsystem;

void Start()
{
    _lastEffectMode = effectMode;
    _lastTaskMode = taskMode;
    _lastActiveHand = activeHand;
    _lastXRBackend = xrBackend;
    _lastOpenXRTrackingMode = openXRTrackingMode;

    AutoAssignReferences();
    AutoAssignXRInput();
    UpdateOpenXRVisualMode();
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

    if (_lastActiveHand != activeHand)
    {
        _lastActiveHand = activeHand;
        ResetHandRaySmoothing();
        SelectEffect();
    }

    if (_lastXRBackend != xrBackend || _lastOpenXRTrackingMode != openXRTrackingMode)
    {
        _lastXRBackend = xrBackend;
        _lastOpenXRTrackingMode = openXRTrackingMode;
        ResetHandRaySmoothing();
        UpdateOpenXRVisualMode();
        SelectEffect();
    }

    AutoAssignXRInput();
    UpdateOpenXRVisualMode();
    UpdateXRConfirm();
    UpdateCalibrationInput();
    SyncEffectReferences();

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
        AutoAssignOpenXRHandPrefabs();
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

    if (statReadout == null)
        statReadout = GameObject.Find("StatText")?.GetComponent<TextMeshProUGUI>();

    if (rigRoot == null)
    {
        var rigObj =
            GameObject.Find("XR Origin") ??
            GameObject.Find("[CameraRig]") ??
            GameObject.Find("CameraRig");
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
    if (xrBackend == XRBackend.OpenXR)
    {
        EnsureOpenXRHandVisuals();

        if (_openXRHandVisualizerRoot == null)
        {
            var handVisualizer = GameObject.Find("Hand Visualizer");
            if (handVisualizer != null)
                _openXRHandVisualizerRoot = handVisualizer;
        }

        if (_leftHandVisualTransform == null)
        {
            var leftHandVisual = GameObject.Find("Left Hand Tracking");
            if (leftHandVisual != null)
                _leftHandVisualTransform = leftHandVisual.transform;
        }

        if (_rightHandVisualTransform == null)
        {
            var rightHandVisual = GameObject.Find("Right Hand Tracking");
            if (rightHandVisual != null)
                _rightHandVisualTransform = rightHandVisual.transform;
        }

        if (openXRTrackingMode == OpenXRTrackingMode.Controllers)
        {
            if (_leftControllerTransform == null)
            {
                var leftObj =
                    GameObject.Find("Left Controller") ??
                    GameObject.Find("LeftHand Controller");
                if (leftObj != null)
                    _leftControllerTransform = leftObj.transform;
            }

            if (_rightControllerTransform == null)
            {
                var rightObj =
                    GameObject.Find("Right Controller") ??
                    GameObject.Find("RightHand Controller");
                if (rightObj != null)
                    _rightControllerTransform = rightObj.transform;
            }

            if (_leftControllerRayOriginTransform == null)
            {
                var leftRayOrigin = GameObject.Find("Left Controller Stabilized Attach");
                if (leftRayOrigin != null)
                    _leftControllerRayOriginTransform = leftRayOrigin.transform;
            }

            if (_rightControllerRayOriginTransform == null)
            {
                var rightRayOrigin = GameObject.Find("Right Controller Stabilized Attach");
                if (rightRayOrigin != null)
                    _rightControllerRayOriginTransform = rightRayOrigin.transform;
            }

            if (_leftControllerRayOriginTransform == null)
                _leftControllerRayOriginTransform = _leftControllerTransform;

            if (_rightControllerRayOriginTransform == null)
                _rightControllerRayOriginTransform = _rightControllerTransform;

            if (_leftPointerVisualTransform == null && _leftControllerTransform != null)
                _leftPointerVisualTransform = FindDeepChild(_leftControllerTransform, "UniversalController");

            if (_rightPointerVisualTransform == null && _rightControllerTransform != null)
                _rightPointerVisualTransform = FindDeepChild(_rightControllerTransform, "UniversalController");
        }

        return;
    }

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
    if (xrBackend == XRBackend.OpenXR && openXRTrackingMode == OpenXRTrackingMode.Hands)
        return null;

    return activeHand == Handedness.Left ? _leftControllerTransform : _rightControllerTransform;
}

Transform GetActiveControllerRayOriginTransform()
{
    if (xrBackend == XRBackend.OpenXR)
        return activeHand == Handedness.Left ? _leftControllerRayOriginTransform : _rightControllerRayOriginTransform;

    return GetActiveControllerTransform();
}

Transform GetActivePointerVisual()
{
    if (xrBackend == XRBackend.OpenXR)
    {
        if (openXRTrackingMode == OpenXRTrackingMode.Controllers)
        {
            var pointerVisual = activeHand == Handedness.Left ? _leftPointerVisualTransform : _rightPointerVisualTransform;
            return pointerVisual != null ? pointerVisual : GetActiveControllerTransform();
        }

        var handVisual = activeHand == Handedness.Left ? _leftHandVisualTransform : _rightHandVisualTransform;
        if (handVisual != null)
            return handVisual;

        return _openXRHandVisualizerRoot != null ? _openXRHandVisualizerRoot.transform : rigRoot;
    }

    Transform controller = GetActiveControllerTransform();
    if (controller == null) return null;

    Transform model = controller.Find("Model");
    return model != null ? model : controller;
}

void UpdateXRConfirm()
{
    bool down = false;

    if (xrBackend == XRBackend.OpenXR)
    {
        if (openXRTrackingMode == OpenXRTrackingMode.Controllers)
        {
            down = GetOpenXRButtonEdge(CommonUsages.triggerButton, activeHand, ref _confirmPressedLastFrame);
        }
        else
        {
            down = UpdateHandDwellConfirm();
        }
    }
    else if (_confirmAction != null)
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
    bool down = false;

    if (xrBackend == XRBackend.OpenXR)
    {
        down = GetOpenXRButtonEdge(CommonUsages.primaryButton, activeHand, ref _calibrateDownLastFrame);
    }
    else if (_calibrateAction != null)
    {
        down = _calibrateAction.GetStateDown(calibrationHand);
    }

    if (down)
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
    AutoAssignReferences();
    AutoAssignXRInput();

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

    SyncEffectReferences();
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

    if (_effect == null)
        return (new Ray(Vector3.zero, Vector3.forward), new Pose(Vector3.zero, Quaternion.identity), false);

    Pose rawPose;
    Ray rawRay;
    if (!TryGetRawInput(out rawRay, out rawPose))
        return (new Ray(Vector3.zero, Vector3.forward), new Pose(Vector3.zero, Quaternion.identity), false);

    var confirm = _confirmDown;

    var ray = _effect.TransformRay(rawRay);
    var pose = _effect.TransformPose(rawPose);

    return (ray, pose, confirm);
}

Ray GetRawPointerRay()
{
    AutoAssignXRInput();

    if (!TryGetRawInput(out Ray ray, out _))
        return new Ray(Vector3.zero, Vector3.forward);

    return ray;
}

bool TryGetRawInput(out Ray ray, out Pose pose)
{
    if (xrBackend == XRBackend.OpenXR && openXRTrackingMode == OpenXRTrackingMode.Hands)
        return TryGetOpenXRHandInput(out ray, out pose);

    Transform controller = GetActiveControllerTransform();
    Transform rayOrigin = GetActiveControllerRayOriginTransform();
    if (controller == null || rayOrigin == null)
    {
        ray = new Ray(Vector3.zero, Vector3.forward);
        pose = new Pose(Vector3.zero, Quaternion.identity);
        return false;
    }

    pose = new Pose(rayOrigin.position, rayOrigin.rotation);
    ray = new Ray(rayOrigin.position, rayOrigin.forward);
    return true;
}

InputDevice GetOpenXRInputDevice()
{
    return GetOpenXRInputDevice(activeHand);
}

InputDevice GetOpenXRInputDevice(Handedness hand)
{
    XRNode xrNode = hand == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand;
    return InputDevices.GetDeviceAtXRNode(xrNode);
}

bool GetOpenXRButtonDown(InputFeatureUsage<bool> usage)
{
    return GetOpenXRButtonDown(usage, activeHand);
}

bool GetOpenXRButtonDown(InputFeatureUsage<bool> usage, Handedness hand)
{
    var device = GetOpenXRInputDevice(hand);
    if (!device.isValid)
        return false;

    if (!device.TryGetFeatureValue(usage, out bool isPressed))
        return false;

    return isPressed;
}

bool GetOpenXRButtonEdge(InputFeatureUsage<bool> usage, Handedness hand, ref bool pressedLastFrame)
{
    bool isPressed = GetOpenXRButtonDown(usage, hand);
    bool pressedThisFrame = isPressed && !pressedLastFrame;
    pressedLastFrame = isPressed;
    return pressedThisFrame;
}

bool UpdateHandDwellConfirm()
{
    bool isPointing = IsActiveHandPointingGesture();
    _handPointingActive = isPointing;

    if (!isPointing)
    {
        _handPointingStartTime = -1f;
        _handDwellProgress01 = 0f;
        _handDwellTriggered = false;
        return false;
    }

    if (_handPointingStartTime < 0f)
        _handPointingStartTime = Time.time;

    float dwellDuration = Mathf.Max(0.05f, handDwellSeconds);
    float elapsed = Time.time - _handPointingStartTime;
    _handDwellProgress01 = Mathf.Clamp01(elapsed / dwellDuration);

    if (!_handDwellTriggered && elapsed >= dwellDuration)
    {
        _handDwellTriggered = true;
        return true;
    }

    return false;
}

XRHands.XRHandSubsystem GetXRHandSubsystem()
{
    if (_xrHandSubsystem != null && _xrHandSubsystem.running)
        return _xrHandSubsystem;

    var generalSettings = XRManagement.XRGeneralSettings.Instance;
    var loader = generalSettings != null ? generalSettings.Manager.activeLoader : null;
    _xrHandSubsystem = loader != null ? loader.GetLoadedSubsystem<XRHands.XRHandSubsystem>() : null;
    return _xrHandSubsystem;
}

bool TryGetOpenXRHandInput(out Ray ray, out Pose pose)
{
    var handSubsystem = GetXRHandSubsystem();
    if (handSubsystem == null)
    {
        ray = new Ray(Vector3.zero, Vector3.forward);
        pose = new Pose(Vector3.zero, Quaternion.identity);
        return false;
    }

    var hand = activeHand == Handedness.Left ? handSubsystem.leftHand : handSubsystem.rightHand;
    if (!TryGetTrackedJointPose(hand, XRHands.XRHandJointID.IndexTip, out Pose indexTipPose) ||
        !TryGetTrackedJointPose(hand, XRHands.XRHandJointID.IndexIntermediate, out Pose indexKnucklePose))
    {
        ray = new Ray(Vector3.zero, Vector3.forward);
        pose = new Pose(Vector3.zero, Quaternion.identity);
        return false;
    }

    Vector3 worldOrigin = TransformTrackingPointToWorld(indexTipPose.position);
    Vector3 worldKnuckle = TransformTrackingPointToWorld(indexKnucklePose.position);
    Vector3 worldDirection = (worldOrigin - worldKnuckle).normalized;

    if (worldDirection.sqrMagnitude < 0.0001f)
        worldDirection = TransformTrackingRotationToWorld(indexTipPose.rotation) * Vector3.forward;

    ApplyHandRaySmoothing(ref worldOrigin, ref worldDirection);

    Quaternion worldRotation = Quaternion.LookRotation(worldDirection, Vector3.up);
    pose = new Pose(worldOrigin, worldRotation);
    ray = new Ray(worldOrigin, worldDirection);
    return true;
}

bool IsActiveHandPointingGesture()
{
    var handSubsystem = GetXRHandSubsystem();
    if (handSubsystem == null)
        return false;

    var hand = activeHand == Handedness.Left ? handSubsystem.leftHand : handSubsystem.rightHand;
    if (!hand.isTracked)
        return false;

    bool indexStraight = TryGetFingerStraightness(hand, XRHands.XRHandJointID.IndexProximal, XRHands.XRHandJointID.IndexIntermediate, XRHands.XRHandJointID.IndexDistal, XRHands.XRHandJointID.IndexTip, out float indexStraightness)
        && indexStraightness >= 0.75f;

    bool middleCurled = TryGetFingerStraightness(hand, XRHands.XRHandJointID.MiddleProximal, XRHands.XRHandJointID.MiddleIntermediate, XRHands.XRHandJointID.MiddleDistal, XRHands.XRHandJointID.MiddleTip, out float middleStraightness)
        && middleStraightness < 0.70f;

    bool ringCurled = TryGetFingerStraightness(hand, XRHands.XRHandJointID.RingProximal, XRHands.XRHandJointID.RingIntermediate, XRHands.XRHandJointID.RingDistal, XRHands.XRHandJointID.RingTip, out float ringStraightness)
        && ringStraightness < 0.70f;

    bool littleCurled = TryGetFingerStraightness(hand, XRHands.XRHandJointID.LittleProximal, XRHands.XRHandJointID.LittleIntermediate, XRHands.XRHandJointID.LittleDistal, XRHands.XRHandJointID.LittleTip, out float littleStraightness)
        && littleStraightness < 0.70f;

    return indexStraight && middleCurled && ringCurled && littleCurled;
}

bool TryGetFingerStraightness(
    XRHands.XRHand hand,
    XRHands.XRHandJointID proximalId,
    XRHands.XRHandJointID intermediateId,
    XRHands.XRHandJointID distalId,
    XRHands.XRHandJointID tipId,
    out float straightness)
{
    straightness = 0f;

    if (!TryGetTrackedJointPose(hand, proximalId, out Pose proximalPose) ||
        !TryGetTrackedJointPose(hand, intermediateId, out Pose intermediatePose) ||
        !TryGetTrackedJointPose(hand, distalId, out Pose distalPose) ||
        !TryGetTrackedJointPose(hand, tipId, out Pose tipPose))
    {
        return false;
    }

    Vector3 segmentA = (intermediatePose.position - proximalPose.position).normalized;
    Vector3 segmentB = (distalPose.position - intermediatePose.position).normalized;
    Vector3 segmentC = (tipPose.position - distalPose.position).normalized;

    if (segmentA.sqrMagnitude < 0.5f || segmentB.sqrMagnitude < 0.5f || segmentC.sqrMagnitude < 0.5f)
        return false;

    straightness = (Vector3.Dot(segmentA, segmentB) + Vector3.Dot(segmentB, segmentC)) * 0.5f;
    return true;
}

bool TryGetTrackedJointPose(XRHands.XRHand hand, XRHands.XRHandJointID jointId, out Pose jointPose)
{
    jointPose = default;
    var joint = hand.GetJoint(jointId);
    return joint.TryGetPose(out jointPose);
}

Vector3 TransformTrackingPointToWorld(Vector3 trackingSpacePoint)
{
    return rigRoot != null ? rigRoot.TransformPoint(trackingSpacePoint) : trackingSpacePoint;
}

Quaternion TransformTrackingRotationToWorld(Quaternion trackingSpaceRotation)
{
    return rigRoot != null ? rigRoot.rotation * trackingSpaceRotation : trackingSpaceRotation;
}

void UpdateOpenXRVisualMode()
{
    if (xrBackend != XRBackend.OpenXR)
        return;

    bool useHands = openXRTrackingMode == OpenXRTrackingMode.Hands;

    if (_openXRHandVisualizerRoot != null)
        _openXRHandVisualizerRoot.SetActive(useHands);

    SetHandVisualState(Handedness.Left, useHands);
    SetHandVisualState(Handedness.Right, useHands);

    SetControllerVisualState(Handedness.Left, !useHands && activeHand == Handedness.Left);
    SetControllerVisualState(Handedness.Right, !useHands && activeHand == Handedness.Right);
}

public TextMeshProUGUI GetStatReadout()
{
    AutoAssignReferences();
    return statReadout;
}

void SetControllerVisualState(Handedness hand, bool active)
{
    Transform explicitVisual = hand == Handedness.Left ? _leftPointerVisualTransform : _rightPointerVisualTransform;
    if (explicitVisual != null)
    {
        explicitVisual.gameObject.SetActive(active);
        return;
    }

    Transform controllerTransform = hand == Handedness.Left ? _leftControllerTransform : _rightControllerTransform;
    Transform controllerVisual = controllerTransform != null ? FindDeepChild(controllerTransform, "UniversalController") : null;
    if (controllerVisual != null)
        controllerVisual.gameObject.SetActive(active);
}

void SetHandVisualState(Handedness hand, bool active)
{
    Transform handVisual = hand == Handedness.Left ? _leftHandVisualTransform : _rightHandVisualTransform;
    if (handVisual != null)
        handVisual.gameObject.SetActive(active);
}

void EnsureOpenXRHandVisuals()
{
    if (rigRoot == null)
        return;

    if (_leftHandVisualTransform == null && openXRLeftHandTrackingPrefab != null)
        _leftHandVisualTransform = InstantiateOpenXRHandPrefab(openXRLeftHandTrackingPrefab, "Left Hand Tracking");

    if (_rightHandVisualTransform == null && openXRRightHandTrackingPrefab != null)
        _rightHandVisualTransform = InstantiateOpenXRHandPrefab(openXRRightHandTrackingPrefab, "Right Hand Tracking");
}

void ApplyHandRaySmoothing(ref Vector3 worldOrigin, ref Vector3 worldDirection)
{
    float smoothTime = Mathf.Max(0f, handRaySmoothingSeconds);
    if (smoothTime <= 0.0001f)
    {
        _smoothedHandRayInitialized = false;
        return;
    }

    if (!_smoothedHandRayInitialized || _smoothedHandRayHand != activeHand)
    {
        _smoothedHandRayInitialized = true;
        _smoothedHandRayHand = activeHand;
        _smoothedHandRayOrigin = worldOrigin;
        _smoothedHandRayDirection = worldDirection;
        return;
    }

    float t = 1f - Mathf.Exp(-Time.deltaTime / smoothTime);
    _smoothedHandRayOrigin = Vector3.Lerp(_smoothedHandRayOrigin, worldOrigin, t);
    _smoothedHandRayDirection = Vector3.Slerp(_smoothedHandRayDirection, worldDirection, t).normalized;

    worldOrigin = _smoothedHandRayOrigin;
    worldDirection = _smoothedHandRayDirection;
}

void ResetHandRaySmoothing()
{
    _smoothedHandRayInitialized = false;
}

Transform InstantiateOpenXRHandPrefab(GameObject prefab, string objectName)
{
    Transform existing = FindDeepChild(rigRoot, objectName);
    if (existing != null)
        return existing;

    Transform handVisualParent = FindDeepChild(rigRoot, "Camera Offset");
    if (handVisualParent == null)
        handVisualParent = rigRoot;

    var instance = Instantiate(prefab, handVisualParent);
    instance.name = objectName;
    instance.transform.localPosition = Vector3.zero;
    instance.transform.localRotation = Quaternion.identity;
    instance.transform.localScale = Vector3.one;

    return instance.transform;
}

void AutoAssignOpenXRHandPrefabs()
{
#if UNITY_EDITOR
    if (openXRLeftHandTrackingPrefab == null)
    {
        openXRLeftHandTrackingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Samples/XR Hands/1.5.1/HandVisualizer/Prefabs/Left Hand Tracking.prefab");
    }

    if (openXRRightHandTrackingPrefab == null)
    {
        openXRRightHandTrackingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Samples/XR Hands/1.5.1/HandVisualizer/Prefabs/Right Hand Tracking.prefab");
    }
#endif
}

static Transform FindDeepChild(Transform root, string childName)
{
    if (root == null)
        return null;

    var stack = new Stack<Transform>();
    stack.Push(root);

    while (stack.Count > 0)
    {
        var current = stack.Pop();
        if (current.name == childName)
            return current;

        for (int i = 0; i < current.childCount; i++)
            stack.Push(current.GetChild(i));
    }

    return null;
}

void SyncEffectReferences()
{
    if (effectMode != EffectMode.Skew)
        return;

    skew.visualWorldRoot = visualWorldRoot;
    skew.visualPointerRoot = GetActivePointerVisual();
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
