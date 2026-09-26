using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using Valve.VR;

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

        // Logs every 5 s how the game kept up with the headset, from SteamVR's own counters.
        static float nextLog;
        static int frames;
        static Compositor_CumulativeStats last;

        internal static void LogStats()
        {
            frames++;
            if (Time.unscaledTime < nextLog) return;
            var stats = new Compositor_CumulativeStats();
            OpenVR.Compositor.GetCumulativeStats(ref stats, (uint)Marshal.SizeOf(typeof(Compositor_CumulativeStats)));
            var timing = new Compositor_FrameTiming { m_nSize = (uint)Marshal.SizeOf(typeof(Compositor_FrameTiming)) };
            OpenVR.Compositor.GetFrameTiming(ref timing, 0);
            if (nextLog > 0f)
            {
                // GPU ms is SteamVR's measure of the game's GPU work for the last frame; frame ms is the
                // game's whole frame. A GPU figure near the frame time means the GPU is the limit.
                Plugin.Log.LogInfo($"Perf: {frames / 5f:F0} fps, frame {Time.unscaledDeltaTime * 1000f:F1} ms, "
                    + $"GPU {timing.m_flTotalRenderGpuMs:F1} ms, presented {stats.m_nNumFramePresents - last.m_nNumFramePresents}, "
                    + $"dropped {stats.m_nNumDroppedFrames - last.m_nNumDroppedFrames}, "
                    + $"reprojected {stats.m_nNumReprojectedFrames - last.m_nNumReprojectedFrames} (last 5 s)");
            }
            last = stats;
            frames = 0;
            nextLog = Time.unscaledTime + 5f;
        }

        internal static void ResetStats()
        {
            nextLog = 0f;
            frames = 0;
        }
    }
}
