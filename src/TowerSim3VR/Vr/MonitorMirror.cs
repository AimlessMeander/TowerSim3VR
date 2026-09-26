using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TowerSim3VR
{
    // The game camera still has to exist and stay enabled (the game uses Camera.main), but rendering it
    // a third time just for the monitor cost a third of the frame. HDRP's customRender hook replaces a
    // camera's rendering with our own callback (culling still runs); it copies the left eye to the screen,
    // cropped to the window's shape. The game's 2D interface still draws on top as usual.
    sealed class MonitorMirror
    {
        HDAdditionalCameraData hooked;
        RenderTexture source;

        internal void Attach(Camera gameCamera, RenderTexture eye)
        {
            Detach();
            if (!gameCamera.TryGetComponent(out HDAdditionalCameraData data)) return;
            source = eye;
            hooked = data;
            hooked.customRender += Render;
            Plugin.Log.LogInfo($"Monitor shows the left eye instead of rendering '{gameCamera.name}' again");
        }

        internal void Detach()
        {
            if (hooked != null) hooked.customRender -= Render;
            hooked = null;
            source = null;
        }

        /// <summary>False while the virtual screen shows: black behind the 2D interface, so its capture doesn't contain itself.</summary>
        internal bool ShowEye = true;

        void Render(ScriptableRenderContext context, HDCamera hdCamera)
        {
            if (source == null) return;
            if (!ShowEye)
            {
                var clear = CommandBufferPool.Get("TowerSim3VR monitor");
                clear.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                clear.ClearRenderTarget(true, true, Color.black);
                context.ExecuteCommandBuffer(clear);
                CommandBufferPool.Release(clear);
                return;
            }
            // Crop the (roughly square) eye image to the window's aspect ratio rather than stretching it.
            float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float eyeAspect = (float)source.width / source.height;
            var scale = Vector2.one;
            if (screenAspect > eyeAspect) scale.y = eyeAspect / screenAspect;
            else scale.x = screenAspect / eyeAspect;
            var offset = new Vector2((1f - scale.x) / 2f, (1f - scale.y) / 2f);

            var cmd = CommandBufferPool.Get("TowerSim3VR monitor");
            cmd.Blit(source, BuiltinRenderTextureType.CameraTarget, scale, offset);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
