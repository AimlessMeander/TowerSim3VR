using System;
using System.Collections;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Valve.VR;

namespace TowerSim3VR
{
    // Renders the headset view with two ordinary HDRP cameras, one per eye, into render textures and
    // submits them to SteamVR (the NuclearesVR approach). HDRP's own XR path can't be used here: the
    // game was built without XR, so its shaders lack the stereo variants (single-pass drew only the
    // left eye and scrambled instanced objects; multi-pass skips terrain).
    //
    // The eye cameras hang off a head rig of their own, placed each frame at the game camera's pose
    // plus the headset pose, so the game camera itself (controls, picking) is never moved.
    public class VrController : MonoBehaviour
    {
        static ManualLogSource Log => Plugin.Log;

        CVRSystem system;
        bool running;
        readonly TrackedDevicePose_t[] renderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        readonly TrackedDevicePose_t[] gamePoses = new TrackedDevicePose_t[0];

        Transform head;
        Camera source;
        Camera leftEye, rightEye;
        RenderTexture leftTex, rightTex;
        float eyeNear, eyeFar;
        readonly MonitorMirror monitor = new MonitorMirror();

        Vector3 zeroPosition;
        float zeroYaw;
        bool needsRecenter = true;
        bool loggedUpdate;

        void Awake()
        {
            SceneManager.sceneLoaded += (scene, mode) => Log.LogInfo($"Scene loaded: {scene.name} (#{scene.buildIndex}, {mode})");
            Application.onBeforeRender += OnBeforeRender;
            StartCoroutine(SubmitLoop());
            if (Plugin.AutoStart.Value)
            {
                StartCoroutine(AutoStart());
            }
            Log.LogInfo("TowerSim3VR loaded. Ctrl+Shift+V starts/stops VR, End recenters.");
        }

        IEnumerator AutoStart()
        {
            yield return new WaitForSecondsRealtime(Plugin.AutoStartDelay.Value);
            StartVr();
        }

        void Update()
        {
            if (!loggedUpdate)
            {
                loggedUpdate = true;
                Log.LogInfo("Controller running");
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool ctrlShift = kb.ctrlKey.isPressed && kb.shiftKey.isPressed;
                if (ctrlShift && kb.vKey.wasPressedThisFrame)
                {
                    if (running) StopVr(); else StartVr();
                }
                if (ctrlShift && kb.fKey.wasPressedThisFrame)
                {
                    Plugin.FlipEyes.Value = !Plugin.FlipEyes.Value;
                    Log.LogInfo($"FlipEyes = {Plugin.FlipEyes.Value}");
                }
                if (ctrlShift && kb.cKey.wasPressedThisFrame)
                {
                    DumpCameras();
                }
                if (kb.endKey.wasPressedThisFrame)
                {
                    needsRecenter = true;
                }
            }
            if (running)
            {
                GraphicsOverrides.Enforce();
                FramePacing.Enforce();
                FramePacing.LogStats();
            }
        }

        void StartVr()
        {
            if (running) return;
            var error = EVRInitError.None;
            system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);
            if (error != EVRInitError.None)
            {
                Log.LogError($"OpenVR init failed: {OpenVR.GetStringForHmdError(error)}. Is SteamVR running?");
                system = null;
                return;
            }
            OpenVR.Compositor.SetTrackingSpace(ETrackingUniverseOrigin.TrackingUniverseSeated);

            uint width = 0, height = 0;
            system.GetRecommendedRenderTargetSize(ref width, ref height);
            width = (uint)Mathf.Max(64, width * Plugin.RenderScale.Value);
            height = (uint)Mathf.Max(64, height * Plugin.RenderScale.Value);
            leftTex = CreateEyeTexture((int)width, (int)height, "Left");
            rightTex = CreateEyeTexture((int)width, (int)height, "Right");

            var rig = new GameObject("TowerSim3VR_Head");
            DontDestroyOnLoad(rig);
            head = rig.transform;

            running = true;
            needsRecenter = true;
            FramePacing.ResetStats();
            var hzError = ETrackedPropertyError.TrackedProp_Success;
            var hz = system.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd,
                ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref hzError);
            Log.LogInfo($"VR started: {width}x{height} per eye, {hz} Hz");
        }

        static RenderTexture CreateEyeTexture(int width, int height, string name)
        {
            var tex = new RenderTexture(width, height, 24, GraphicsFormat.R8G8B8A8_SRGB)
            {
                name = "TowerSim3VR_" + name,
                antiAliasing = 1,
            };
            tex.Create();
            return tex;
        }

        void StopVr()
        {
            if (!running) return;
            running = false;
            LevelMovement.Active = false;
            DestroyEyes();
            if (head != null) Destroy(head.gameObject);
            head = null;
            if (leftTex != null) leftTex.Release();
            if (rightTex != null) rightTex.Release();
            leftTex = rightTex = null;
            GraphicsOverrides.Restore();
            FramePacing.Restore();
            OpenVR.Shutdown();
            system = null;
            Log.LogInfo("VR stopped");
        }

        void BuildEyes(Camera main)
        {
            DestroyEyes();
            source = main;
            eyeNear = main.nearClipPlane;
            eyeFar = main.farClipPlane;
            leftEye = CreateEye(main, EVREye.Eye_Left, leftTex);
            rightEye = CreateEye(main, EVREye.Eye_Right, rightTex);
            // After the eyes are made, so they don't copy the hook from the game camera's HDRP data.
            if (Plugin.MonitorShowsEye.Value) monitor.Attach(main, leftTex);
            Log.LogInfo($"Eye cameras follow '{main.name}' (near {eyeNear}, far {eyeFar}, mask {main.cullingMask})");
        }

        Camera CreateEye(Camera main, EVREye eye, RenderTexture target)
        {
            var go = new GameObject("TowerSim3VR_" + eye);
            go.SetActive(false);
            go.transform.SetParent(head, false);

            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.targetTexture = target;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            // Unity's occlusion culling misjudges the off-centre frustum of an eye (see NuclearesVR).
            cam.useOcclusionCulling = false;

            var data = go.AddComponent<HDAdditionalCameraData>();
            if (main.TryGetComponent<HDAdditionalCameraData>(out var mainData))
            {
                mainData.CopyTo(data);
            }
            data.xrRendering = false;
            data.allowDynamicResolution = false;
            // Head movement is camera movement, so motion blur (the game turns it on whatever the menu says)
            // and temporal AA smear the whole view. Motion blur is switched off for the eyes only.
            data.customRenderingSettings = true;
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.MotionBlur] = true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.MotionBlur, false);
            data.antialiasing = Plugin.EyeAntialiasing.Value;

            // CopyFrom also copies the transform, so the eye offset goes on afterwards.
            system.GetEyeToHeadTransform(eye).ToUnity(out var eyePosition, out var eyeRotation);
            go.transform.localPosition = eyePosition;
            go.transform.localRotation = eyeRotation;
            ApplyProjection(cam, eye);

            go.SetActive(true);
            return cam;
        }

        void ApplyProjection(Camera cam, EVREye eye)
        {
            cam.nearClipPlane = eyeNear;
            cam.farClipPlane = eyeFar;
            cam.projectionMatrix = system.GetProjectionMatrix(eye, eyeNear, eyeFar).ToMatrix4x4();
        }

        void DestroyEyes()
        {
            monitor.Detach();
            if (leftEye != null) Destroy(leftEye.gameObject);
            if (rightEye != null) Destroy(rightEye.gameObject);
            leftEye = rightEye = null;
            source = null;
        }

        // Keep the eyes' settings in step with the game camera, which the game changes (views, zoom levels).
        void SyncEyes()
        {
            if (!Mathf.Approximately(source.nearClipPlane, eyeNear) || !Mathf.Approximately(source.farClipPlane, eyeFar))
            {
                eyeNear = source.nearClipPlane;
                eyeFar = source.farClipPlane;
                ApplyProjection(leftEye, EVREye.Eye_Left);
                ApplyProjection(rightEye, EVREye.Eye_Right);
            }
            foreach (var eye in new[] { leftEye, rightEye })
            {
                eye.cullingMask = source.cullingMask;
                eye.clearFlags = source.clearFlags;
                eye.backgroundColor = source.backgroundColor;
            }
        }

        // After LateUpdate (the game and Cinemachine have placed the camera), before rendering.
        void OnBeforeRender()
        {
            if (!running) return;
            try
            {
                // Blocks until SteamVR wants the next frame and returns the poses to render it with.
                OpenVR.Compositor.WaitGetPoses(renderPoses, gamePoses);

                var main = Camera.main;
                if (main != null && main != source)
                {
                    BuildEyes(main);
                }
                if (source == null) return;
                SyncEyes();

                var hmd = renderPoses[OpenVR.k_unTrackedDeviceIndex_Hmd];
                if (!hmd.bPoseIsValid) return;
                hmd.mDeviceToAbsoluteTracking.ToUnity(out var headPosition, out var headRotation);
                if (needsRecenter)
                {
                    zeroPosition = headPosition;
                    zeroYaw = headRotation.eulerAngles.y;
                    needsRecenter = false;
                    Log.LogInfo("Recentered");
                }
                var recenter = Quaternion.Euler(0f, -zeroYaw, 0f);
                var localPosition = recenter * (headPosition - zeroPosition);
                var localRotation = recenter * headRotation;

                var t = source.transform;
                var baseRotation = Plugin.YawOnly.Value ? Quaternion.Euler(0f, t.eulerAngles.y, 0f) : t.rotation;
                head.SetPositionAndRotation(t.position + baseRotation * localPosition, baseRotation * localRotation);
                LevelMovement.HeadYaw = head.eulerAngles.y;
                LevelMovement.Active = true;
            }
            catch (Exception ex)
            {
                Log.LogError($"VR frame error: {ex}");
            }
        }

        IEnumerator SubmitLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                if (!running) continue;
                try
                {
                    var bounds = Plugin.FlipEyes.Value
                        ? new VRTextureBounds_t { uMin = 0, vMin = 1, uMax = 1, vMax = 0 }
                        : new VRTextureBounds_t { uMin = 0, vMin = 0, uMax = 1, vMax = 1 };
                    var left = new Texture_t { handle = leftTex.GetNativeTexturePtr(), eType = ETextureType.DirectX, eColorSpace = EColorSpace.Auto };
                    var right = new Texture_t { handle = rightTex.GetNativeTexturePtr(), eType = ETextureType.DirectX, eColorSpace = EColorSpace.Auto };
                    var leftError = OpenVR.Compositor.Submit(EVREye.Eye_Left, ref left, ref bounds, EVRSubmitFlags.Submit_Default);
                    var rightError = OpenVR.Compositor.Submit(EVREye.Eye_Right, ref right, ref bounds, EVRSubmitFlags.Submit_Default);
                    if (leftError != EVRCompositorError.None || rightError != EVRCompositorError.None)
                    {
                        Log.LogWarning($"Submit: left {leftError}, right {rightError}");
                    }
                }
                catch (Exception ex)
                {
                    Log.LogError($"VR submit error: {ex}");
                }
            }
        }

        static void DumpCameras()
        {
            foreach (var c in Camera.allCameras)
            {
                Log.LogInfo($"Camera '{c.name}' depth={c.depth} target={(c.targetTexture ? c.targetTexture.name : "screen")} "
                    + $"pos={c.transform.position} rot={c.transform.eulerAngles} mask={c.cullingMask} main={c == Camera.main}");
            }
        }

        void OnDestroy()
        {
            Application.onBeforeRender -= OnBeforeRender;
            StopVr();
        }
    }
}
