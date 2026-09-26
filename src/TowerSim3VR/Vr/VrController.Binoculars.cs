using UnityEngine;

namespace TowerSim3VR
{
    // The binocular look while zoomed (Y held, right stick; the zoom itself is in VrController.Hands.cs):
    //  - Stabilising: magnified, the view shakes with every tremor of the head. While zoomed, the view's rotation
    //    follows the head through a low-pass filter that gets stronger with the zoom, so tremor is absorbed and
    //    deliberate turns still pan. At 1x the head is used as is.
    //  - A frame: a black mask with the classic two-circle opening, up while Y is held (drawn unzoomed). It hangs far in front
    //    of the eyes and draws over everything, so both eyes see it in the same place (no double image).
    public partial class VrController
    {
        Quaternion smoothedHead = Quaternion.identity;
        bool smoothing;
        GameObject binocularMask;
        float maskAlpha;
        Material binocularMaterial;

        const float MaskDistance = 50f;

        // In OnBeforeRender, on the head's local rotation.
        Quaternion StabiliseHead(Quaternion headRotation)
        {
            if (zoom <= 1.01f)
            {
                smoothing = false;
                return headRotation;
            }
            if (!smoothing)
            {
                smoothing = true;
                smoothedHead = headRotation;
            }
            // Filter rate (per second): quick at low zoom, slow at full zoom; BinocularSmoothing 0 turns it off.
            float strength = Mathf.Clamp01(Plugin.BinocularSmoothing.Value);
            float atFullZoom = Mathf.Lerp(30f, 1.5f, strength);
            float t = Mathf.InverseLerp(1f, Mathf.Max(1.01f, Plugin.MaxZoom.Value), zoom);
            float rate = Mathf.Lerp(30f, atFullZoom, Mathf.Sqrt(t));
            smoothedHead = Quaternion.Slerp(smoothedHead, headRotation, 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime));
            return smoothedHead;
        }

        // In Update.
        void UpdateBinocularMask()
        {
            // Up as soon as Y is held (as with real binoculars), until let go and the zoom is back to 1x.
            bool want = Plugin.BinocularFrame.Value && (Looking || zoom > 1.01f) && !screenVisible;
            maskAlpha = Mathf.MoveTowards(maskAlpha, want ? 1f : 0f, Time.unscaledDeltaTime / 0.2f);
            float alpha = maskAlpha;
            if (alpha <= 0f)
            {
                if (binocularMask != null && binocularMask.activeSelf) binocularMask.SetActive(false);
                return;
            }
            if (head == null) return;
            if (binocularMask == null) CreateBinocularMask();
            if (!binocularMask.activeSelf) binocularMask.SetActive(true);
            binocularMaterial.color = new Color(1f, 1f, 1f, alpha);
        }

        void CreateBinocularMask()
        {
            binocularMask = GameObject.CreatePrimitive(PrimitiveType.Quad);
            binocularMask.name = "TowerSim3VR_Binoculars";
            binocularMask.layer = vrLayer;
            Destroy(binocularMask.GetComponent<Collider>());
            // On the head the eyes hang from (the steadied one): SteamVR shows each frame as if rendered from the real
            // head, so whatever is fixed to the eye cameras is fixed to the display.
            binocularMask.transform.SetParent(head, false);
            binocularMask.transform.localPosition = new Vector3(0f, 0f, MaskDistance);
            binocularMask.transform.localRotation = Quaternion.identity;
            // Covers about +-80 degrees both ways, beyond the headset's field of view.
            float size = 2f * MaskDistance * Mathf.Tan(80f * Mathf.Deg2Rad);
            binocularMask.transform.localScale = new Vector3(size, size, 1f);

            if (binocularMaterial == null)
            {
                binocularMaterial = new Material(Shader.Find("UI/Default")) { mainTexture = MakeMaskTexture(size), renderQueue = 3102 };
                binocularMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            var renderer = binocularMask.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = binocularMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            RegisterOverlay(renderer, 5); // over the lasers too
        }

        // Black everywhere except two overlapping circles, sized as viewing angles at MaskDistance.
        static Texture2D MakeMaskTexture(float quadSize)
        {
            const int resolution = 1024;
            float radius = MaskDistance * Mathf.Tan(Plugin.BinocularCircleDegrees.Value * Mathf.Deg2Rad);
            float offset = radius * 0.45f; // the circles overlap by about half
            float softness = radius * 0.06f;
            var pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                float my = ((y + 0.5f) / resolution - 0.5f) * quadSize;
                for (int x = 0; x < resolution; x++)
                {
                    float mx = ((x + 0.5f) / resolution - 0.5f) * quadSize;
                    float left = Mathf.Sqrt((mx + offset) * (mx + offset) + my * my);
                    float right = Mathf.Sqrt((mx - offset) * (mx - offset) + my * my);
                    float edge = Mathf.Min(left, right) - radius; // < 0 inside a circle
                    byte a = (byte)(Mathf.Clamp01(edge / softness + 0.5f) * 255f);
                    pixels[y * resolution + x] = new Color32(0, 0, 0, a);
                }
            }
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = "TowerSim3VR_BinocularMask",
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
