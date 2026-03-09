using UnityEngine;
using Valve.VR;
using System.Collections;

public class SteamVRConfirmInput : MonoBehaviour
{
    [Header("SteamVR Action")]
    public SteamVR_Action_Boolean confirmAction; // set to SteamVR_Actions.default_Trigger (or your action name)

    [Header("Which hand(s) can confirm)")]
    public bool allowLeft = true;
    public bool allowRight = true;

    public bool GetConfirmDown()
    {
        if (confirmAction == null) return false;

        bool down = false;
        if (allowLeft)  down |= confirmAction.GetStateDown(SteamVR_Input_Sources.LeftHand);
        if (allowRight) down |= confirmAction.GetStateDown(SteamVR_Input_Sources.RightHand);
        return down;
    }

    public bool GetConfirmHeld()
    {
        if (confirmAction == null) return false;

        bool held = false;
        if (allowLeft)  held |= confirmAction.GetState(SteamVR_Input_Sources.LeftHand);
        if (allowRight) held |= confirmAction.GetState(SteamVR_Input_Sources.RightHand);
        return held;
    }
}