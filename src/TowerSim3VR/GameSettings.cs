using System.Linq;
using System.Reflection;

namespace TowerSim3VR
{
    // The game's settings object (Settings.<obfuscated static property>, a class holding fields such as
    // dd_gfx_fsr3 and key_push_to_talk). The property's name changes between builds, so it is found by type.
    static class GameSettings
    {
        static PropertyInfo property;

        internal static object Current
        {
            get
            {
                if (property == null)
                {
                    property = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(p => p.PropertyType.GetField("dd_gfx_fsr3") != null);
                    if (property == null) return null;
                }
                try { return property.GetValue(null); } catch { return null; }
            }
        }

        internal static FieldInfo Field(string name) => Current?.GetType().GetField(name);

        internal static T Get<T>(string name, T fallback)
        {
            var cfg = Current;
            var field = cfg?.GetType().GetField(name);
            return field != null && field.GetValue(cfg) is T value ? value : fallback;
        }
    }
}
