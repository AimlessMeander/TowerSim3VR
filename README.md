# TowerSim3VR

An unofficial VR mod for [Tower! Simulator 3](https://store.steampowered.com/app/2176130) (Steam, Windows): play
from the tower cab in a SteamVR headset, with head tracking and motion controllers. The game has no VR support
of its own. Built as a [BepInEx](https://github.com/BepInEx/BepInEx) 5 plugin, in the style of
[NuclearesVR](https://github.com/AimlessMeander/NuclearesVR).

**Status: test version for playtesting.** Tested with a Quest 3 over Steam Link on an RTX 4090 (about 85-90 fps
at 90 Hz). Please report problems as GitHub issues, with `BepInEx\LogOutput.log` from the game folder.

## Features

- **Removes the long intro video** that otherwise plays every time the game starts, in VR and on the monitor.
  The game goes straight to the main menu. (Set `SkipIntro = false` in the settings to get it back.)
- Stereo 3D with head tracking. VR starts by itself when an airport has loaded (if SteamVR is running) and
  stops when you leave it, so the game's menus stay on the monitor.
- Laser pointers on both controllers: click and drag on the radar screens, the strip board and the comms panel,
  and pick aircraft in the world. Menus and pop-ups appear on a floating screen.
- Stick movement over the whole airport, smooth turning, push to talk for speech recognition.
- Binoculars: hold Y to look at the selected aircraft and zoom with the right stick, with a steadied view and a
  binocular frame.
- Aircraft labels (call sign and runway, as on the monitor) above the aircraft, facing you, at a readable size at
  any distance. Labels that would overlap are stacked. Can be switched off or resized in the settings.
- Fixes a game bug (present without the mod too) that could leave the radio voice silent for a whole session.

## Install

Download the zip from [Releases](../../releases), then follow `INSTALL.txt` inside it: copy everything into the
game folder (next to `Tower! Simulator 3.exe`). The zip contains the mod and the BepInEx loader, no game files.
To update, copy the new zip's contents over the old ones. What changed: [CHANGELOG.md](CHANGELOG.md).

## Controls (Quest Touch; remappable in SteamVR's controller bindings)

| Control | Action |
| --- | --- |
| Trigger | Click; hold and move to drag. The hand that last pulled its trigger has the laser. |
| Left stick | Move where you look; click in for fast travel |
| Right stick | Turn; up/down moves up and down, or zooms a radar screen you point at, or zooms the binoculars |
| X (hold) | Push to talk |
| Y (hold) | Look at the airplane, binoculars |
| B | Menu (Escape) |
| A | Show / hide the 2D windows on the floating screen |
| Right stick click | Desk view (F1) |
| Both triggers, 2 s | Recentre (or the End key) |

Keyboard: Ctrl+Shift+V switches VR off and on. Settings: `BepInEx\config\com.mjh.towersim3vr.cfg`
(see `INSTALL.txt`).

Known limits: the tower cab and its monitors disappear when you move outside the tower (the game hides the
monitors away from the tower, and the cab is only modelled from inside). DirectX 12 mode (`TowerDX12.bat`) is not
supported.

## Building

Needs the .NET SDK, the game, and BepInEx 5 (x64) installed in the game folder.

```powershell
powershell -File scripts/setup-dependencies.ps1     # once: copies the game's assemblies into lib/ (not in git)
dotnet build src/TowerSim3VR/TowerSim3VR.csproj      # builds and deploys into <game>/BepInEx/plugins
powershell -File scripts/make-release.ps1           # builds dist/TowerSim3VR-<version>.zip
```

`GameDir` in the `.csproj` and the scripts defaults to `E:\Programs\Steam\steamapps\common\Tower! Simulator 3`;
pass `-GameDir` / `-p:GameDir=` for another location. Logs: `<game>/BepInEx/LogOutput.log` and
`%USERPROFILE%\AppData\LocalLow\FeelThere Inc_\Tower! Simulator 3\Player.log`. The game's code is obfuscated
(decompile with `ilspycmd` to read it).

## How it works

- **Rendering** (`Vr/VrController.cs`): the game uses HDRP and was built without XR, so its shaders lack the
  stereo variants that Unity's own XR path needs (tried first: single-pass drew only the left eye and scrambled
  instanced objects; multi-pass skips terrain). Instead two ordinary HDRP cameras, one per eye, render to textures
  that are submitted to SteamVR. They hang off a body/head rig at the game camera's pose plus the headset pose;
  the game camera itself is never moved.
- **Performance**: the monitor shows the left eye instead of the game rendering its camera a third time (HDRP
  `customRender`, `Vr/MonitorMirror.cs`); FSR3, the game's frame limiter and vsync are lifted while in VR
  (`GraphicsOverrides.cs`, `Vr/FramePacing.cs`); motion blur is off on the eyes.
- **Pointing** (`Vr/VrController.Hands.cs`, `Vr/VrKeys.cs`): the game does every 3D click from
  `Camera.main.ScreenPointToRay(Input.mousePosition)`, so the laser's target, projected into the game camera, is
  reported as the mouse position, and controller buttons as keys and mouse buttons (Harmony patches on the legacy
  `Input`). Lasers stop on world-space canvases (the desk displays have no colliders). While Unity's UI event
  system runs, the game camera is put on the laser (`GraphicRaycaster` drops pointers outside its view), and kept
  where it was at the press while dragging, so the pointer moves.
- **The mod's own objects** (`Vr/VrController.Overlay.cs`): labels, lasers, the hand marker, the virtual screen and the
  binocular frame are drawn with a command buffer straight into each eye image after HDRP has finished (HDRP drew
  the desk displays over them whatever their sorting).
- **Aircraft labels** (`Vr/VrController.Labels.cs`): the game's labels (`VTag`) are on a 2D overlay placed with
  the game camera, so the headset can't show them. The mod builds its own billboards with the same rules, colours and
  font, drawn with the other overlays. The text is generated at the pixel size it covers in the eye image (the font
  texture has no mipmaps), and laid out again whenever the game rebuilds the shared font texture.
- **Virtual screen** (`Vr/VrController.Screen.cs`): `ScreenCapture` of the monitor onto a quad, shown while a menu
  or pop-up is open (or with A); the monitor draws black behind the 2D interface meanwhile.
- **Binoculars** (`Vr/VrController.Binoculars.cs`): the eye projections are magnified, the head rotation is
  low-pass filtered while zoomed, and the frame is drawn without the zoom.
- **Radio fix** (`RadioStartFix.cs`): the first radio call can go out before the game's separate TTS program has
  connected; the speaking thread then waits forever for its audio. Calls are held until the TTS program reports
  ready.
- **Robustness**: patches that name obfuscated members (`RadioStartFix`, `SkipIntro`'s timer) may need updating
  after a game update; each patch class is applied on its own and a failure is logged without stopping the rest.

## Licence

MIT (see `LICENSE`). Built on BepInEx (LGPL-2.1), HarmonyX (MIT) and Valve's OpenVR API (BSD-3-Clause).
Not affiliated with FeelThere or Valve.
