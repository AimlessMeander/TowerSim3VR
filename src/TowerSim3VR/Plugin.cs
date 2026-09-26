using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace TowerSim3VR
{
    public enum VrMode { Auto, Always, Never }

    [BepInPlugin("com.mjh.towersim3vr", "TowerSim3VR", "0.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static ConfigEntry<VrMode> Mode;
        internal static ConfigEntry<bool> YawOnly;
        internal static ConfigEntry<bool> LevelMovement;
        internal static ConfigEntry<float> RenderScale;
        internal static ConfigEntry<bool> FlipEyes;
        internal static ConfigEntry<bool> MonitorShowsEye;
        internal static ConfigEntry<bool> OverlayFlip;
        internal static ConfigEntry<HDAdditionalCameraData.AntialiasingMode> EyeAntialiasing;
        internal static ConfigEntry<float> MoveSpeed;
        internal static ConfigEntry<float> FastMoveMultiplier;
        internal static ConfigEntry<float> TurnSpeed;
        internal static ConfigEntry<float> LaserPitch;
        internal static ConfigEntry<float> RecenterHoldSeconds;
        internal static ConfigEntry<KeyCode> KeyRightStick;
        internal static ConfigEntry<float> MaxZoom;
        internal static ConfigEntry<float> ZoomSpeed;
        internal static ConfigEntry<float> BinocularSmoothing;
        internal static ConfigEntry<bool> BinocularFrame;
        internal static ConfigEntry<float> BinocularCircleDegrees;
        internal static ConfigEntry<KeyCode> KeyB;
        internal static ConfigEntry<float> ScreenDistance;
        internal static ConfigEntry<float> ScreenDown;
        internal static ConfigEntry<float> ScreenHeight;

        void Awake()
        {
            Log = Logger;
            SkipIntro.Enabled = Config.Bind("General", "SkipIntro", true, "Skip the intro video at startup.").Value;
            Mode = Config.Bind("General", "VrMode", VrMode.Auto,
                "When VR starts by itself on loading an airport (it stops when you leave). Auto: only if SteamVR is "
                + "already running. Always: also launches SteamVR. Never: only with Ctrl+Shift+V.");
            YawOnly = Config.Bind("Tracking", "YawOnly", true,
                "Ignore the game camera's pitch and roll so the horizon stays level; your head supplies them.");
            LevelMovement = Config.Bind("Tracking", "LevelMovement", true,
                "In VR, WASD moves level along where your head faces (Q/E up and down) instead of along the game camera's downward tilt.");
            RenderScale = Config.Bind("Rendering", "RenderScale", 1f,
                "Multiplier on SteamVR's recommended per-eye resolution. Applied when VR starts.");
            FlipEyes = Config.Bind("Rendering", "FlipEyes", true,
                "Flip the eye images vertically when sending them to SteamVR (Ctrl+Shift+F toggles).");
            EyeAntialiasing = Config.Bind("Rendering", "EyeAntialiasing", HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing,
                "Antialiasing for the eye cameras. TemporalAntialiasing blurs when you move your head. Applied when VR starts.");
            OverlayFlip = Config.Bind("Rendering", "OverlayFlip", false, "Flip the lasers, menu screen and binocular frame vertically in the eye images (Ctrl+Shift+O toggles), if they appear mirrored top to bottom.");
            MonitorShowsEye = Config.Bind("Rendering", "MonitorShowsEye", true,
                "While VR runs, the monitor shows the left eye instead of the game rendering its camera a third time (faster).");

            MoveSpeed = Config.Bind("Controllers", "MoveSpeed", 2f, "Left stick movement speed at full push, metres per second.");
            FastMoveMultiplier = Config.Bind("Controllers", "FastMoveMultiplier", 30f, "Speed multiplier while the left stick is clicked in.");
            TurnSpeed = Config.Bind("Controllers", "TurnSpeed", 45f, "Right stick smooth turn speed, degrees per second.");
            LaserPitch = Config.Bind("Controllers", "LaserPitch", 0f, "Tilts the lasers down (positive) or up (negative), degrees.");
            RecenterHoldSeconds = Config.Bind("Controllers", "RecenterHoldSeconds", 2f,
                "Holding both triggers this long recentres the view (0 = off). The End key also recentres.");
            KeyRightStick = Config.Bind("Controllers", "KeyRightStick", KeyCode.F1, "Key held while the right stick is clicked in (F1 = back to the desk view).");
            MaxZoom = Config.Bind("Controllers", "MaxZoom", 8f, "Largest binocular zoom (right stick while Y is held), times magnification.");
            ZoomSpeed = Config.Bind("Controllers", "ZoomSpeed", 1.5f, "How fast the binocular zoom changes at full stick, doublings per second.");
            BinocularSmoothing = Config.Bind("Binoculars", "Smoothing", 0.7f, "How much the zoomed view is steadied against head tremor, 0 (off) to 1 (strongest). Stronger with more zoom.");
            BinocularFrame = Config.Bind("Binoculars", "Frame", true, "Show a binocular frame (two circles) while zoomed.");
            BinocularCircleDegrees = Config.Bind("Binoculars", "CircleSize", 24f, "Radius of each circle of the frame, degrees of view. Applied when the frame is first shown.");
            KeyB = Config.Bind("Controllers", "KeyB", KeyCode.Escape, "Key held while B is held (Escape opens the game's menu).");
            ScreenDistance = Config.Bind("VirtualScreen", "Distance", 1.1f, "How far in front of you the 2D screen appears, metres.");
            ScreenDown = Config.Bind("VirtualScreen", "Down", 0.15f, "How far below eye level its centre is, metres.");
            ScreenHeight = Config.Bind("VirtualScreen", "Height", 0.8f, "Its height, metres (the width follows the window's shape).");

            // One patch class at a time, so one that no longer fits the game (after an update) doesn't stop the rest.
            var harmony = new Harmony("com.mjh.towersim3vr");
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"Patch {type.Name} failed: {ex.Message}");
                }
            }

            // BepInEx's own manager object doesn't get Update in this game, so the mod runs on its own object.
            var go = new GameObject("TowerSim3VR");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<VrController>();
        }
    }
}
