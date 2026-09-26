using HarmonyLib;
using UnityEngine;

namespace TowerSim3VR
{
    // Diagnostics for the radio voice (text to speech) staying silent. TowerSpeak holds the voice back
    // (FeelthereTTS.locked) while push to talk is active or has just been released (mm_shift > -1.6), while game
    // speed is 0 or time is stopped, or while speech is being processed; in microphone test mode it stops
    // updating the lock at all. Logged every 5 s while an airport is loaded, VR or not, only when it changes.
    static class RadioDiagnostics
    {
        static readonly System.Reflection.FieldInfo TestModeField = AccessTools.Field(typeof(TowerSpeak), "GJPNMMOJBJL");
        static float nextLog;
        static string last;

        internal static void Update()
        {
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 5f;
            var game = Game.instance;
            var speak = TowerSpeak.instance;
            if (game == null || speak == null) return;
            var testMode = TestModeField != null && TestModeField.GetValue(speak) is bool b && b;
            var line = $"locked {FeelthereTTS.locked}, ptt {speak.mm_shift:F1}, processing {speak.processing_timer:F1}, "
                + $"test mode {testMode}, speed {game.speedup}, timeScale {Time.timeScale}";
            // Compare without the ever-changing ptt countdown, so it is only logged when something else changes.
            var key = $"{FeelthereTTS.locked}|{speak.mm_shift > -1.6f}|{speak.isProcessing()}|{testMode}|{game.speedup}|{Time.timeScale}";
            if (key == last) return;
            last = key;
            Plugin.Log.LogInfo($"Radio: {line}");
        }
    }
}
