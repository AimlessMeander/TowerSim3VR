using HarmonyLib;

namespace TowerSim3VR
{
    // A game bug, not a VR one (it happens unmodded too): the radio voice stays silent for a whole session.
    // FeelthereTTS's speaking thread starts speaking about a second after the airport loads. If the separate
    // TTS program hasn't connected by then (it now takes ~24 s here), the first call (LAW921 asking for push and
    // start) is sent to no one, and the thread then waits for its audio in a loop that never exits
    // (`num--; if (num == 0) LogError("* data id not arrived")`, no break). Nothing is spoken again.
    //
    // Fix: on that thread, each tick right before it decides whether to take a message, keep it locked and its
    // start delay topped up until the TTS program has reported ready (NOINFNKBKGF.readytts). The first call then
    // waits in the queue and is spoken once the voice is up. CCLAKPDFKHO, AOIFJPMGAHN and NOINFNKBKGF are
    // obfuscated names that may change with a game update; the patch then fails to apply and is logged.
    [HarmonyPatch(typeof(FeelthereTTS), "CCLAKPDFKHO")]
    static class RadioStartFix
    {
        static readonly AccessTools.FieldRef<FeelthereTTS, float> StartDelay =
            AccessTools.FieldRefAccess<FeelthereTTS, float>("AOIFJPMGAHN");
        static bool loggedWaiting, loggedReady;

        static void Postfix(FeelthereTTS __instance)
        {
            var bridge = NOINFNKBKGF.instance;
            if (bridge != null && bridge.readytts)
            {
                if (loggedWaiting && !loggedReady)
                {
                    loggedReady = true;
                    Plugin.Log.LogInfo("Radio voice ready - queued calls will now be spoken");
                }
                return;
            }
            if (!loggedWaiting)
            {
                loggedWaiting = true;
                Plugin.Log.LogInfo("Radio voice not ready yet - holding radio calls until it is");
            }
            FeelthereTTS.locked = true;
            if (StartDelay(__instance) < 1f) StartDelay(__instance) = 1f;
        }
    }
}
