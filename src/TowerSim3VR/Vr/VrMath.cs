using UnityEngine;
using Valve.VR;

namespace TowerSim3VR
{
    // Conversions between OpenVR's matrix structs and Unity types (from NuclearesVR).
    internal static class VrMath
    {
        public static Matrix4x4 ToMatrix4x4(this HmdMatrix44_t src)
        {
            var m = new Matrix4x4();
            m.m00 = src.m0; m.m01 = src.m1; m.m02 = src.m2; m.m03 = src.m3;
            m.m10 = src.m4; m.m11 = src.m5; m.m12 = src.m6; m.m13 = src.m7;
            m.m20 = src.m8; m.m21 = src.m9; m.m22 = src.m10; m.m23 = src.m11;
            m.m30 = src.m12; m.m31 = src.m13; m.m32 = src.m14; m.m33 = src.m15;
            return m;
        }

        // OpenVR is right-handed with forward = -Z, Unity left-handed with forward = +Z:
        // flip Z on both sides of the rotation, and on the position.
        public static void ToUnity(this HmdMatrix34_t src, out Vector3 position, out Quaternion rotation)
        {
            position = new Vector3(src.m3, src.m7, -src.m11);
            var forward = new Vector3(-src.m2, -src.m6, src.m10);
            var up = new Vector3(src.m1, src.m5, -src.m9);
            rotation = Quaternion.LookRotation(forward, up);
        }
    }
}
