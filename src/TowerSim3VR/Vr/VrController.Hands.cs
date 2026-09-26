using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;

namespace TowerSim3VR
{
    // Motion controllers through SteamVR Input (manifest and Quest Touch bindings next to the DLL; anyone can
    // remap in SteamVR's binding screen). As in NuclearesVR: the last hand to pull its trigger has the laser, the
    // trigger is the left mouse button, sticks move and turn. Buttons: X push to talk, Y look at the airplane
    // (the right stick zooms), B menu, A the 2D screen, right stick click F1 (desk view).
    //
    // Pointing into the world: the game does every 3D click (radar screens, aircraft tags, picking aircraft)
    // from Camera.main.ScreenPointToRay(Input.mousePosition), and the radar screens work out the position on
    // their display from the mouse position and that camera too. So the laser's target point is projected into
    // the game camera and reported as the mouse position; the game camera sits near the head, so a ray from it
    // through that point meets the same thing the laser does.
    public partial class VrController
    {
        struct Hand
        {
            public bool Valid;
            public Vector3 Position;   // world
            public Quaternion Rotation;
            public bool Trigger, StickClick;
        }

        Hand leftHand, rightHand;
        Vector2 moveStick, turnStick;
        bool buttonA, buttonB, buttonX, buttonY;

        bool inputReady;
        ulong hPoseL, hPoseR, hTrigL, hTrigR, hMove, hTurn, hA, hB, hX, hY, hClickL, hClickR;
        readonly VRActiveActionSet_t[] actionSets = new VRActiveActionSet_t[1];
        static readonly uint DigitalSize = (uint)Marshal.SizeOf(typeof(InputDigitalActionData_t));
        static readonly uint AnalogSize = (uint)Marshal.SizeOf(typeof(InputAnalogActionData_t));
        static readonly uint PoseSize = (uint)Marshal.SizeOf(typeof(InputPoseActionData_t));
        static readonly uint ActionSetSize = (uint)Marshal.SizeOf(typeof(VRActiveActionSet_t));

        GameObject leftLaser, rightLaser;
        Material laserIdle, laserActive, dotIdle, dotActive, handMarker;
        bool activeHandIsRight = true;
        bool previousLeftTrigger, previousRightTrigger, previousA;
        // Y held: looking at the airplane (the game's look key), which also makes the right stick the binocular zoom.
        bool Looking => buttonY;
        bool pointingAtDesk;
        readonly HashSet<KeyCode> wantedKeys = new HashSet<KeyCode>();
        float bothTriggersSince = -1f;
        bool recentredThisHold;
        DeskCameraController desk;

        // The game's radar and desk displays are on this layer (the game's own raycasts use it).
        const int DeskDisplayLayer = 23;
        const float LaserIdleLength = 5f;
        const float StickDeadzone = 0.2f;

        void InitInput()
        {
            inputReady = false;
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var manifest = Path.Combine(dir, "towersim3vr_actions.json");
                if (!File.Exists(manifest))
                {
                    Log.LogWarning($"SteamVR Input: {manifest} is missing - controllers won't work");
                    return;
                }
                var error = OpenVR.Input.SetActionManifestPath(manifest);
                if (error != EVRInputError.None)
                {
                    Log.LogWarning($"SteamVR Input: manifest -> {error}");
                    return;
                }
                ulong actionSet = 0;
                bool ok = OpenVR.Input.GetActionSetHandle("/actions/main", ref actionSet) == EVRInputError.None;
                actionSets[0] = new VRActiveActionSet_t { ulActionSet = actionSet, ulRestrictedToDevice = OpenVR.k_ulInvalidInputValueHandle };
                ok &= Handle("PoseLeft", ref hPoseL) & Handle("PoseRight", ref hPoseR);
                ok &= Handle("TriggerLeft", ref hTrigL) & Handle("TriggerRight", ref hTrigR);
                ok &= Handle("Move", ref hMove) & Handle("Turn", ref hTurn);
                ok &= Handle("ButtonA", ref hA) & Handle("ButtonB", ref hB) & Handle("ButtonX", ref hX) & Handle("ButtonY", ref hY);
                ok &= Handle("StickClickLeft", ref hClickL) & Handle("StickClickRight", ref hClickR);
                inputReady = ok;
                if (!ok) Log.LogWarning("SteamVR Input: some actions were not found");
            }
            catch (Exception ex)
            {
                Log.LogError($"SteamVR Input setup failed: {ex}");
            }
        }

        static bool Handle(string action, ref ulong handle)
        {
            var error = OpenVR.Input.GetActionHandle("/actions/main/in/" + action, ref handle);
            if (error != EVRInputError.None) Log.LogWarning($"SteamVR Input: {action} -> {error}");
            return error == EVRInputError.None;
        }

        static bool Digital(ulong handle)
        {
            var data = new InputDigitalActionData_t();
            return OpenVR.Input.GetDigitalActionData(handle, ref data, DigitalSize, OpenVR.k_ulInvalidInputValueHandle) == EVRInputError.None
                && data.bActive && data.bState;
        }

        static Vector2 Analog(ulong handle)
        {
            var data = new InputAnalogActionData_t();
            return OpenVR.Input.GetAnalogActionData(handle, ref data, AnalogSize, OpenVR.k_ulInvalidInputValueHandle) == EVRInputError.None
                && data.bActive ? new Vector2(data.x, data.y) : Vector2.zero;
        }

        void ReadHand(ulong pose, ulong trigger, ulong click, ref Hand hand)
        {
            var data = new InputPoseActionData_t();
            var error = OpenVR.Input.GetPoseActionDataForNextFrame(pose, TrackingSpace, ref data, PoseSize, OpenVR.k_ulInvalidInputValueHandle);
            hand.Valid = error == EVRInputError.None && data.bActive && data.pose.bPoseIsValid;
            if (hand.Valid)
            {
                data.pose.mDeviceToAbsoluteTracking.ToUnity(out var p, out var r);
                // Same mapping as the head, then into the world through the body.
                TrackingToLocal(p, r, out var localPosition, out var localRotation);
                hand.Position = body.TransformPoint(localPosition);
                hand.Rotation = body.rotation * localRotation * Quaternion.Euler(Plugin.LaserPitch.Value, 0f, 0f);
            }
            hand.Trigger = Digital(trigger);
            hand.StickClick = Digital(click);
        }

        // In OnBeforeRender, after WaitGetPoses and the body and head have been placed.
        void UpdateHands()
        {
            if (!inputReady || body == null) return;
            if (OpenVR.Input.UpdateActionState(actionSets, ActionSetSize) != EVRInputError.None) return;

            ReadHand(hPoseL, hTrigL, hClickL, ref leftHand);
            ReadHand(hPoseR, hTrigR, hClickR, ref rightHand);
            moveStick = Analog(hMove);
            turnStick = Analog(hTurn);
            buttonA = Digital(hA);
            buttonB = Digital(hB);
            buttonX = Digital(hX);
            buttonY = Digital(hY);

            if (rightHand.Trigger && !previousRightTrigger) activeHandIsRight = true;
            else if (leftHand.Trigger && !previousLeftTrigger) activeHandIsRight = false;
            previousLeftTrigger = leftHand.Trigger;
            previousRightTrigger = rightHand.Trigger;

            UpdatePointing();
            UpdateButtons();
        }

        void UpdatePointing()
        {
            EnsureLasers();
            var hand = activeHandIsRight ? rightHand : leftHand;
            bool onScreen = false, inWorld = false;
            var worldTarget = Vector3.zero;
            float activeLength = LaserIdleLength;
            pointingAtDesk = false;

            if (hand.Valid && source != null)
            {
                var direction = hand.Rotation * Vector3.forward;
                if (TryHitScreen(hand.Position, direction, out var screenPixel, out var screenDistance))
                {
                    onScreen = true;
                    activeLength = screenDistance;
                    VrKeys.MousePosition = screenPixel;
                }
                else if (!screenVisible)
                {
                    var target = hand.Position + direction * 1000f;
                    var nearest = float.PositiveInfinity;
                    if (Physics.Raycast(hand.Position, direction, out var hit, 20000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                    {
                        target = hit.point;
                        nearest = hit.distance;
                        pointingAtDesk = hit.collider.gameObject.layer == DeskDisplayLayer;
                    }
                    // The desk displays (strip board and others) are world-space canvases with no collider, so
                    // physics misses them; the laser must stop on them, both to be seen and so the projected
                    // point is the one on the display rather than the desk behind it.
                    if (TryHitWorldCanvas(hand.Position, direction, out var canvasDistance) && canvasDistance < nearest)
                    {
                        nearest = canvasDistance;
                        target = hand.Position + direction * canvasDistance;
                        pointingAtDesk = true;
                    }
                    if (nearest < float.PositiveInfinity) activeLength = nearest;
                    worldTarget = target;
                    var projected = source.WorldToScreenPoint(target);
                    if (projected.z > 0f)
                    {
                        inWorld = true;
                        VrKeys.MousePosition = projected;
                    }
                }
            }

            VrKeys.MouseOverride = onScreen || inWorld;
            VrKeys.WorldPointing = inWorld;
            VrKeys.AimValid = inWorld;
            if (inWorld)
            {
                VrKeys.AimOrigin = hand.Position;
                VrKeys.AimRotation = hand.Rotation;
                VrKeys.AimTarget = worldTarget;
                VrKeys.AimCamera = source.transform;
            }
            VrKeys.SetMouseButton(0, (onScreen || inWorld) && hand.Trigger);

            ShowLaser(leftLaser, leftHand, !activeHandIsRight, activeHandIsRight ? LaserIdleLength : activeLength);
            ShowLaser(rightLaser, rightHand, activeHandIsRight, activeHandIsRight ? activeLength : LaserIdleLength);
        }

        void UpdateButtons()
        {
            wantedKeys.Clear();
            // X holds the game's push-to-talk key and Y its "look at the airplane" key, both read from the game's
            // settings so they follow any change made there. B is the menu key (Escape).
            if (buttonX) wantedKeys.Add(GameSettings.Get("key_push_to_talk", KeyCode.LeftControl));
            if (buttonY) wantedKeys.Add(GameSettings.Get("key_push_to_look", KeyCode.Home));
            if (buttonB && Plugin.KeyB.Value != KeyCode.None) wantedKeys.Add(Plugin.KeyB.Value);
            if (rightHand.StickClick && Plugin.KeyRightStick.Value != KeyCode.None) wantedKeys.Add(Plugin.KeyRightStick.Value);
            VrKeys.Apply(wantedKeys);

            if (buttonA && !previousA) screenToggled = !screenToggled;
            previousA = buttonA;

            // Holding both triggers recentres (once per hold).
            if (leftHand.Trigger && rightHand.Trigger && Plugin.RecenterHoldSeconds.Value > 0f)
            {
                if (bothTriggersSince < 0f) bothTriggersSince = Time.unscaledTime;
                else if (!recentredThisHold && Time.unscaledTime - bothTriggersSince >= Plugin.RecenterHoldSeconds.Value)
                {
                    recentredThisHold = true;
                    needsRecenter = true;
                }
            }
            else
            {
                bothTriggersSince = -1f;
                recentredThisHold = false;
            }

            // Right stick up/down zooms a radar screen while the laser is on it (the mouse wheel); while Y is held
            // it is the binocular zoom instead.
            VrKeys.Scroll = !Looking && pointingAtDesk && Mathf.Abs(turnStick.y) > StickDeadzone ? turnStick.y * 0.05f : 0f;
        }

        // ---- binocular zoom while looking at the airplane ----
        // On a monitor the mouse wheel narrows the camera's field of view, which the headset ignores (the eyes
        // have its projection). So both eyes' projections are magnified instead, like binoculars, while Y is held;
        // letting go of Y returns to 1x (the game itself puts the view back where it was). LOD bias is raised by the
        // same factor so a far-away airplane is drawn in full detail when magnified.
        float zoom = 1f;
        float zoomTarget = 1f;
        float savedLodBias = -1f;

        void UpdateZoom()
        {
            float dt = Time.unscaledDeltaTime;
            if (Looking && !screenVisible)
            {
                var push = Curve(turnStick.y);
                if (push != 0f) zoomTarget *= Mathf.Pow(2f, push * Plugin.ZoomSpeed.Value * dt);
                zoomTarget = Mathf.Clamp(zoomTarget, 1f, Mathf.Max(1f, Plugin.MaxZoom.Value));
            }
            else
            {
                zoomTarget = 1f;
            }
            // Ease towards the target in log space, quicker on the way back out.
            float rate = zoomTarget < zoom ? 12f : 8f;
            zoom = Mathf.Exp(Mathf.Lerp(Mathf.Log(zoom), Mathf.Log(zoomTarget), 1f - Mathf.Exp(-rate * dt)));
            if (Mathf.Abs(zoom - zoomTarget) < 0.001f) zoom = zoomTarget;

            if (zoom > 1f)
            {
                if (savedLodBias < 0f) savedLodBias = QualitySettings.lodBias;
                QualitySettings.lodBias = savedLodBias * zoom;
            }
            else
            {
                RestoreZoom();
            }
        }

        void RestoreZoom()
        {
            zoom = zoomTarget = 1f;
            if (savedLodBias >= 0f) QualitySettings.lodBias = savedLodBias;
            savedLodBias = -1f;
        }

        // In Update: the sticks move the game's own camera target, which the game then glides to.
        void UpdateSticks()
        {
            if (!inputReady || screenVisible) return;
            if (desk == null) desk = FindObjectOfType<DeskCameraController>();
            if (desk == null) return;

            float dt = Time.unscaledDeltaTime;
            float speed = Plugin.MoveSpeed.Value * (leftHand.StickClick ? Plugin.FastMoveMultiplier.Value : 1f);
            // Right stick up/down is the zoom while Y is held, and a radar screen's zoom while pointing at one.
            var stick = new Vector3(Curve(moveStick.x), pointingAtDesk || Looking ? 0f : Curve(turnStick.y), Curve(moveStick.y));
            if (stick != Vector3.zero)
            {
                // Level along where the head faces; right stick up/down is straight up and down.
                var level = Quaternion.Euler(0f, LevelMovement.HeadYaw, 0f) * new Vector3(stick.x, 0f, stick.z);
                desk.target_pos += (level + Vector3.up * stick.y) * speed * dt;
            }
            var turn = Curve(turnStick.x);
            if (turn != 0f)
            {
                desk.target_rot = Quaternion.Euler(0f, turn * Plugin.TurnSpeed.Value * dt, 0f) * desk.target_rot;
            }
        }

        // Past the dead zone, rescaled to 0..1 and squared: a small push is slow, a full push full speed.
        static float Curve(float v)
        {
            var magnitude = Mathf.InverseLerp(StickDeadzone, 1f, Mathf.Abs(v));
            return Mathf.Sign(v) * magnitude * magnitude;
        }

        // ---- world-space canvases (the desk displays) ----

        readonly List<Canvas> worldCanvases = new List<Canvas>();
        float nextCanvasScan;

        bool TryHitWorldCanvas(Vector3 origin, Vector3 direction, out float distance)
        {
            distance = float.PositiveInfinity;
            if (Time.unscaledTime >= nextCanvasScan)
            {
                nextCanvasScan = Time.unscaledTime + 2f;
                worldCanvases.Clear();
                foreach (var canvas in FindObjectsOfType<Canvas>())
                {
                    if (canvas.isRootCanvas && canvas.renderMode == RenderMode.WorldSpace) worldCanvases.Add(canvas);
                }
            }
            foreach (var canvas in worldCanvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                var rect = (RectTransform)canvas.transform;
                var normal = rect.forward;
                var facing = Vector3.Dot(direction, normal);
                if (Mathf.Abs(facing) < 1e-4f) continue;
                var d = Vector3.Dot(rect.position - origin, normal) / facing;
                if (d <= 0f || d >= distance) continue;
                var local = rect.InverseTransformPoint(origin + direction * d);
                if (rect.rect.Contains(new Vector2(local.x, local.y))) distance = d;
            }
            return distance < float.PositiveInfinity;
        }

        // ---- lasers (drawn into the eye images over everything, see VrController.Overlay.cs) ----

        void EnsureLasers()
        {
            if (laserIdle == null)
            {
                laserIdle = MakeUnlitColor(new Color(0.35f, 0.85f, 1f, 0.6f));
                laserActive = MakeUnlitColor(new Color(0.3f, 1f, 0.35f, 1f));
                dotIdle = MakeUnlitColor(new Color(0.35f, 0.85f, 1f, 1f));
                dotActive = MakeUnlitColor(new Color(0.3f, 1f, 0.35f, 1f));
                handMarker = MakeUnlitColor(new Color(0.9f, 0.95f, 1f, 1f));
            }
            if (leftLaser == null) leftLaser = MakeLaser();
            if (rightLaser == null) rightLaser = MakeLaser();
        }

        GameObject MakeLaser()
        {
            var root = new GameObject("TowerSim3VR_Laser");
            root.transform.SetParent(body, false);
            foreach (var (name, type, order) in new[] { ("Beam", PrimitiveType.Cube, 1), ("Dot", PrimitiveType.Sphere, 2) })
            {
                var part = GameObject.CreatePrimitive(type);
                part.name = name;
                part.transform.SetParent(root.transform, false);
                Destroy(part.GetComponent<Collider>()); // must never get in the way of the game's raycasts
                var renderer = part.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = laserIdle;
                RegisterOverlay(renderer, order);
            }
            return root;
        }

        void ShowLaser(GameObject laser, Hand hand, bool active, float length)
        {
            if (laser.activeSelf != hand.Valid) laser.SetActive(hand.Valid);
            if (!hand.Valid) return;
            laser.transform.SetPositionAndRotation(hand.Position, hand.Rotation);
            var beam = laser.transform.GetChild(0);
            var dot = laser.transform.GetChild(1);
            var beamRenderer = beam.GetComponent<MeshRenderer>();
            var dotRenderer = dot.GetComponent<MeshRenderer>();

            // Only the active hand has a laser; the other shows a short stub, to show where it is and which way it points.
            if (!active)
            {
                const float stub = 0.08f;
                beam.localPosition = new Vector3(0f, 0f, stub * 0.5f);
                beam.localScale = new Vector3(0.006f, 0.006f, stub);
                dot.localPosition = new Vector3(0f, 0f, stub);
                dot.localScale = Vector3.one * 0.015f;
                beamRenderer.sharedMaterial = handMarker;
                dotRenderer.sharedMaterial = handMarker;
                return;
            }
            beam.localPosition = new Vector3(0f, 0f, length * 0.5f);
            beam.localScale = new Vector3(0.003f, 0.003f, length);
            dot.localPosition = new Vector3(0f, 0f, length);
            dot.localScale = Vector3.one * Mathf.Clamp(length * 0.008f, 0.012f, 3f);
            beamRenderer.sharedMaterial = hand.Trigger ? laserActive : laserIdle;
            dotRenderer.sharedMaterial = hand.Trigger ? dotActive : dotIdle;
        }

        // UI/Default is always in a build: unlit, alpha blended, no culling.
        static Material MakeUnlitColor(Color color)
        {
            var material = new Material(Shader.Find("UI/Default")) { color = color };
            material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            return material;
        }

        // On leaving VR: no button or key left held.
        void ReleaseInput()
        {
            VrKeys.Clear();
            inputReady = false;
            leftHand = rightHand = default;
            screenToggled = false;
            buttonY = false;
            RestoreZoom();
        }
    }
}
