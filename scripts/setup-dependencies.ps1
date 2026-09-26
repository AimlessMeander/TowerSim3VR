<#
.SYNOPSIS
  Populates lib/ with everything the project needs to build, but that we don't
  commit to git: the game's own assemblies (copyrighted, must come from your
  local install) and the BepInEx core assemblies.

.PARAMETER GameDir
  Path to the Tower! Simulator 3 install. Defaults to the Steam location found on
  this machine; override if yours differs.
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
    "Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll",
    "UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.PhysicsModule.dll",
    "UnityEngine.InputLegacyModule.dll", "UnityEngine.UI.dll", "UnityEngine.UIModule.dll",
    "UnityEngine.IMGUIModule.dll", "UnityEngine.TextRenderingModule.dll",
    "UnityEngine.XRModule.dll", "UnityEngine.VRModule.dll", "UnityEngine.SubsystemsModule.dll", "UnityEngine.VideoModule.dll",
    "Unity.RenderPipelines.Core.Runtime.dll", "Unity.RenderPipelines.HighDefinition.Runtime.dll",
    "Unity.InputSystem.dll", "Unity.TextMeshPro.dll", "Cinemachine.dll"
)
foreach ($dll in $gameDlls) {
    $src = Join-Path $managed $dll
    if (Test-Path $src) {
        Copy-Item $src "$root\lib\game\" -Force
    } else {
        Write-Warning "Missing (skipped): $dll"
    }
}

$bepinexCore = Join-Path $GameDir "BepInEx\core"
if (-not (Test-Path $bepinexCore)) {
    throw "BepInEx isn't installed in $GameDir yet - install it first (see README.md)."
}
Copy-Item "$bepinexCore\BepInEx.dll" "$root\lib\bepinex\" -Force
Copy-Item "$bepinexCore\0Harmony.dll" "$root\lib\bepinex\" -Force

Write-Host "Dependencies ready. Build with: dotnet build src/TowerSim3VR/TowerSim3VR.csproj" -ForegroundColor Green
