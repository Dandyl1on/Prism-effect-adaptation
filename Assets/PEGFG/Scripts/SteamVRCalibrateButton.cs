using UnityEngine;
using Valve.VR;

public class SteamVRCalibrateButton : MonoBehaviour
{
    [Header("References")]
    public HeightCalibration calibration;

    [Header("SteamVR Input")]
    public SteamVR_Action_Boolean calibrateAction;
    public SteamVR_Input_Sources hand = SteamVR_Input_Sources.RightHand;

    void Update()
    {
        if (calibration == null || calibrateAction == null)
            return;

        if (calibrateAction.GetStateDown(hand))
            calibration.CalibrateHeight();
    }
}