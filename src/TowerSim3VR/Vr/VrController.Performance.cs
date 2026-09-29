using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace TowerSim3VR
{
    // Performance: every frame the scene is rendered twice (once per eye). ReduceEffects turns off, for the eyes only,
    // screen-space global illumination, ambient occlusion, subsurface scattering and transmission, and motion vectors
    // (unused with SMAA and motion blur off). Measured on an RTX 4090 at 90 Hz: 11 ms per frame down to about 8, and
    // it looked better in the headset (screen-space effects computed separately per eye don't quite match).
    // Measured and not worth it: volumetric clouds and shadow distance (well under 1 ms each), the separate
    // post-processing effects (each under 0.5 ms), the tower's layered window glass (no measurable difference; the
    // transparent cost, about 2 ms, is mostly the desk displays), and rendering the eyes at a lower resolution with upscaling
    // (looked poor: the eyes are already at about the panel's resolution).
    // Don't call IVRCompositor.GetFrameTiming every frame: with Steam Link it made SteamVR stop handing out frames
    // (WaitGetPoses never returned) after a couple of dozen frames.
    public partial class VrController
    {
        static readonly FrameSettingsField[] ReducedEffects =
        {
            FrameSettingsField.SSGI, FrameSettingsField.SSAO, FrameSettingsField.SubsurfaceScattering, FrameSettingsField.Transmission,
        };
        static readonly FrameSettingsField[] MotionVectorFields =
        {
            FrameSettingsField.MotionVectors, FrameSettingsField.ObjectMotionVectors, FrameSettingsField.TransparentsWriteMotionVector,
        };

        // For each eye camera when it is made.
        void ApplyPerformance(HDAdditionalCameraData data)
        {
            if (!Plugin.ReduceEffects.Value) return;
            bool temporal = Plugin.EyeAntialiasing.Value == HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            foreach (var field in ReducedEffects) TurnOff(data, field);
            if (!temporal) foreach (var field in MotionVectorFields) TurnOff(data, field); // TAA needs them
        }

        static void TurnOff(HDAdditionalCameraData data, FrameSettingsField field)
        {
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
            data.renderingPathCustomFrameSettings.SetEnabled(field, false);
        }
    }
}
