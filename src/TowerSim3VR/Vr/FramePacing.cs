using HarmonyLib;
using UnityEngine;

namespace TowerSim3VR
{
    // While VR runs SteamVR sets the pace (WaitGetPoses blocks until the headset wants a frame), so the
    // game's own limits must not hold it back: Settings.LimitFPS applies its FPS limit every frame (a lower
    // background limit when the window isn't focused, which it often isn't with the headset on), and
    // the game turns on vsync to the monitor. Both are lifted while VR runs and restored afterwards.
    [HarmonyPatch(typeof(Settings), nameof(Settings.LimitFPS))]
    static class FramePacing
    {
        internal static bool Active;
        static int savedVSync = -1;

        static bool Prefix()
        {
            if (!Active) return true;
            if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
            return false;
        }

        // Called every frame while VR runs; the game reapplies vsync when its settings are applied.
        internal static void Enforce()
        {
            Active = true;
            if (QualitySettings.vSyncCount != 0)
            {
                if (savedVSync < 0) savedVSync = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
                Plugin.Log.LogInfo("VSync switched off for VR");
            }
            if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
        }

        internal static void Restore()
        {
            Active = false;
            if (savedVSync >= 0) QualitySettings.vSyncCount = savedVSync;
            savedVSync = -1;
        }
    }
}
