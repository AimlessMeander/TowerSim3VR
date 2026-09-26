using System;
using System.IO;
using UnityEngine;

namespace TowerSim3VR
{
    // Diagnostics for things that are created and active but don't show in the headset (the inactive hand's
    // marker, the binocular frame):
    //  - a grip press saves the left eye's image as a PNG next to the BepInEx log, to see what is really rendered;
    //  - every 5 s their Renderer.isVisible (false = culled before drawing) and placement are logged.
    public partial class VrController
    {
        bool previousGrip;
        bool screenshotWanted;
        int screenshotCount;
        float nextVisibilityLog;

        // In OnBeforeRender, after the controllers are read.
        void CheckScreenshotRequest()
        {
            bool grip = leftHand.Grip || rightHand.Grip;
            if (grip && !previousGrip) screenshotWanted = true;
            previousGrip = grip;
        }

        // End of frame, when the eye texture holds this frame.
        void SaveEyeScreenshot()
        {
            if (!screenshotWanted || leftTex == null) return;
            screenshotWanted = false;
            try
            {
                var previous = RenderTexture.active;
                RenderTexture.active = leftTex;
                var image = new Texture2D(leftTex.width, leftTex.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, leftTex.width, leftTex.height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                var path = Path.Combine(BepInEx.Paths.BepInExRootPath, $"TowerSim3VR_eye_{++screenshotCount}.png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Destroy(image);
                Log.LogInfo($"Saved the left eye's image to {path} (zoom {zoom:F1}, active hand {(activeHandIsRight ? "right" : "left")})");
                LogVisibility();
            }
            catch (Exception ex)
            {
                Log.LogError($"Eye screenshot failed: {ex}");
            }
        }

        // In Update.
        void UpdateVisibilityLog()
        {
            if (Time.unscaledTime < nextVisibilityLog) return;
            nextVisibilityLog = Time.unscaledTime + 5f;
            LogVisibility();
        }

        void LogVisibility()
        {
            var inactive = activeHandIsRight ? leftLaser : rightLaser;
            string stub = "none";
            if (inactive != null)
            {
                var beam = inactive.transform.GetChild(0).GetComponent<MeshRenderer>();
                stub = $"active {beam.gameObject.activeInHierarchy}, visible {beam.isVisible}, scale {beam.transform.lossyScale}, "
                    + $"material {(beam.sharedMaterial != null ? beam.sharedMaterial.name + "/" + beam.sharedMaterial.shader.name : "null")}, "
                    + $"layer {beam.gameObject.layer}, order {beam.sortingOrder}";
            }
            string mask = "none";
            if (binocularMask != null)
            {
                var renderer = binocularMask.GetComponent<MeshRenderer>();
                mask = $"active {binocularMask.activeInHierarchy}, visible {renderer.isVisible}, alpha {binocularMaterial.color.a:F2}, "
                    + $"texture {(binocularMaterial.mainTexture != null)}, layer {binocularMask.layer}, order {renderer.sortingOrder}";
            }
            Log.LogInfo($"Visibility: inactive hand stub [{stub}]; binocular frame [{mask}]; zoom {zoom:F2}; "
                + $"eye mask {(leftEye != null ? leftEye.cullingMask : 0)}");
        }
    }
}
