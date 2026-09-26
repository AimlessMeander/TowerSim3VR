using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerSim3VR
{
    // The mod's own objects (lasers, the inactive hand's stub, the virtual screen, the binocular frame) are not drawn
    // by HDRP with the scene: however their sorting was set, the game's desk displays and parts of the scene still
    // drew over them (eye screenshots showed the laser missing in front of a monitor, and the stub and the frame not
    // at all). Instead they are drawn straight into each eye's image at the end of the frame, after HDRP has finished
    // and before the images go to SteamVR, with that eye camera's view and projection. The eye image's depth is
    // cleared first, so they are always on top; the lasers end at what they hit anyway.
    //
    // The GameObjects stay (their transforms place everything); their MeshRenderers are disabled so the scene
    // cameras skip them, and are drawn here in order of sortingOrder.
    public partial class VrController
    {
        readonly List<MeshRenderer> overlayRenderers = new List<MeshRenderer>();
        CommandBuffer overlayCommands;

        static int CompareOrder(MeshRenderer a, MeshRenderer b) => a.sortingOrder.CompareTo(b.sortingOrder);

        void RegisterOverlay(MeshRenderer renderer, int order)
        {
            renderer.enabled = false;
            renderer.sortingOrder = order;
            overlayRenderers.Add(renderer);
            overlayRenderers.Sort(CompareOrder);
        }

        // End of frame, before the screenshot and the submit.
        void DrawOverlays()
        {
            overlayRenderers.RemoveAll(r => r == null);
            if (leftEye == null || rightEye == null || overlayRenderers.Count == 0) return;
            if (overlayCommands == null) overlayCommands = new CommandBuffer { name = "TowerSim3VR overlays" };
            var cmd = overlayCommands;
            cmd.Clear();
            DrawOverlaysInto(cmd, leftEye, leftTex);
            DrawOverlaysInto(cmd, rightEye, rightTex);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        void DrawOverlaysInto(CommandBuffer cmd, Camera eye, RenderTexture target)
        {
            if (target == null) return;
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(true, false, Color.clear);
            var projection = eye.projectionMatrix;
            // The binocular frame is at the eyes, not in the world, so it is drawn without the binocular zoom
            // (which magnifies the eye projection); otherwise zooming in magnified the frame out of view.
            var unzoomed = projection;
            unzoomed.m00 /= zoom;
            unzoomed.m11 /= zoom;
            if (Plugin.OverlayFlip.Value)
            {
                var flip = Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
                projection = flip * projection;
                unzoomed = flip * unzoomed;
            }
            cmd.SetViewProjectionMatrices(eye.worldToCameraMatrix, projection);
            foreach (var renderer in overlayRenderers)
            {
                if (!renderer.gameObject.activeInHierarchy || renderer.sharedMaterial == null) continue;
                if (renderer.gameObject == binocularMask) cmd.SetViewProjectionMatrices(eye.worldToCameraMatrix, unzoomed);
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                cmd.DrawMesh(filter.sharedMesh, renderer.localToWorldMatrix, renderer.sharedMaterial, 0, 0);
            }
        }
    }
}
