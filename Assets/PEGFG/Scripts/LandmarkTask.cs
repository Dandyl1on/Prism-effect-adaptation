using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class LandmarkTask : MonoBehaviour, ISandboxTask
{
    public enum BlockType { Baseline, Post }
    public enum ChoiceSide { Left, Right }

    [Header("References")]
    public SandboxRunner runner;
    public Transform boardPlane;
    public Transform hmd;
    public LineRenderer leftRenderer;
    public LineRenderer rightRenderer;
    public Transform cursorMarker;
    public Transform midpointMarker;
    public TextMeshProUGUI readout;

    [Header("Block")]
    public BlockType blockType = BlockType.Baseline;
    public int trialsPerBlock = 20;
    public bool latchConfirm = true;

    [Header("Trial gating (return to start posture)")]
    public bool requireResetBetweenTrials = true;
    public float resetDropMeters = 0.6f;
    public float minSecondsBetweenTrials = 0.15f;

    [Header("Landmark stimulus (metres, board local X)")]
    public float centreGap = 0.02f;
    public float totalLength = 0.4f;
    public float lengthDifference = 0.06f;
    public float lineZRange = 0.25f;

    [Header("Randomisation")]
    public bool randomiseLineZEachTrial = true;
    public bool randomiseTotalLengthEachTrial = false;
    public float minTotalLength = 0.25f;
    public float maxTotalLength = 0.55f;

    public bool randomiseDifferenceEachTrial = false;
    public float minDifference = 0.02f;
    public float maxDifference = 0.10f;

    [Header("Cursor snapping")]
    public bool snapToNearestSegment = true;

    [Header("Debug")]
    public bool showCursor = true;
    public bool showBoardMidpoint = false;

    int _trialIndex = 0;
    bool _confirmLatched = false;
    bool _armed = true;
    float _lastAcceptedTime = -999f;

    float _currentLineZ = 0f;

    ChoiceSide _longerSide = ChoiceSide.Left;
    float _leftLen = 0.2f;
    float _rightLen = 0.2f;

    readonly List<int> _correct = new List<int>(128);
    readonly List<int> _choices = new List<int>(128);

    float? _baselineAcc = null;
    float? _postAcc = null;

    bool _isActive;

    public SandboxRunner.TaskMode TaskMode => SandboxRunner.TaskMode.Landmark;

    public void SetTaskActive(bool active)
    {
        _isActive = active;
        UpdateVisuals();

        if (_isActive && boardPlane != null && leftRenderer != null && rightRenderer != null)
            SetupNewStimulus();
    }

    void UpdateVisuals()
    {
        if (leftRenderer)
            leftRenderer.gameObject.SetActive(_isActive);

        if (rightRenderer)
            rightRenderer.gameObject.SetActive(_isActive);

        if (cursorMarker)
            cursorMarker.gameObject.SetActive(_isActive && showCursor);

        if (midpointMarker)
            midpointMarker.gameObject.SetActive(_isActive && showBoardMidpoint);
    }

    void Update()
    {
        if (!_isActive) return;
        if (runner == null || boardPlane == null || leftRenderer == null || rightRenderer == null) return;

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

        ChoiceSide chosenSide = (local.x >= 0f) ? ChoiceSide.Right : ChoiceSide.Left;

        Vector3 snappedLocal = snapToNearestSegment
            ? SnapToSegment(local, chosenSide)
            : ClampToSegment(local, chosenSide);

        Vector3 snappedWorld = boardPlane.TransformPoint(snappedLocal);

        if (cursorMarker && showCursor)
            cursorMarker.position = snappedWorld;

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

            bool isCorrect = (chosenSide == _longerSide);

            _correct.Add(isCorrect ? 1 : 0);
            _choices.Add(chosenSide == ChoiceSide.Right ? 1 : -1);
            _trialIndex++;

            Debug.Log($"[Landmark] block={blockType} trial={_trialIndex}/{trialsPerBlock} choice={chosenSide} longer={_longerSide} correct={(isCorrect ? 1 : 0)} " +
                      $"(L={_leftLen:0.000}m R={_rightLen:0.000}m gap={centreGap:0.000}m z={_currentLineZ:0.000}m)");

            if (_trialIndex >= trialsPerBlock)
            {
                float acc = Accuracy01(_correct);
                if (blockType == BlockType.Baseline) _baselineAcc = acc;
                if (blockType == BlockType.Post) _postAcc = acc;

                Debug.Log($"[Landmark] block={blockType} COMPLETE acc={acc * 100f:0.0}% n={_correct.Count}");

                if (_baselineAcc.HasValue && _postAcc.HasValue)
                {
                    float delta = (_postAcc.Value - _baselineAcc.Value) * 100f;
                    Debug.Log($"[Landmark] CHANGE (Post - Baseline) = {delta:0.0} percentage points");
                }

                UpdateReadout(final: true);
                return;
            }

            SetupNewStimulus();
        }

        UpdateReadout(final: false);
    }

    public void StartNewBlock(BlockType newBlock)
    {
        blockType = newBlock;
        _trialIndex = 0;
        _confirmLatched = false;

        _correct.Clear();
        _choices.Clear();

        _armed = true;
        _lastAcceptedTime = -999f;

        if (_isActive)
            SetupNewStimulus();

        UpdateReadout(final: false);
    }

    public void ClearSummaries()
    {
        _baselineAcc = null;
        _postAcc = null;
    }

    void SetupNewStimulus()
    {
        if (randomiseLineZEachTrial)
            _currentLineZ = Random.Range(-lineZRange, lineZRange);
        else
            _currentLineZ = 0f;

        float tot = totalLength;
        if (randomiseTotalLengthEachTrial)
            tot = Random.Range(minTotalLength, maxTotalLength);

        float diff = lengthDifference;
        if (randomiseDifferenceEachTrial)
            diff = Random.Range(minDifference, maxDifference);

        _longerSide = (Random.value < 0.5f) ? ChoiceSide.Left : ChoiceSide.Right;

        float a = (tot + diff) * 0.5f;
        float b = (tot - diff) * 0.5f;

        if (_longerSide == ChoiceSide.Left)
        {
            _leftLen = Mathf.Max(0.01f, a);
            _rightLen = Mathf.Max(0.01f, b);
        }
        else
        {
            _leftLen = Mathf.Max(0.01f, b);
            _rightLen = Mathf.Max(0.01f, a);
        }

        DrawSegments();
    }

    void DrawSegments()
    {
        float halfGap = Mathf.Max(0f, centreGap) * 0.5f;

        Vector3 l0 = new Vector3(-halfGap - _leftLen, 0f, _currentLineZ);
        Vector3 l1 = new Vector3(-halfGap, 0f, _currentLineZ);

        Vector3 r0 = new Vector3(+halfGap, 0f, _currentLineZ);
        Vector3 r1 = new Vector3(+halfGap + _rightLen, 0f, _currentLineZ);

        Vector3 l0w = boardPlane.TransformPoint(l0);
        Vector3 l1w = boardPlane.TransformPoint(l1);
        Vector3 r0w = boardPlane.TransformPoint(r0);
        Vector3 r1w = boardPlane.TransformPoint(r1);

        leftRenderer.positionCount = 2;
        leftRenderer.useWorldSpace = true;
        leftRenderer.SetPosition(0, l0w);
        leftRenderer.SetPosition(1, l1w);

        rightRenderer.positionCount = 2;
        rightRenderer.useWorldSpace = true;
        rightRenderer.SetPosition(0, r0w);
        rightRenderer.SetPosition(1, r1w);
    }

    Vector3 SnapToSegment(Vector3 local, ChoiceSide side)
    {
        float halfGap = Mathf.Max(0f, centreGap) * 0.5f;

        if (side == ChoiceSide.Left)
        {
            float minX = -halfGap - _leftLen;
            float maxX = -halfGap;
            local.x = Mathf.Clamp(local.x, minX, maxX);
        }
        else
        {
            float minX = +halfGap;
            float maxX = +halfGap + _rightLen;
            local.x = Mathf.Clamp(local.x, minX, maxX);
        }

        return local;
    }

    Vector3 ClampToSegment(Vector3 local, ChoiceSide side)
    {
        return SnapToSegment(local, side);
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

        if (_correct.Count == 0)
        {
            readout.text =
                $"Landmark ({blockType})\n" +
                $"Trial {_trialIndex}/{trialsPerBlock}\n" +
                $"Acc: —\n" +
                $"Gate: {gateTxt}";
            return;
        }

        float acc = Accuracy01(_correct);
        float bias = MeanChoice(_choices);

        if (!final)
        {
            readout.text =
                $"Landmark ({blockType})\n" +
                $"Trial {_trialIndex}/{trialsPerBlock}\n" +
                $"Acc: {acc * 100f:0.0}%\n" +
                $"Bias (R=+1): {bias:0.00}\n" +
                $"Gate: {gateTxt}";
            return;
        }

        string summary =
            $"Landmark ({blockType}) COMPLETE\n" +
            $"n={_correct.Count}\n" +
            $"Acc: {acc * 100f:0.0}%\n" +
            $"Bias (R=+1): {bias:0.00}";

        if (_baselineAcc.HasValue && _postAcc.HasValue)
        {
            float delta = (_postAcc.Value - _baselineAcc.Value) * 100f;
            summary += $"\nΔAcc: {delta:0.0} pp";
        }

        readout.text = summary;
    }

    static float Accuracy01(List<int> xs)
    {
        if (xs.Count == 0) return 0f;
        int sum = 0;
        for (int i = 0; i < xs.Count; i++) sum += xs[i];
        return (float)sum / xs.Count;
    }

    static float MeanChoice(List<int> xs)
    {
        if (xs.Count == 0) return 0f;
        int sum = 0;
        for (int i = 0; i < xs.Count; i++) sum += xs[i];
        return (float)sum / xs.Count;
    }
}