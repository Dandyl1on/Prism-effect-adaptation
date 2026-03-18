using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PupilLabs
{
    public class EnableDuringCalibration : MonoBehaviour
    {

        public CalibrationController controller;
        public MeshRenderer renderer;

        void Awake()
        {
            controller.OnCalibrationStarted += EnableMePls;
            controller.OnCalibrationRoutineDone += DisableMePls;
        }

        void OnDestroy()
        {
            controller.OnCalibrationStarted -= EnableMePls;
            controller.OnCalibrationRoutineDone -= DisableMePls;
        }

        void EnableMePls()
        {
            renderer.enabled = true;
        }

        void DisableMePls()
        {
            renderer.enabled = false;
        }
    }
}
