using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TowerSim3VR
{
    // Brightness in the headset. The game sets its automatic exposure's compensation itself (1.5-2.1 EV, by weather),
    // and the headset's picture came out too bright and washed out. So the eyes' exposure compensation is shifted by
    // the Brightness setting (EV; negative is darker), on top of whatever the game sets: after HDRP has blended an eye
    // camera's volumes each frame, the offset is added to that eye's result. Ctrl+Shift+Up/Down changes it in steps
    // of 0.25 while playing, and the value is saved to the settings.
    public partial class VrController
    {
        const float BrightnessStep = 0.25f;

        // In Update while VR runs.
        void UpdateBrightness()
        {
            EyeExposurePatch.Offset = Plugin.Brightness.Value;
            EyeExposurePatch.Stacks.Clear();
            if (EyeExposurePatch.Offset != 0f)
            {
                foreach (var eye in new[] { leftEye, rightEye })
                {
                    var stack = eye != null ? HDCamera.GetOrCreate(eye).volumeStack : null;
                    if (stack != null) EyeExposurePatch.Stacks.Add(stack);
                }
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed || !kb.shiftKey.isPressed) return;
            float change = kb.upArrowKey.wasPressedThisFrame ? BrightnessStep : kb.downArrowKey.wasPressedThisFrame ? -BrightnessStep : 0f;
            if (change == 0f) return;
            Plugin.Brightness.Value = Mathf.Clamp(Plugin.Brightness.Value + change, -4f, 4f);
            Log.LogInfo($"Brightness {Plugin.Brightness.Value:+0.00;-0.00;0} EV");
        }

        void StopBrightness() => EyeExposurePatch.Stacks.Clear();
    }

    [HarmonyPatch(typeof(VolumeManager), nameof(VolumeManager.Update), typeof(VolumeStack), typeof(Transform), typeof(LayerMask))]
    static class EyeExposurePatch
    {
        internal static readonly List<VolumeStack> Stacks = new List<VolumeStack>();
        internal static float Offset;
        // What was last written per stack: VolumeManager skips stacks that need no update, and the offset must not be
        // added twice to a value it left as it was.
        static readonly Dictionary<VolumeStack, float> written = new Dictionary<VolumeStack, float>();

        static void Postfix(VolumeStack stack)
        {
            if (Offset == 0f || !Stacks.Contains(stack)) return;
            var exposure = stack.GetComponent<Exposure>();
            if (exposure == null) return;
            var compensation = exposure.compensation;
            if (written.TryGetValue(stack, out var last) && compensation.value == last) return;
            compensation.value += Offset;
            written[stack] = compensation.value;
        }
    }
}
