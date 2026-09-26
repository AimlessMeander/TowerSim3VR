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
    public partial class VrController : MonoBehaviour
    {
        static ManualLogSource Log => Plugin.Log;

        CVRSystem system;
        bool running;
        readonly TrackedDevicePose_t[] renderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        readonly TrackedDevicePose_t[] gamePoses = new TrackedDevicePose_t[0];

        // body: the game camera's pose (level unless YawOnly is off); head: the headset relative to it.
        Transform body, head;
        int vrLayer = -1;
        const ETrackingUniverseOrigin TrackingSpace = ETrackingUniverseOrigin.TrackingUniverseSeated;
        Camera source;
        Camera leftEye, rightEye;
        RenderTexture leftTex, rightTex;
        float eyeNear, eyeFar;
        readonly MonitorMirror monitor = new MonitorMirror();

        Vector3 zeroPosition;
        float zeroYaw;
        bool needsRecenter = true;
        bool loggedUpdate;

        // Automatic start/stop, as in NuclearesVR: VR starts once per airport load when the game is fully
        // loaded (and, in Auto mode, SteamVR is already running), and stops once the game has been left
        // for LeaveGameGraceSeconds, so menus are an ordinary desktop window.
        const float LeaveGameGraceSeconds = 1.5f;
        bool decidedThisGame;
        bool shuttingDown;
        float notInGameSince = -1f;

        void Awake()
        {
            SceneManager.sceneLoaded += (scene, mode) => Log.LogInfo($"Scene loaded: {scene.name} (#{scene.buildIndex}, {mode})");
            Application.onBeforeRender += OnBeforeRender;
            StartCoroutine(SubmitLoop());
            Log.LogInfo($"TowerSim3VR loaded (VrMode {Plugin.Mode.Value}). Ctrl+Shift+V starts/stops VR, End recenters.");
        }

        // The same test the game's camera controller uses before it lets the camera move.
        static bool InGame()
        {
            var game = Game.instance;
            return game != null && !game.loading && game.loadingcnt >= Game.LOAD_FINISHED;
        }

        // Starting OpenVR launches SteamVR if it isn't running, which nobody wants when playing on the monitor.
        static bool VrWanted(out string reason)
        {
            reason = "";
            switch (Plugin.Mode.Value)
            {
                case VrMode.Never:
                    reason = "VrMode is Never";
                    return false;
                case VrMode.Always:
                    return true;
                default:
                    try
                    {
                        if (System.Diagnostics.Process.GetProcessesByName("vrserver").Length > 0) return true;
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"Could not check whether SteamVR is running ({ex.Message}); assuming it is");
                        return true;
                    }
                    reason = "SteamVR is not running (VrMode Always would launch it)";
                    return false;
            }
        }

        void UpdateAutoStart()
        {
            if (InGame())
            {
                notInGameSince = -1f;
                if (!decidedThisGame && !running && !shuttingDown)
                {
                    decidedThisGame = true;
                    if (VrWanted(out var reason))
                    {
                        Log.LogInfo("Airport loaded - starting VR");
                        StartVr();
                    }
                    else
                    {
                        Log.LogInfo($"Airport loaded - not starting VR: {reason}");
                    }
                }
                return;
            }

            if (notInGameSince < 0f)
            {
                notInGameSince = Time.unscaledTime;
            }
            else if (Time.unscaledTime - notInGameSince > LeaveGameGraceSeconds)
            {
                decidedThisGame = false;
                if (running)
                {
                    Log.LogInfo("Left the airport - leaving VR");
                    StopVr();
                }
            }
        }

        void Update()
        {
            if (!loggedUpdate)
            {
                loggedUpdate = true;
                Log.LogInfo("Controller running");
            }
            UpdateAutoStart();
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool ctrlShift = kb.ctrlKey.isPressed && kb.shiftKey.isPressed;
                if (ctrlShift && kb.vKey.wasPressedThisFrame)
                {
                    if (running) StopVr(); else if (!shuttingDown) StartVr();
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
                UpdateSticks();
                UpdateScreen();
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
            OpenVR.Compositor.SetTrackingSpace(TrackingSpace);

            uint width = 0, height = 0;
            system.GetRecommendedRenderTargetSize(ref width, ref height);
            width = (uint)Mathf.Max(64, width * Plugin.RenderScale.Value);
            height = (uint)Mathf.Max(64, height * Plugin.RenderScale.Value);
            leftTex = CreateEyeTexture((int)width, (int)height, "Left");
            rightTex = CreateEyeTexture((int)width, (int)height, "Right");

            var rig = new GameObject("TowerSim3VR_Body");
            DontDestroyOnLoad(rig);
            body = rig.transform;
            head = new GameObject("TowerSim3VR_Head").transform;
            head.SetParent(body, false);
            InitInput();
            DetectUiInput();

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

        // Order matters (learned in NuclearesVR): stop submitting, shut OpenVR down, and only then free the
        // eye textures - freeing them while the compositor may still read them crashes the graphics driver.
        void StopVr(bool immediate = false)
        {
            if (!running) return;
            running = false;
            LevelMovement.Active = false;
            ReleaseInput();
            DestroyEyes();
            DestroyScreen();
            if (body != null) Destroy(body.gameObject); // the head, eyes, lasers and screen are under it
            body = head = null;
            GraphicsOverrides.Restore();
            FramePacing.Restore();
            if (immediate)
            {
                OpenVR.Shutdown();
                system = null;
                ReleaseEyeTextures();
                return;
            }
            shuttingDown = true;
            StartCoroutine(FinishShutdown());
        }

        IEnumerator FinishShutdown()
        {
            // Let the camera destruction and any in-flight frame finish.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            try
            {
                OpenVR.Shutdown();
            }
            catch (Exception ex)
            {
                Log.LogError($"OpenVR.Shutdown threw: {ex}");
            }
            system = null;
            yield return null;
            yield return null;
            ReleaseEyeTextures();
            shuttingDown = false;
            Log.LogInfo("VR stopped");
        }

        void ReleaseEyeTextures()
        {
            if (leftTex != null) { leftTex.Release(); Destroy(leftTex); }
            if (rightTex != null) { rightTex.Release(); Destroy(rightTex); }
            leftTex = rightTex = null;
        }

        void BuildEyes(Camera main)
        {
            DestroyEyes();
            source = main;
            if (vrLayer < 0)
            {
                vrLayer = PickUnusedLayer(main.cullingMask);
                Log.LogInfo($"Lasers and screen on layer {vrLayer}");
            }
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
                eye.cullingMask = source.cullingMask | (1 << vrLayer);
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
                TrackingToLocal(headPosition, headRotation, out var localPosition, out var localRotation);

                var t = source.transform;
                var baseRotation = Plugin.YawOnly.Value ? Quaternion.Euler(0f, t.eulerAngles.y, 0f) : t.rotation;
                body.SetPositionAndRotation(t.position, baseRotation);
                head.localPosition = localPosition;
                head.localRotation = localRotation;
                LevelMovement.HeadYaw = head.eulerAngles.y;
                LevelMovement.Active = true;
                UpdateHands();
            }
            catch (Exception ex)
            {
                Log.LogError($"VR frame error: {ex}");
            }
        }

        // A tracking-space pose relative to the recentre pose, as a local pose under the body.
        void TrackingToLocal(Vector3 position, Quaternion rotation, out Vector3 localPosition, out Quaternion localRotation)
        {
            var recenter = Quaternion.Euler(0f, -zeroYaw, 0f);
            localPosition = recenter * (position - zeroPosition);
            localRotation = recenter * rotation;
        }

        IEnumerator SubmitLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                if (!running) continue;
                CaptureScreen();
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
            StopVr(immediate: true);
        }
    }
}
