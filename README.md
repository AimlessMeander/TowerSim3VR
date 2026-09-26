# TowerSim3VR

An unofficial VR mod for [Tower! Simulator 3](https://store.steampowered.com/app/2176130) (Unity 2022.3,
Mono, HDRP, Direct3D 11), built as a [BepInEx](https://github.com/BepInEx/BepInEx) 5 plugin.

Rendering uses two ordinary HDRP cameras, one per eye, rendered to textures and submitted to SteamVR
(the NuclearesVR approach). They hang off a head rig that follows the game camera plus the headset pose; the
game camera itself is never moved.

HDRP's native XR path was tried first (starting the OpenVR XR display that an earlier UUVR install left in
`Tower! Simulator 3_Data/UnitySubsystems`) and does not work: the game was built without XR, so its shaders lack
the stereo variants. Single-pass drew only the left eye and scrambled instanced scenery; multi-pass skips terrain.

While VR runs, the game's FSR3 upscaler is forced off in memory (it is not XR aware). The intro video is skipped.

## Status

Step 1, proof of concept: two-camera rendering, not yet tested in the headset.

## Build and install

```powershell
powershell -File scripts/setup-dependencies.ps1     # once: fills lib/ from your game install
dotnet build src/TowerSim3VR/TowerSim3VR.csproj      # also deploys into <game>/BepInEx/plugins
```

BepInEx 5.4.23.5 is installed in the game folder. Logs: `<game>/BepInEx/LogOutput.log`; Unity's own log
(the OpenVR plugin writes `[OpenVR]` lines there):
`%USERPROFILE%\AppData\LocalLow\FeelThere Inc_\Tower! Simulator 3\Player.log`.
`reference/` (gitignored) holds ilspycmd decompiles of the game and HDRP assemblies. The game's own code is
obfuscated.

## Starting and stopping

VR starts by itself once an airport has fully loaded, and stops 1.5 s after you leave it, so menus stay an
ordinary desktop window (as in NuclearesVR). `VrMode`: Auto (default) starts only if SteamVR is already running,
so monitor play never launches SteamVR; Always also launches SteamVR; Never leaves it to Ctrl+Shift+V.
Shutdown stops submitting, shuts OpenVR down, then frees the eye textures (freeing them earlier can crash the driver).

## Keys

| Key | What |
| --- | --- |
| Ctrl+Shift+V | Start / stop VR |
| End | Recenter |
| Ctrl+Shift+F | Flip the eye images vertically (if the view is upside down) |
| Ctrl+Shift+C | Log all cameras (diagnostics) |

Settings: `<game>/BepInEx/config/com.mjh.towersim3vr.cfg` (created on first run).
