using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TowerSim3VR
{
    // FreeCamController.Update moves the camera target by camera.TransformDirection(movespeed): along the
    // game camera's full rotation, pitched down, while the headset view is level (YawOnly). So W moved
    // forward and down. In VR the step is redirected level along where the head faces (yaw only), with
    // the same length; Q/E (movespeed.y) stay vertical. Other target changes in the same frame (jumps
    // to an aircraft, action-cam momentum) don't match the key step and are left alone.
    [HarmonyPatch(typeof(FreeCamController), "Update")]
    static class LevelMovement
    {
        internal static bool Active;
        internal static float HeadYaw;

        static readonly FieldInfo DeskField = typeof(FreeCamController)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(f => f.FieldType == typeof(DeskCameraController));

        static void Prefix(FreeCamController __instance, out Vector3 __state)
        {
            var desk = Desk(__instance);
            __state = desk != null ? desk.target_pos : Vector3.zero;
        }

        static void Postfix(FreeCamController __instance, Vector3 __state)
        {
            if (!Active || !Plugin.LevelMovement.Value) return;
            var desk = Desk(__instance);
            if (desk == null) return;
            var move = desk.movespeed;
            var step = desk.target_pos - __state;
            if (move.sqrMagnitude < 1e-8f || step.sqrMagnitude < 1e-10f) return;

            // Only the key-movement step: parallel to the direction the game computed it along.
            var gameDirection = __instance.transform.TransformDirection(move).normalized;
            if (Vector3.Dot(step.normalized, gameDirection) < 0.999f) return;

            var level = Quaternion.Euler(0f, HeadYaw, 0f) * move.normalized;
            desk.target_pos = __state + level * step.magnitude;
        }

        static DeskCameraController Desk(FreeCamController cam) => DeskField?.GetValue(cam) as DeskCameraController;
    }
}
