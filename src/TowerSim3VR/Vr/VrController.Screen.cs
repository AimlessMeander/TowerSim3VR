using System;
using UnityEngine;

namespace TowerSim3VR
{
    // The virtual screen (from NuclearesVR): the game's 2D interface is Screen Space - Overlay canvases that no
    // camera sees, so the monitor output is captured with ScreenCapture onto a quad in front of you. It shows
    // by itself while a menu or pop-up is open (Escape menu, settings, questions, errors), and X shows or hides
    // it at any time for the other 2D windows. While it shows, the monitor draws black behind the 2D interface
    // instead of the left eye, so the capture doesn't contain the screen itself.
    public partial class VrController
    {
        GameObject screenQuad;
        Material screenMaterial;
        RenderTexture captureTexture;
        bool screenVisible;
        bool screenToggled;
        bool menuOpen;
        float nextMenuCheck;

        static readonly Type[] PopupTypes =
        {
            typeof(PopupPause), typeof(PopupEndScreen), typeof(PopupError), typeof(PopupQuestion),
            typeof(PopupProgress), typeof(PopupEnterpass), typeof(PopupBugreport), typeof(PopupUpdate), typeof(PopupMic),
        };

        static bool MenuOrPopupOpen()
        {
            var windows = LegacyWindows.instance;
            if (windows != null
                && ((windows.win_pause != null && windows.win_pause.activeInHierarchy)
                    || (windows.win_settings != null && windows.win_settings.activeInHierarchy)))
            {
                return true;
            }
            foreach (var type in PopupTypes)
            {
                if (FindObjectOfType(type) != null) return true; // only finds active ones
            }
            return false;
        }

        // In Update.
        void UpdateScreen()
        {
            if (Time.unscaledTime >= nextMenuCheck)
            {
                nextMenuCheck = Time.unscaledTime + 0.2f;
                menuOpen = MenuOrPopupOpen();
            }
            bool want = source != null && body != null && (menuOpen || screenToggled);
            if (want == screenVisible) return;

            screenVisible = want;
            monitor.ShowEye = !want;
            if (want)
            {
                EnsureScreen();
                PlaceScreen();
            }
            if (screenQuad != null) screenQuad.SetActive(want);
        }

        void EnsureScreen()
        {
            if (captureTexture == null || captureTexture.width != Screen.width || captureTexture.height != Screen.height)
            {
                if (captureTexture != null) { captureTexture.Release(); Destroy(captureTexture); }
                captureTexture = new RenderTexture(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height), 0, RenderTextureFormat.ARGB32);
                captureTexture.Create();
                if (screenMaterial != null) screenMaterial.mainTexture = captureTexture;
            }
            if (screenQuad != null) return;

            screenQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screenQuad.name = "TowerSim3VR_Screen";
            screenQuad.layer = vrLayer;
            Destroy(screenQuad.GetComponent<Collider>());
            screenMaterial = new Material(Shader.Find("UI/Default")) { mainTexture = captureTexture, renderQueue = 3000 };
            // Always on top of the scene, so a desk or wall between you and it can't hide a menu.
            screenMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            var renderer = screenQuad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = screenMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            DrawOnTop(renderer, 0); // under the lasers, over the game's desk displays
            screenQuad.SetActive(false);
            Log.LogInfo($"Virtual screen set up ({Screen.width}x{Screen.height}, shader {screenMaterial.shader.name})");
        }

        // In front of where the head faces when it appears, then fixed there (relative to the body).
        void PlaceScreen()
        {
            screenQuad.transform.SetParent(body, false);
            var yaw = Quaternion.Euler(0f, head.localEulerAngles.y, 0f);
            screenQuad.transform.localPosition = head.localPosition
                + yaw * new Vector3(0f, -Plugin.ScreenDown.Value, Plugin.ScreenDistance.Value);
            screenQuad.transform.localRotation = yaw;
            float height = Plugin.ScreenHeight.Value;
            // Negative height: ScreenCapture's output is upside down on Direct3D.
            screenQuad.transform.localScale = new Vector3(height * Screen.width / Mathf.Max(1, Screen.height), -height, 1f);
        }

        // Where a ray meets the screen, as a Unity screen pixel (origin bottom left), and how far along the ray.
        bool TryHitScreen(Vector3 origin, Vector3 direction, out Vector2 pixel, out float distance)
        {
            pixel = Vector2.zero;
            distance = 0f;
            if (!screenVisible || screenQuad == null) return false;
            var quad = screenQuad.transform;
            var normal = quad.forward;
            var facing = Vector3.Dot(direction, normal);
            if (Mathf.Abs(facing) < 1e-4f) return false;
            distance = Vector3.Dot(quad.position - origin, normal) / facing;
            if (distance <= 0f) return false;
            var local = quad.InverseTransformPoint(origin + direction * distance);
            if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f) return false;
            // The quad's height is negative, so local +y is at the bottom.
            pixel = new Vector2((local.x + 0.5f) * Screen.width, (0.5f - local.y) * Screen.height);
            return true;
        }

        // End of frame, before submitting: the monitor output, 2D interface included.
        void CaptureScreen()
        {
            if (!screenVisible || captureTexture == null) return;
            try
            {
                if (captureTexture.width != Screen.width || captureTexture.height != Screen.height) EnsureScreen();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(captureTexture);
            }
            catch (Exception ex)
            {
                Log.LogError($"Screen capture error: {ex}");
            }
        }

        void DestroyScreen()
        {
            screenVisible = false;
            monitor.ShowEye = true;
            if (screenQuad != null) Destroy(screenQuad);
            screenQuad = null;
            if (captureTexture != null) { captureTexture.Release(); Destroy(captureTexture); }
            captureTexture = null;
        }

        // A layer no game camera renders, for the lasers and the screen; the eyes add it to their mask.
        static int PickUnusedLayer(int gameMask)
        {
            for (int layer = 31; layer >= 8; layer--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer)) && (gameMask & (1 << layer)) == 0) return layer;
            }
            return 31;
        }
    }
}
