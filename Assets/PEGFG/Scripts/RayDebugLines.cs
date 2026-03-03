using UnityEngine;

public class RayDebugLines : MonoBehaviour
{
    public SandboxRunner runner;
    public LineRenderer rawLine;
    public LineRenderer transformedLine;

    public float length = 3f;
    public bool showRaw = true;
    public bool showTransformed = true;

    void LateUpdate()
    {
        if (runner == null || rawLine == null || transformedLine == null) return;

        // Get raw from the input provider, and transformed from runner output
        // We’ll fetch transformed directly, and raw via runner internal debug rays by duplicating logic here:
        // Best simple approach: ask runner for transformed, and also draw raw by temporarily disabling effects.
        var (rayT, _, _) = runner.GetTransformedInput();

        // Raw ray: use the selected input provider directly
        // (Add this helper to SandboxRunner if you want it cleaner, but this works if you expose a method)
        Ray rayR = runner.GetRawPointerRayForDebug();

        if (showRaw)
        {
            rawLine.enabled = true;
            rawLine.SetPosition(0, rayR.origin);
            rawLine.SetPosition(1, rayR.origin + rayR.direction * length);
        }
        else rawLine.enabled = false;

        if (showTransformed)
        {
            transformedLine.enabled = true;
            transformedLine.SetPosition(0, rayT.origin);
            transformedLine.SetPosition(1, rayT.origin + rayT.direction * length);
        }
        else transformedLine.enabled = false;
    }
}