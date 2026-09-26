namespace TowerSim3VR
{
    // The game's FSR3 upscaler (TND.FSR, on by default even at the lowest preset) adds custom passes and
    // dynamic resolution that are not XR aware. While VR runs, the in-memory setting is forced to Off; the
    // tower camera script (DeskCameraController) applies it every frame. The saved settings file is not touched.
    static class GraphicsOverrides
    {
        const string FsrField = "dd_gfx_fsr3";

        static object cfg;
        static int? savedFsr;

        // Called every frame while VR runs: re-applies after scene loads or the settings menu saving.
        internal static void Enforce()
        {
            var current = GameSettings.Current;
            var field = GameSettings.Field(FsrField);
            if (current == null || field == null) return;
            cfg = current;
            int value = (int)field.GetValue(cfg);
            if (value == 0) return;
            if (savedFsr == null) savedFsr = value;
            field.SetValue(cfg, 0);
            Plugin.Log.LogInfo($"FSR3 switched off for VR (was {value})");
        }

        internal static void Restore()
        {
            var field = GameSettings.Field(FsrField);
            if (savedFsr != null && cfg != null && field != null)
            {
                field.SetValue(cfg, savedFsr.Value);
                Plugin.Log.LogInfo($"FSR3 restored to {savedFsr.Value}");
            }
            savedFsr = null;
        }
    }
}
