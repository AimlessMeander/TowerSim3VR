<#
.SYNOPSIS
  Populates lib/ with everything the project needs to build but that isn't in git: the game's own
  assemblies (copyrighted, must come from your local install), the BepInEx core assemblies, and Valve's
  openvr_api.dll (downloaded from Valve's GitHub if missing).

.PARAMETER GameDir
  Path to the Tower! Simulator 3 install, with BepInEx 5 already installed in it.
#>
param(
    [string]$GameDir = "E:\Programs\Steam\steamapps\common\Tower! Simulator 3"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$managed = Join-Path $GameDir "Tower! Simulator 3_Data\Managed"

if (-not (Test-Path $managed)) {
    throw "Couldn't find $managed - pass -GameDir pointing at your Tower! Simulator 3 install."
}

New-Item -ItemType Directory -Force -Path "$root\lib\game" | Out-Null
New-Item -ItemType Directory -Force -Path "$root\lib\bepinex" | Out-Null

$gameDlls = @(
    "Assembly-CSharp.dll",
    "UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.InputLegacyModule.dll", "UnityEngine.PhysicsModule.dll",
    "UnityEngine.ScreenCaptureModule.dll", "UnityEngine.UI.dll", "UnityEngine.UIModule.dll", "UnityEngine.VideoModule.dll",
    "Unity.InputSystem.dll", "Unity.RenderPipelines.Core.Runtime.dll", "Unity.RenderPipelines.HighDefinition.Runtime.dll"
)
foreach ($dll in $gameDlls) {
    $src = Join-Path $managed $dll
    if (-not (Test-Path $src)) { throw "Missing $dll in $managed" }
    Copy-Item $src "$root\lib\game\" -Force
}

$bepinexCore = Join-Path $GameDir "BepInEx\core"
if (-not (Test-Path $bepinexCore)) {
    throw "BepInEx isn't installed in $GameDir yet - install BepInEx 5 (x64) there first."
}
Copy-Item "$bepinexCore\BepInEx.dll" "$root\lib\bepinex\" -Force
Copy-Item "$bepinexCore\0Harmony.dll" "$root\lib\bepinex\" -Force

$openvrDll = "$root\lib\openvr_api.dll"
if (-not (Test-Path $openvrDll)) {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/ValveSoftware/openvr/master/bin/win64/openvr_api.dll" -OutFile $openvrDll
}

Write-Host "Dependencies ready. Build with: dotnet build src/TowerSim3VR/TowerSim3VR.csproj" -ForegroundColor Green
