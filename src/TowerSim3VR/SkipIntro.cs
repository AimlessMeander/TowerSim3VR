using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TowerSim3VR
{
    // The intro scene's Intro component plays the video, waits 3 s, then loads the menu as soon as
    // the video isn't playing. Not starting the video and zeroing the wait makes the game's own code
    // load the menu on the first frame. Survives game updates, unlike editing the asset files.
    [HarmonyPatch(typeof(Intro))]
    static class SkipIntro
    {
        internal static bool Enabled;

        [HarmonyPrefix]
        [HarmonyPatch("Awake")]
        static bool AwakePrefix(Intro __instance)
        {
            if (!Enabled) return true;
            __instance.player.enabled = false;
            Plugin.Log.LogInfo("Intro video skipped");
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        static void StartPostfix(Intro __instance)
        {
            if (!Enabled) return;
            // The wait timer is the class's only float field; its name is obfuscated and changes between builds.
            var timer = typeof(Intro).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(f => f.FieldType == typeof(float));
            timer?.SetValue(__instance, 0f);
        }
    }
}
