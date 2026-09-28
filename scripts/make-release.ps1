<#
.SYNOPSIS
  Builds a zip to share the mod: the compiled plugin, a ready-configured copy of the BepInEx mod
  loader (taken from your own game folder), and the player install guide. Contains no game files.

.PARAMETER GameDir
  The Tower! Simulator 3 install folder that already has BepInEx installed (defaults to this machine's).

.OUTPUTS
  dist\TowerSim3VR-<version>.zip
#>
param(
    [string]$GameDir = "E:\Programs\Steam\steamapps\common\Tower! Simulator 3"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# Build (Release; deploys into the game folder too)
& dotnet build "$root\src\TowerSim3VR\TowerSim3VR.csproj" -c Release --no-incremental | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$version = (Select-String -Path "$root\src\TowerSim3VR\Plugin.cs" -Pattern 'BepInPlugin\([^)]*"(\d+\.\d+\.\d+)"\)').Matches[0].Groups[1].Value
$out = "$root\dist\TowerSim3VR-$version"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path "$out\BepInEx\core", "$out\BepInEx\plugins\TowerSim3VR", "$out\BepInEx\config", "$out\BepInEx\patchers" | Out-Null

# Mod loader (BepInEx 5, x64), exactly as installed in the game folder - but not its settings, cache or logs
foreach ($f in "winhttp.dll", "doorstop_config.ini", ".doorstop_version") {
    if (-not (Test-Path "$GameDir\$f")) { throw "Missing $f in $GameDir - is BepInEx installed there?" }
    Copy-Item "$GameDir\$f" $out
}
if (-not (Select-String -Path "$out\doorstop_config.ini" -Pattern '^enabled\s*=\s*true' -Quiet)) {
    throw "doorstop_config.ini in the game folder has BepInEx disabled - set enabled = true first."
}
Copy-Item "$GameDir\BepInEx\core\*" "$out\BepInEx\core" -Recurse

# The plugin itself, the OpenVR library and the SteamVR Input files
$bin = "$root\src\TowerSim3VR\bin\Release"
foreach ($f in "TowerSim3VR.dll", "openvr_api.dll", "towersim3vr_actions.json", "towersim3vr_bindings_oculus_touch.json") {
    if (-not (Test-Path "$bin\$f")) { throw "Build output $f not found in $bin" }
    Copy-Item "$bin\$f" "$out\BepInEx\plugins\TowerSim3VR"
}

Copy-Item "$root\release\INSTALL.txt" $out
Copy-Item "$root\CHANGELOG.md" "$out\CHANGELOG.txt"
Copy-Item "$root\LICENSE" "$out\BepInEx\plugins\TowerSim3VR\LICENSE.txt"

# Safety check: nothing from the game itself may be in the package
$bad = Get-ChildItem $out -Recurse -Include "Assembly-CSharp*.dll", "UnityEngine*.dll", "Unity.*.dll", "Tower!*"
if ($bad) { throw "Game files found in the package: $($bad.FullName -join ', ')" }

$zip = "$root\dist\TowerSim3VR-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
# Entry names with "/" separators: Compress-Archive (and .NET Framework's ZipFile) write "\", which some unzip
# tools turn into file names instead of folders.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $out -Recurse -File -Force) {
        $name = $file.FullName.Substring($out.Length + 1).Replace("\", "/")
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
Write-Host "Built $zip" -ForegroundColor Green
Get-ChildItem $out -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($out.Length + 1) }
