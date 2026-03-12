using UnityEngine;
using TMPro;

public class ExposureTask : MonoBehaviour, ISandboxTask
{
    [Header("References")]
    public SandboxRunner runner;
    public Transform boardPlane;
    public Transform hmd;

    [Tooltip("Three targets in a 1x3 horizontal layout: Left, Center, Right.")]
    public Transform[] targets = new Transform[3];

    [Tooltip("Optional renderers matching the target order. If left empty, they will be fetched from the target objects.")]
    public Renderer[] targetRenderers = new Renderer[3];

    public Transform cursorMarker;
    public Transform hitMarker;
    public TextMeshProUGUI readout;

    [Header("Exposure Block")]
    [Tooltip("Number of successful target hits required before exposure is complete.")]
    public int successfulHitsToComplete = 90;

    [Tooltip("If true, target order is randomized. If false, cycles Left-Center-Right.")]
    public bool randomizeTargetOrder = true;

    [Tooltip("Prevent immediate repetition of the same target when randomizing.")]
    public bool avoidImmediateRepeat = true;

    [Header("Hit Logic")]
    [Tooltip("Radius around the target that counts as a hit, in metres.")]
    public float hitRadiusMeters = 0.05f;

    [Tooltip("If true, the cursor marker is shown on the board plane.")]
    public bool showCursor = true;

    [Tooltip("If true, the hit marker is shown at the accepted click position.")]
    public bool showHitMarker = true;

    [Header("Trial gating (return to start posture)")]
    public bool requireResetBetweenTrials = true;
    public float resetDropMeters = 0.6f;
    public float minSecondsBetweenAttempts = 0.10f;
    public bool latchConfirm = true;

    [Header("Colors")]
    public Color idleColor = Color.white;
    public Color activeColor = Color.green;
    public Color hitColor = Color.cyan;
    public Color missColor = Color.red;

    [Header("Debug")]
    public bool logAttempts = true;

    bool _isActive;
    bool _confirmLatched = false;
    bool _armed = true;
    float _lastAcceptedTime = -999f;

    int _successCount = 0;
    int _attemptCount = 0;
    int _currentTargetIndex = 1;
    int _lastTargetIndex = -1;

    public SandboxRunner.TaskMode TaskMode => SandboxRunner.TaskMode.Exposure;

    public void SetTaskActive(bool active)
    {
        _isActive = active;
        UpdateVisualsFromState();

        if (_isActive)
            RefreshTargetVisuals();
    }

    void Awake()
    {
        AutoFillRenderers();
    }

    void Start()
    {
        UpdateVisualsFromState();
    }

        void UpdateVisualsFromState()
    {
        SetVisualsVisible(_isActive);
    }

    public void StartExposureBlock()
    {
        _successCount = 0;
        _attemptCount = 0;
        _confirmLatched = false;
        _armed = true;
        _lastAcceptedTime = -999f;

        AutoFillRenderers();
        SetVisualsVisible(true);   // add this
        PickNextTarget(forceCenterFirst: true);
        RefreshTargetVisuals();
        UpdateReadout();
    }

    void Update()
    {
        if (!_isActive) return;
        if (runner == null || boardPlane == null || targets == null || targets.Length < 3) return;

        var (ray, pose, confirm) = runner.GetTransformedInput();

        if (latchConfirm)
        {
            if (!confirm) _confirmLatched = false;
            if (confirm && _confirmLatched) confirm = false;
        }

        if (requireResetBetweenTrials && hmd != null)
        {
            float resetY = hmd.position.y - resetDropMeters;

            if (!_armed && pose.position.y <= resetY)
                _armed = true;

            if (confirm && !_armed)
                confirm = false;
        }

        if (!IntersectRayWithBoard(ray, out Vector3 hitPoint))
        {
            UpdateReadout();
            return;
        }

        if (cursorMarker && showCursor)
            cursorMarker.position = hitPoint;

        if (confirm)
        {
            if (requireResetBetweenTrials)
            {
                if (Time.time - _lastAcceptedTime < minSecondsBetweenAttempts)
                {
                    UpdateReadout();
                    return;
                }

                _lastAcceptedTime = Time.time;
                _armed = false;
            }

            if (latchConfirm)
                _confirmLatched = true;

            _attemptCount++;

            var target = targets[_currentTargetIndex];
            float dist = Vector3.Distance(hitPoint, target.position);
            bool isHit = dist <= hitRadiusMeters;

            if (hitMarker && showHitMarker)
                hitMarker.position = hitPoint;

            if (isHit)
            {
                _successCount++;

                if (logAttempts)
                    Debug.Log($"[Exposure] HIT {_successCount}/{successfulHitsToComplete} on target {_currentTargetIndex} (attempt {_attemptCount}, dist={dist:0.000}m)");

                FlashSingleTarget(_currentTargetIndex, hitColor);

                if (_successCount >= successfulHitsToComplete)
                {
                    UpdateReadout();
                    Debug.Log("[Exposure] Exposure block complete.");
                    runner?.NotifyExposureCompleted();
                    return;
                }

                PickNextTarget(forceCenterFirst: false);
                RefreshTargetVisuals();
            }
            else
            {
                if (logAttempts)
                    Debug.Log($"[Exposure] MISS target {_currentTargetIndex} (attempt {_attemptCount}, dist={dist:0.000}m)");

                FlashSingleTarget(_currentTargetIndex, missColor);
            }
        }

        UpdateReadout();
    }

    void SetVisualsVisible(bool visible)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
                targets[i].gameObject.SetActive(visible);
        }

        if (cursorMarker)
            cursorMarker.gameObject.SetActive(visible && showCursor);

        if (hitMarker)
            hitMarker.gameObject.SetActive(visible && showHitMarker);

        if (visible)
            RefreshTargetVisuals();
    }

    void AutoFillRenderers()
    {
        if (targets == null) return;

        if (targetRenderers == null || targetRenderers.Length != targets.Length)
            targetRenderers = new Renderer[targets.Length];

        for (int i = 0; i < targets.Length; i++)
        {
            if (targetRenderers[i] == null && targets[i] != null)
                targetRenderers[i] = targets[i].GetComponentInChildren<Renderer>();
        }
    }

    void PickNextTarget(bool forceCenterFirst)
    {
        _lastTargetIndex = _currentTargetIndex;

        if (forceCenterFirst)
        {
            _currentTargetIndex = Mathf.Clamp(1, 0, targets.Length - 1);
            return;
        }

        if (!randomizeTargetOrder)
        {
            _currentTargetIndex++;
            if (_currentTargetIndex >= targets.Length)
                _currentTargetIndex = 0;
            return;
        }

        int next = _currentTargetIndex;
        int safety = 0;

        while (safety < 20)
        {
            next = Random.Range(0, targets.Length);

            if (!avoidImmediateRepeat || next != _lastTargetIndex)
                break;

            safety++;
        }

        _currentTargetIndex = next;
    }

    void RefreshTargetVisuals()
    {
        if (!_isActive) return;

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            if (targetRenderers[i] == null) continue;
            SetRendererColor(targetRenderers[i], i == _currentTargetIndex ? activeColor : idleColor);
        }
    }

    void FlashSingleTarget(int index, Color color)
    {
        if (index < 0 || index >= targetRenderers.Length) return;
        if (targetRenderers[index] == null) return;

        SetRendererColor(targetRenderers[index], color);
    }

    void SetRendererColor(Renderer r, Color c)
    {
        if (r == null || r.material == null) return;
        r.material.color = c;
    }

    bool IntersectRayWithBoard(Ray r, out Vector3 hit)
    {
        Vector3 n = boardPlane.up;
        float denom = Vector3.Dot(n, r.direction);
        if (Mathf.Abs(denom) < 1e-4f)
        {
            hit = default;
            return false;
        }

        var plane = new Plane(n, boardPlane.position);
        if (plane.Raycast(r, out float enter) && enter > 0f && enter < 10f)
        {
            hit = r.GetPoint(enter);
            return true;
        }

        hit = default;
        return false;
    }

    void UpdateReadout()
    {
        if (!readout || !_isActive) return;

        string gateTxt = (requireResetBetweenTrials && hmd != null) ? (_armed ? "ARMED" : "RESET") : "—";
        string targetName = TargetLabel(_currentTargetIndex);

        readout.text =
            $"Exposure\n" +
            $"Effect: {runner.CurrentEffectMode}\n" +
            $"Target: {targetName}\n" +
            $"Hits: {_successCount}/{successfulHitsToComplete}\n" +
            $"Attempts: {_attemptCount}\n" +
            $"Gate: {gateTxt}";
    }

    string TargetLabel(int i)
    {
        return i switch
        {
            0 => "Left",
            1 => "Center",
            2 => "Right",
            _ => $"T{i}"
        };
    }
}