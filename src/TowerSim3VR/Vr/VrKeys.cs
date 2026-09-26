using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TowerSim3VR
{
    // Keys, mouse buttons and the mouse position the controllers "press" on the game's behalf (the approach
    // from NuclearesVR). The game reads its controls through Unity's legacy Input (Input.GetKey, the push to
    // talk key, Escape, Input.mousePosition for every click), so the patches below add the controllers' state
    // on top of the real keyboard and mouse. Edges (down / up) are stamped with the frame the game will read
    // them in, since the controllers are sampled after the game's Update.
    internal static class VrKeys
    {
        static readonly HashSet<KeyCode> Held = new HashSet<KeyCode>();
        static readonly Dictionary<KeyCode, int> DownFrame = new Dictionary<KeyCode, int>();
        static readonly Dictionary<KeyCode, int> UpFrame = new Dictionary<KeyCode, int>();

        // Mouse buttons 0 (left) and 1 (right).
        static readonly bool[] ButtonHeld = new bool[2];
        static readonly int[] ButtonDownFrame = { -1, -1 };
        static readonly int[] ButtonUpFrame = { -1, -1 };

        /// <summary>
        /// While true, Input.mousePosition is <see cref="MousePosition"/> (Unity screen pixels, origin bottom
        /// left). When pointing into the world this is where the laser's target projects into the game camera,
        /// so everything the game derives from the mouse (rays, positions on the radar screens) lands there.
        /// </summary>
        internal static bool MouseOverride;
        internal static Vector2 MousePosition;

        /// <summary>
        /// Pointing into the 3D world: the 2D overlay canvases must not take the click, since the mouse
        /// position is then a projection of the laser, not something on the 2D screen.
        /// </summary>
        internal static bool WorldPointing;

        /// <summary>Extra "Mouse ScrollWheel" (radar zoom), from the right stick while pointing at a desk display.</summary>
        internal static float Scroll;

        /// <summary>
        /// The laser, for the UI event system while pointing into the world (see the EventSystem patch).
        /// </summary>
        internal static bool AimValid;
        internal static Vector3 AimOrigin;
        internal static Quaternion AimRotation = Quaternion.identity;
        internal static Transform AimCamera;

        internal static void Apply(HashSet<KeyCode> wanted)
        {
            var visibleFrame = Time.frameCount + 1;
            foreach (var key in wanted)
            {
                if (Held.Add(key)) DownFrame[key] = visibleFrame;
            }
            foreach (var key in new List<KeyCode>(Held))
            {
                if (!wanted.Contains(key))
                {
                    Held.Remove(key);
                    UpFrame[key] = visibleFrame;
                }
            }
        }

        internal static void SetMouseButton(int button, bool pressed)
        {
            if (ButtonHeld[button] == pressed) return;
            ButtonHeld[button] = pressed;
            if (pressed) ButtonDownFrame[button] = Time.frameCount + 1;
            else ButtonUpFrame[button] = Time.frameCount + 1;
        }

        internal static void Clear()
        {
            for (int i = 0; i < 2; i++)
            {
                ButtonHeld[i] = false;
                ButtonDownFrame[i] = ButtonUpFrame[i] = -1;
            }
            Held.Clear();
            DownFrame.Clear();
            UpFrame.Clear();
            MouseOverride = false;
            WorldPointing = false;
            Scroll = 0f;
            AimValid = false;
            AimCamera = null;
        }

        internal static bool IsHeld(KeyCode key) => Held.Contains(key);
        internal static bool WentDown(KeyCode key) => DownFrame.TryGetValue(key, out var f) && f == Time.frameCount;
        internal static bool WentUp(KeyCode key) => UpFrame.TryGetValue(key, out var f) && f == Time.frameCount;
        internal static bool ButtonIsHeld(int b) => b >= 0 && b < 2 && ButtonHeld[b];
        internal static bool ButtonWentDown(int b) => b >= 0 && b < 2 && ButtonDownFrame[b] == Time.frameCount;
        internal static bool ButtonWentUp(int b) => b >= 0 && b < 2 && ButtonUpFrame[b] == Time.frameCount;
    }

    [HarmonyPatch]
    internal static class InputPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetKey), typeof(KeyCode))]
        static void GetKey(KeyCode key, ref bool __result) { if (!__result && VrKeys.IsHeld(key)) __result = true; }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
        static void GetKeyDown(KeyCode key, ref bool __result) { if (!__result && VrKeys.WentDown(key)) __result = true; }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), typeof(KeyCode))]
        static void GetKeyUp(KeyCode key, ref bool __result) { if (!__result && VrKeys.WentUp(key)) __result = true; }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton))]
        static void MouseButton(int button, ref bool __result) { if (!__result && VrKeys.ButtonIsHeld(button)) __result = true; }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown))]
        static void MouseButtonDown(int button, ref bool __result) { if (!__result && VrKeys.ButtonWentDown(button)) __result = true; }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp))]
        static void MouseButtonUp(int button, ref bool __result) { if (!__result && VrKeys.ButtonWentUp(button)) __result = true; }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.mousePosition), MethodType.Getter)]
        static void MousePosition(ref Vector3 __result)
        {
            if (VrKeys.MouseOverride)
            {
                __result.x = VrKeys.MousePosition.x;
                __result.y = VrKeys.MousePosition.y;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Input), nameof(Input.GetAxis))]
        static void GetAxis(string axisName, ref float __result)
        {
            if (VrKeys.Scroll != 0f && axisName == "Mouse ScrollWheel") __result += VrKeys.Scroll;
        }

        // The desk panels (comms panel etc.) are world-space canvases, and Unity's GraphicRaycaster drops any
        // pointer outside its event camera's view. The laser's point projected into the game camera often is
        // (the game camera faces the airport, the panels are low on the desk), so clicks worked only sometimes.
        // While the event system runs, the game camera is put on the laser with the mouse at the centre of its
        // view (as NuclearesVR does for its tablet), then put back.
        internal struct CameraSwap
        {
            public bool Moved;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector2 Mouse;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(UnityEngine.EventSystems.EventSystem), "Update")]
        static void EventSystemPrefix(out CameraSwap __state)
        {
            __state = default;
            var camera = VrKeys.AimCamera;
            if (!VrKeys.AimValid || !VrKeys.WorldPointing || camera == null) return;
            __state = new CameraSwap { Moved = true, Position = camera.position, Rotation = camera.rotation, Mouse = VrKeys.MousePosition };
            camera.SetPositionAndRotation(VrKeys.AimOrigin, VrKeys.AimRotation);
            VrKeys.MousePosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.EventSystems.EventSystem), "Update")]
        static void EventSystemPostfix(CameraSwap __state)
        {
            if (!__state.Moved) return;
            var camera = VrKeys.AimCamera;
            if (camera != null) camera.SetPositionAndRotation(__state.Position, __state.Rotation);
            VrKeys.MousePosition = __state.Mouse;
        }

        // While pointing into the world, the 2D overlay (screen-space) canvases ignore the pointer; world-space
        // canvases, such as the radar screens' own buttons, still get it.
        [HarmonyPrefix, HarmonyPatch(typeof(GraphicRaycaster), nameof(GraphicRaycaster.Raycast),
            typeof(UnityEngine.EventSystems.PointerEventData), typeof(List<UnityEngine.EventSystems.RaycastResult>))]
        static bool GraphicRaycast(GraphicRaycaster __instance)
        {
            if (!VrKeys.WorldPointing) return true;
            var canvas = __instance.GetComponent<Canvas>();
            return canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay;
        }
    }
}
