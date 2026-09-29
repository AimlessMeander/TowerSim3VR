using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace TowerSim3VR
{
    // Temporary test: Ctrl+Shift+T leaves out all transparent objects in the headset (about 2 of 8 ms per frame on an
    // RTX 4090), to see whether the game is playable without them; the first time it logs the transparent objects
    // within 60 m, to find the costly ones.
    public partial class VrController
    {
        bool transparentsOff;

        void ToggleTransparents()
        {
            transparentsOff = !transparentsOff;
            foreach (var eye in new[] { leftEye, rightEye })
            {
                if (eye == null || !eye.TryGetComponent<HDAdditionalCameraData>(out var data)) continue;
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.TransparentObjects] = transparentsOff;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.TransparentObjects, !transparentsOff);
            }
            Log.LogInfo($"Transparent objects {(transparentsOff ? "off" : "on")}");
            if (!transparentsOff || head == null) return;
            var near = new SortedDictionary<string, int>();
            foreach (var renderer in FindObjectsOfType<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMaterial == null) continue;
                if (renderer.sharedMaterial.renderQueue < 2501) continue; // transparent queues
                if (renderer.bounds.SqrDistance(head.position) > 60f * 60f) continue;
                string key = $"{renderer.name} [{renderer.sharedMaterial.shader.name}, queue {renderer.sharedMaterial.renderQueue}]";
                near.TryGetValue(key, out int count);
                near[key] = count + 1;
            }
            Log.LogInfo($"Transparent objects within 60 m ({near.Count} kinds):");
            foreach (var pair in near) Log.LogInfo($"  {pair.Value} x {pair.Key}");
        }

        void UpdateTransparentsKey()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.ctrlKey.isPressed && kb.shiftKey.isPressed && kb.tKey.wasPressedThisFrame) ToggleTransparents();
        }
    }
}
