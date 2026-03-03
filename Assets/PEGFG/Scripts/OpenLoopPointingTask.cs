using UnityEngine;
using TMPro;

public class OpenLoopPointingTask : MonoBehaviour
{
    [Header("References")]
    public SandboxRunner runner;
    public Transform boardPlane;          // plane transform (normal = forward)
    public TextMeshProUGUI readout;       // optional UI text
    public Transform hitMarker;           // small sphere to show endpoint
    public Transform midpointMarker;      // small sphere to show board centre

    [Header("Settings")]
    public float boardHalfWidth = 0.5f;   // metres
    public bool clampToBoard = true;

    [Header("Debug")]
    public bool showLiveAimMarker = true;     // marker follows ray continuously
    public bool showBoardMidpoint = true;     // show centre reference

    void Update()
    {
        if (runner == null || boardPlane == null)
            return;

        // Ensure midpoint marker stays at centre
        if (midpointMarker && showBoardMidpoint)
            midpointMarker.position = boardPlane.position;

        var (ray, _, confirm) = runner.GetTransformedInput();

        if (!IntersectRayWithBoard(ray, out Vector3 hit))
            return;

        if (clampToBoard)
            hit = ClampToBoard(hit);

        // Live aim debug (optional)
        if (showLiveAimMarker && hitMarker)
            hitMarker.position = hit;

        // Confirmed endpoint
        if (confirm)
        {
            if (!showLiveAimMarker && hitMarker)
                hitMarker.position = hit;

            float signedX = SignedOffsetOnBoard(hit);

            if (readout)
                readout.text = $"OLP offset: {signedX:0.000} m";

            Debug.Log($"[OLP] offset={signedX:0.000} m hit={hit}");
        }
    }

    bool IntersectRayWithBoard(Ray r, out Vector3 hit)
    {
        var plane = new Plane(boardPlane.forward, boardPlane.position);
        if (plane.Raycast(r, out float enter))
        {
            hit = r.GetPoint(enter);
            return true;
        }

        hit = default;
        return false;
    }

    Vector3 ClampToBoard(Vector3 hitWorld)
    {
        Vector3 local = boardPlane.InverseTransformPoint(hitWorld);
        local.x = Mathf.Clamp(local.x, -boardHalfWidth, boardHalfWidth);
        return boardPlane.TransformPoint(local);
    }

    float SignedOffsetOnBoard(Vector3 hitWorld)
    {
        Vector3 local = boardPlane.InverseTransformPoint(hitWorld);
        return local.x; // signed horizontal deviation in metres
    }
}