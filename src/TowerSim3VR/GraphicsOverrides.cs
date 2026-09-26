using System.Linq;
using System.Reflection;

namespace TowerSim3VR
{
    // The game's FSR3 upscaler (TND.FSR, on by default even at the lowest preset) adds custom passes and
    // dynamic resolution that are not XR aware: in the headset only the left eye was drawn and scenery
    // smeared with head movement. While VR runs, the in-memory setting is forced to Off; the tower camera
    // script (DeskCameraController) applies it every frame. The saved settings file is not touched.
    static class GraphicsOverrides
    {
        const string FsrField = "dd_gfx_fsr3";

        static FieldInfo fsrField;
        static object cfg;
        static int? savedFsr;

        // Settings exposes its config through an obfuscated static property; find it by the field it holds.
        static object Cfg()
        {
            var prop = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(p => p.PropertyType.GetField(FsrField) != null);
            if (prop == null) return null;
            fsrField = prop.PropertyType.GetField(FsrField);
            try { return prop.GetValue(null); } catch { return null; }
        }

        // Called every frame while VR runs: re-applies after scene loads or the settings menu saving.
        internal static void Enforce()
        {
            var current = Cfg();
            if (current == null) return;
            cfg = current;
            int value = (int)fsrField.GetValue(cfg);
            if (value == 0) return;
            if (savedFsr == null) savedFsr = value;
            fsrField.SetValue(cfg, 0);
            Plugin.Log.LogInfo($"FSR3 switched off for VR (was {value})");
        }

        internal static void Restore()
        {
            if (savedFsr != null && cfg != null)
            {
                fsrField.SetValue(cfg, savedFsr.Value);
                Plugin.Log.LogInfo($"FSR3 restored to {savedFsr.Value}");
            }
            savedFsr = null;
        }
    }
}
