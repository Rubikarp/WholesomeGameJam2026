using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace JeffGrawAssets.FlexibleUI
{ 
[ExecuteAlways]
public class BiRPUiCameraFix : MonoBehaviour
{
    private void Awake()
    {
        bool isURP;
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
        {
            isURP = false;
        }
        else
        {
            var assembly = pipeline.GetType().Assembly;
            isURP = assembly.GetName().Name.Contains("universal", StringComparison.InvariantCultureIgnoreCase);
        }

        if (isURP)
            return;

        var uiCam = GetComponent<Camera>();
        if (uiCam == null)
            return;

        uiCam.depth = 0;
        uiCam.clearFlags = CameraClearFlags.Depth;
    }
}
}
