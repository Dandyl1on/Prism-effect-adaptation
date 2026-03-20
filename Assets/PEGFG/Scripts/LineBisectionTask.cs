using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class LineBisectionTask : MonoBehaviour, ISandboxTask
{
    public enum BlockType { Baseline, Post }

    [Header("References")]
    private SandboxRunner runner;
    private Transform boardPlane;
    private Transform hmd;
    private LineRenderer lineRenderer;
    private Transform cursorMarker;
    private Transform midpointMarker;
    private TextMeshProUGUI readout;

    [Header("Block")]
    private BlockType blockType = BlockType.Baseline;
    private int trialsPerBlock = 20;
    private bool latchConfirm = true;

    [Header("Trial gating (return to start posture)")]
    public bool requireResetBetweenTrials = false;
    private float resetDropMeters = 0.6f;
    private float minSecondsBetweenTrials = 0f;

    [Header("Line settings (in metres)")]
    private float lineLength = 1f;
    private float lineZRange = 0.1f;
    private bool clampToLineSegment = true;

    [Header("Randomisation")]
    private bool randomiseLineZEachTrial = true;
    private bool randomiseLineLengthEachTrial = true;
    private float minLineLength = 1f;
    private float maxLineLength = 1.25f;

    [Header("Debug")]
    public bool showCursor = true;
    [SerializeField] private bool showBoardMidpoint = true;

    int _trialIndex = 0;
    bool _confirmLatched = false;
    bool _armed = true;
    float _lastAcceptedTime = -999f;

    float _currentLineZ = 0f;
    float _currentHalfLen = 0.2f;

    readonly List<float> _errorsCm = new List<float>(128);
    float? _baselineMeanCm = null;
    float? _postMeanCm = null;

    bool _isActive;

    public SandboxRunner.TaskMode TaskMode => SandboxRunner.TaskMode.LineBisection;

    void Awake()
    {
        AutoAssignReferences();
    }

    void Reset()
    {
        AutoAssignReferences();
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            AutoAssignReferences();
    }

    public void SetTaskActive(bool active)
    {
        _isActive = active;
        UpdateVisuals();

        if (_isActive && lineRenderer != null && boardPlane != null)
            SetupNewLine();
    }

    void AutoAssignReferences()
    {
        if (runner == null)
            runner = FindObjectOfType<SandboxRunner>();

        if (boardPlane == null)
            boardPlane = GameObject.Find("Board")?.transform;

        if (hmd == null)
        {
            var camObj = GameObject.Find("Camera") ?? GameObject.Find("Camera (eye)") ?? GameObject.Find("Main Camera");
            if (camObj != null) hmd = camObj.transform;
            else if (Camera.main != null) hmd = Camera.main.transform;
        }

        if (lineRenderer == null)
            lineRenderer = GameObject.Find("Bisectionline")?.GetComponent<LineRenderer>();

        if (cursorMarker == null)
            cursorMarker = GameObject.Find("Hitmarker")?.transform ?? GameObject.Find("HitMarker")?.transform;

        if (midpointMarker == null)
            midpointMarker = GameObject.Find("MidPointMarker")?.transform ?? GameObject.Find("MidpointMarker")?.transform;

        if (readout == null)
            readout = runner != null ? runner.GetStatReadout() : GameObject.Find("StatText")?.GetComponent<TextMeshProUGUI>();
    }

    void UpdateVisuals()
    {
        if (lineRenderer)
            lineRenderer.gameObject.SetActive(_isActive);

        if (cursorMarker)
            cursorMarker.gameObject.SetActive(_isActive && showCursor);

        if (midpointMarker)
            midpointMarker.gameObject.SetActive(_isActive && showBoardMidpoint);
    }

    void Update()
    {
        if (!_isActive) return;
        if (runner == null || boardPlane == null || lineRenderer == null) return;

        UpdateVisuals();

        if (midpointMarker && showBoardMidpoint)
            midpointMarker.position = boardPlane.position;

        if (_trialIndex >= trialsPerBlock)
        {
            UpdateReadout(final: true);
            return;
        }

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

        if (!IntersectRayWithBoard(ray, out Vector3 hit))
        {
            UpdateReadout(final: false);
            return;
        }

        Vector3 local = boardPlane.InverseTransformPoint(hit);
        local.z = _currentLineZ;

        if (clampToLineSegment)
            local.x = Mathf.Clamp(local.x, -_currentHalfLen, _currentHalfLen);

        Vector3 constrainedWorld = boardPlane.TransformPoint(local);

        if (cursorMarker && showCursor)
            cursorMarker.position = constrainedWorld;

        if (confirm)
        {
            if (requireResetBetweenTrials)
            {
                if (Time.time - _lastAcceptedTime < minSecondsBetweenTrials)
                {
                    UpdateReadout(final: false);
                    return;
                }
                _lastAcceptedTime = Time.time;
                _armed = false;
            }

            if (latchConfirm) _confirmLatched = true;

            float errorMeters = local.x;
            float errorCm = errorMeters * 100f;

            _errorsCm.Add(errorCm);
            _trialIndex++;

            Debug.Log($"[LineBisection] block={blockType} trial={_trialIndex}/{trialsPerBlock} error={errorCm:0.0} cm (lineZ={_currentLineZ:0.000}m len={_currentHalfLen * 2f:0.000}m)");

            if (_trialIndex >= trialsPerBlock)
            {
                var (mean, sd) = MeanAndSd(_errorsCm);
                if (blockType == BlockType.Baseline) _baselineMeanCm = mean;
                if (blockType == BlockType.Post) _postMeanCm = mean;

                Debug.Log($"[LineBisection] block={blockType} COMPLETE mean={mean:0.0} cm sd={sd:0.0} cm n={_errorsCm.Count}");

                if (_baselineMeanCm.HasValue && _postMeanCm.HasValue)
                {
                    float afterEffect = _postMeanCm.Value - _baselineMeanCm.Value;
                    Debug.Log($"[LineBisection] AFTER-EFFECT (Post - Baseline) = {afterEffect:0.0} cm");
                }

                UpdateReadout(final: true);
                return;
            }

            SetupNewLine();
        }

        UpdateReadout(final: false);
    }

    public void StartNewBlock(BlockType newBlock)
    {
        blockType = newBlock;
        _trialIndex = 0;
        _confirmLatched = false;
        _errorsCm.Clear();

        _armed = true;
        _lastAcceptedTime = -999f;

        if (_isActive)
            SetupNewLine();

        UpdateReadout(final: false);
    }

    public void ClearSummaries()
    {
        _baselineMeanCm = null;
        _postMeanCm = null;
    }

    void SetupNewLine()
    {
        float len = lineLength;
        if (randomiseLineLengthEachTrial)
            len = Random.Range(minLineLength, maxLineLength);

        _currentHalfLen = Mathf.Max(0.01f, len * 0.5f);

        if (randomiseLineZEachTrial)
            _currentLineZ = Random.Range(-lineZRange, lineZRange);
        else
            _currentLineZ = 0f;

        Vector3 aLocal = new Vector3(-_currentHalfLen, 0f, _currentLineZ);
        Vector3 bLocal = new Vector3(+_currentHalfLen, 0f, _currentLineZ);

        Vector3 aWorld = boardPlane.TransformPoint(aLocal);
        Vector3 bWorld = boardPlane.TransformPoint(bLocal);

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;
        lineRenderer.SetPosition(0, aWorld);
        lineRenderer.SetPosition(1, bWorld);
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

    void UpdateReadout(bool final)
    {
        if (!readout || !_isActive) return;

        string gateTxt = (requireResetBetweenTrials && hmd != null) ? (_armed ? "ARMED" : "RESET") : "—";

        if (_errorsCm.Count == 0)
        {
            readout.text =
                $"Line Bisection ({blockType})\n" +
                $"Trial {_trialIndex}/{trialsPerBlock}\n" +
                $"Error: —\n" +
                $"Gate: {gateTxt}";
            return;
        }

        var (mean, sd) = MeanAndSd(_errorsCm);

        if (!final)
        {
            float last = _errorsCm[_errorsCm.Count - 1];
            readout.text =
                $"Line Bisection ({blockType})\n" +
                $"Trial {_trialIndex}/{trialsPerBlock}\n" +
                $"Last: {last:0.0} cm\n" +
                $"Mean: {mean:0.0} cm (SD {sd:0.0})\n" +
                $"Gate: {gateTxt}";
            return;
        }

        string summary =
            $"Line Bisection ({blockType}) COMPLETE\n" +
            $"n={_errorsCm.Count}\n" +
            $"Mean: {mean:0.0} cm (SD {sd:0.0})";

        if (_baselineMeanCm.HasValue && _postMeanCm.HasValue)
        {
            float afterEffect = _postMeanCm.Value - _baselineMeanCm.Value;
            summary += $"\nAfter-effect: {afterEffect:0.0} cm";
        }

        readout.text = summary;
    }

    static (float mean, float sd) MeanAndSd(List<float> xs)
    {
        int n = xs.Count;
        if (n <= 0) return (0f, 0f);

        double sum = 0.0;
        for (int i = 0; i < n; i++) sum += xs[i];
        double mean = sum / n;

        if (n == 1) return ((float)mean, 0f);

        double var = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d = xs[i] - mean;
            var += d * d;
        }
        var /= (n - 1);
        double sd = System.Math.Sqrt(var);

        return ((float)mean, (float)sd);
    }
}
