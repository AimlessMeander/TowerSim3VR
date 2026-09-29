using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Valve.VR;

namespace TowerSim3VR
{
    // Performance: every frame the scene is rendered twice (once per eye).
    //  - ReduceEffects turns off, for the eyes only, screen-space global illumination (bounced light) and motion
    //    vectors (unused with SMAA and motion blur off). Measured on an RTX 4090 at 90 Hz: about 2 ms of 11 per frame,
    //    with no difference to be seen in the headset. (Volumetric clouds cost well under 1 ms; rendering the eyes at
    //    a lower resolution and upscaling looked poor, as the eyes are already at about the panel's resolution.)
    //  - Once a minute the GPU time per frame (from SteamVR) is logged, for performance reports.
    public partial class VrController
    {
        static readonly FrameSettingsField[] ReducedEffects =
        {
            FrameSettingsField.SSGI, FrameSettingsField.MotionVectors, FrameSettingsField.ObjectMotionVectors,
            FrameSettingsField.TransparentsWriteMotionVector,
        };

        float timingSince;
        uint lastTimedFrame;
        int timedFrames;
        float gpuTotal, gpuMax;

        // For each eye camera when it is made.
        void ApplyPerformance(HDAdditionalCameraData data)
        {
            if (!Plugin.ReduceEffects.Value) return;
            bool temporal = Plugin.EyeAntialiasing.Value == HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            foreach (var field in ReducedEffects)
            {
                if (temporal && field != FrameSettingsField.SSGI) continue; // TAA needs the motion vectors
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(field, false);
            }
        }

        void RestartTiming()
        {
            timingSince = Time.unscaledTime;
            timedFrames = 0;
            gpuTotal = gpuMax = 0f;
        }

        // After each submit: SteamVR's timing of the last finished frame.
        void SampleTiming()
        {
            var timing = new Compositor_FrameTiming { m_nSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Compositor_FrameTiming)) };
            if (!OpenVR.Compositor.GetFrameTiming(ref timing, 0) || timing.m_nFrameIndex == lastTimedFrame) return;
            lastTimedFrame = timing.m_nFrameIndex;
            float gpu = timing.m_flPreSubmitGpuMs + timing.m_flPostSubmitGpuMs;
            if (gpu > 0f)
            {
                timedFrames++;
                gpuTotal += gpu;
                gpuMax = Mathf.Max(gpuMax, gpu);
            }
            if (Time.unscaledTime - timingSince < 60f || timedFrames == 0) return;
            var error = ETrackedPropertyError.TrackedProp_Success;
            float hz = system.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref error);
            Log.LogInfo($"Performance: GPU {gpuTotal / timedFrames:0.0} ms per frame (max {gpuMax:0.0}) of {1000f / Mathf.Max(1f, hz):0.0} ms "
                + $"at {hz:0} Hz, RenderScale {Plugin.RenderScale.Value}, ReduceEffects {Plugin.ReduceEffects.Value}");
            RestartTiming();
        }
    }
}
