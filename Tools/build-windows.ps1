# Builds the Windows game into Builds/Windows/UltraPuck.exe (and copies StreamingAssets, so card/map edits land in the build).
# Double-click "Build Windows.bat" in the project root, or:
#   powershell -File Tools/build-windows.ps1
#
# Close the Unity editor first if it has this project open.
#
# Note: card face text / art / maps are files under StreamingAssets — a rebuild (or Sync Cards.bat) copies them.
# Gameplay numbers for utility cards (Shockwave slow, range, duration, cooldown, …) live in Core/Tuning.cs;
# changing only the face rows in ultrapuck.json updates what the card says, not how it plays, until Tuning matches.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe'
if (!(Test-Path $unity)) { throw "Unity 6000.6.9f1 not found at $unity" }

$log = Join-Path $root 'build.log'
Write-Host "Building Ultra Puck (Windows)…"
$p = Start-Process -FilePath $unity -ArgumentList @(
    '-batchmode', '-quit',
    '-projectPath', "`"$root`"",
    '-executeMethod', 'UltraPuck.Editor.BuildScript.BuildWindows',
    '-logFile', "`"$log`""
) -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "The build failed (exit $($p.ExitCode)); see build.log" }
# Unity already copies StreamingAssets; sync again so a mid-build edit still lands
& (Join-Path $PSScriptRoot 'sync-streaming.ps1')
$exe = Join-Path $root 'Builds\Windows\UltraPuck.exe'
Write-Host "Done: $exe"
