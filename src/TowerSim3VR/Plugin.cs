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
        internal static ConfigEntry<HDAdditionalCameraData.AntialiasingMode> EyeAntialiasing;

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
            MonitorShowsEye = Config.Bind("Rendering", "MonitorShowsEye", true,
                "While VR runs, the monitor shows the left eye instead of the game rendering its camera a third time (faster).");

            new Harmony("com.mjh.towersim3vr").PatchAll(typeof(Plugin).Assembly);

            // BepInEx's own manager object doesn't get Update in this game, so the mod runs on its own object.
            var go = new GameObject("TowerSim3VR");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<VrController>();
        }
    }
}
