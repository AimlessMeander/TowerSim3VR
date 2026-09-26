# TowerSim3VR

An unofficial VR mod for [Tower! Simulator 3](https://store.steampowered.com/app/2176130) (Unity 2022.3,
Mono, HDRP, Direct3D 11), built as a [BepInEx](https://github.com/BepInEx/BepInEx) 5 plugin in the style of
NuclearesVR. Tested on a Quest 3 over Steam Link / SteamVR with an RTX 4090 (about 85-90 fps at 90 Hz).

## Status

Working and in use:

- Stereo rendering, 6DoF head tracking, recentring. VR starts by itself when an airport has loaded and SteamVR is
  running, and stops when you leave the airport.
- Motion controllers with laser pointers: the radar screens, strip board, comms panel and aircraft in the world
  all take clicks. Menus and pop-ups show on a floating virtual screen.
- Stick movement over the whole airport, smooth turning, binocular zoom when looking at an aircraft.
- Fixes a game bug that left the radio voice silent for a whole session (see below). The intro video is skipped.

Known limits: the tower cab and its monitors vanish when you move outside the tower (the game hides the monitors
beyond 10 m, and the cab is modelled to be seen from inside).

## Controls (Quest Touch defaults, remappable in SteamVR's controller bindings)

| Control | What |
| --- | --- |
| Trigger | Left click. The hand that last pulled its trigger has the laser; the other shows a small ball. |
| Left stick | Move level, where you look. Click in for fast travel. |
| Right stick | Left/right turns. Up/down: move up and down; zooms a radar screen while pointing at it; binocular zoom while Y is held. |
| X (hold) | Push to talk (the game's push-to-talk key) |
| Y (hold) | Look at the airplane (the game's look key); the right stick zooms |
| B | Menu (Escape) |
| A | Show / hide the 2D screen (the game's 2D windows) |
| Right stick click | Desk view (F1) |
| Both triggers for 2 s | Recentre |

Keyboard: Ctrl+Shift+V starts / stops VR, End recentres, Ctrl+Shift+F flips the eye images, Ctrl+Shift+C logs the
cameras. Settings: `<game>/BepInEx/config/com.mjh.towersim3vr.cfg`. `VrMode`: Auto (default, only if SteamVR is
already running), Always (also launches SteamVR), Never (Ctrl+Shift+V only).

## How it works

- **Rendering** (`Vr/VrController.cs`): two ordinary HDRP cameras, one per eye, render to textures submitted to
  SteamVR. They hang off a body/head rig at the game camera's pose plus the headset pose; the game camera itself is
  never moved. HDRP's native XR path was tried first (the OpenVR XR plugin an earlier UUVR install left in
  `UnitySubsystems`) and does not work: the game was built without XR, so its shaders lack the stereo variants.
- **Performance**: the monitor shows the left eye instead of the game rendering its camera a third time (HDRP
  `customRender`); FSR3, the game's frame limiter and vsync are lifted while in VR; motion blur is off on the eyes.
- **Pointing** (`Vr/VrController.Hands.cs`, `Vr/VrKeys.cs`): the game does every 3D click from
  `Camera.main.ScreenPointToRay(Input.mousePosition)`, so the laser's target, projected into the game camera, is
  reported as the mouse position, and the controllers' buttons as keys and mouse buttons (Harmony patches on the
  legacy `Input`). Lasers stop on world-space canvases (the desk displays have no colliders). For Unity's UI event
  system the game camera is briefly put on the laser, because `GraphicRaycaster` drops pointers outside its view.
- **Drawing on top**: the lasers and the virtual screen use `UI/Default` on the last sorting layer; the desk
  canvases are on a later sorting layer than Default and otherwise draw over them. HDRP skips queue 4000 and up.
- **Virtual screen** (`Vr/VrController.Screen.cs`): `ScreenCapture` of the monitor onto a quad, shown while a menu or
  pop-up is open (or with B); the monitor draws black behind the 2D interface meanwhile.
- **Radio fix** (`RadioStartFix.cs`): unmodded, the first radio call can go out before the separate TTS program has
  connected; the speaking thread then waits forever for its audio and the voice is silent all session. Calls are
  held until the TTS program reports ready. `RadioDiagnostics.cs` logs the voice lock state.

## Build and install

```powershell
powershell -File scripts/setup-dependencies.ps1     # once: fills lib/ from your game install
dotnet build src/TowerSim3VR/TowerSim3VR.csproj      # also deploys into <game>/BepInEx/plugins
```

BepInEx 5.4.23.5 is installed in the game folder (`enabled` in `doorstop_config.ini` turns it off). Logs:
`<game>/BepInEx/LogOutput.log` and `%USERPROFILE%\AppData\LocalLow\FeelThere Inc_\Tower! Simulator 3\Player.log`.
`reference/` (gitignored) holds ilspycmd decompiles of the game and HDRP assemblies. The game's code is obfuscated,
so patches that name obfuscated members (`RadioStartFix`, `SkipIntro`'s timer) may need updating after a game
update; each patch class is applied separately and a failure is logged without stopping the rest.
