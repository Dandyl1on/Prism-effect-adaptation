using UnityEngine;

public class HeightCalibration : MonoBehaviour
{
    [Header("Assign")]
    public Transform rigRoot;     // [CameraRig]
    public Transform hmd;         // [CameraRig]/Camera
    public Transform boardMid;    // Board midpoint marker (or Board transform if its pivot is centre)

    [Header("Options")]
    public bool calibrateOnStart = false;

    void Start()
    {
        if (calibrateOnStart) CalibrateHeight();
    }

    [ContextMenu("Calibrate Height Now")]
    public void CalibrateHeight()
    {
        if (!rigRoot || !hmd || !boardMid)
        {
            Debug.LogError("[HeightCalibration] Missing references (rigRoot/hmd/boardMid).");
            return;
        }

        float deltaY = boardMid.position.y - hmd.position.y;

        Vector3 p = rigRoot.position;
        p.y += deltaY;
        rigRoot.position = p;

        Debug.Log($"[HeightCalibration] Applied deltaY={deltaY:0.000}m. HMD is now at board mid height.");
    }
}