# Builds Ultra Puck, packs it as dist/UltraPuck-Windows.zip (an "UltraPuck" folder with the game and a read-me)
# and dist/UltraPuck-Linux.tar.gz (the Linux / Steam Deck version), and publishes a GitHub release. Every copy of the game checks for the newest release when it starts and updates
# itself, so publishing is all it takes to update everyone.
#
#   Double-click Release.bat at the project root (asks for notes, bump + build + publish + commit + push).
#   powershell -File Tools/release.ps1 -Bump -Notes "What changed"   (0.1.0 -> 0.1.1, build, publish)
#   powershell -File Tools/release.ps1                                (publish the version in version.txt)
#   powershell -File Tools/release.ps1 -NoPublish                     (just make the zip, e.g. to send someone)
param([switch]$Bump, [switch]$NoPublish, [string]$Notes = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$repo = 'FancyDucky/UltraPuck-Standalone'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe'

$version = (Get-Content version.txt -Raw).Trim()
if ($Bump) {
    $parts = $version.Split('.')
    $parts[$parts.Length - 1] = [string]([int]$parts[$parts.Length - 1] + 1)
    $version = $parts -join '.'
    [IO.File]::WriteAllText((Join-Path $root 'version.txt'), "$version`n")
}
Write-Host "Ultra Puck $version"

# build
$log = Join-Path $root 'build.log'
$p = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-quit', '-projectPath', "`"$root`"", '-executeMethod', 'UltraPuck.Editor.BuildScript.BuildWindows', '-logFile', "`"$log`"") -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "The build failed (exit $($p.ExitCode)); see build.log" }
$linuxLog = Join-Path $root 'build-linux.log'
$p = Start-Process -FilePath $unity -ArgumentList @('-batchmode', '-quit', '-projectPath', "`"$root`"", '-executeMethod', 'UltraPuck.Editor.BuildScript.BuildLinux', '-logFile', "`"$linuxLog`"") -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "The Linux build failed (exit $($p.ExitCode)); see build-linux.log" }

# pack: an UltraPuck folder with the game and a read-me
$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
$folder = Join-Path $dist 'UltraPuck'
New-Item -ItemType Directory $folder | Out-Null
Copy-Item -Path (Join-Path $root 'Builds\Windows\*') -Destination $folder -Recurse
Get-ChildItem $folder -Directory | Where-Object { $_.Name -like '*DoNotShip*' } | Remove-Item -Recurse -Force
[IO.File]::WriteAllText((Join-Path $folder 'Read me.txt'), @"
Ultra Puck $version

PLAYING
  1. Keep this whole folder together (unzip it somewhere like Documents, not Program Files).
  2. Run UltraPuck.exe. If Windows says "Windows protected your PC", click "More info" then "Run anyway"
     (the game isn't signed yet).
  3. The game updates itself: when a new version is out it says so, downloads it and restarts.

ONLINE
  Steam: have Steam running and logged in. Online > Host on Steam, then "Invite Steam friends" (or your friend
  uses Online > Find games on Steam, or joins from the Steam friends list). Steam will say you're playing
  "Spacewar" - that's Valve's test app, used until Ultra Puck has its own.
  By IP: Online > Host by IP on one PC, Online > Join by IP with that PC's address on the other.

CONTROLS
  Controller: left stick move, right stick aim, A / RB shoot or check, B / LB charge shot or brake,
  Start pause. Keyboard: WASD move, arrows aim, Space shoot or check, Shift charge or brake, Esc pause.
"@)
$zip = Join-Path $dist 'UltraPuck-Windows.zip'
Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
[IO.File]::WriteAllText((Join-Path $dist 'version.txt'), $version)
Write-Host "Made $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
python -I (Join-Path $PSScriptRoot 'pack-linux.py')
if ($LASTEXITCODE -ne 0) { throw "Packing the Linux build failed" }
$tar = Join-Path $dist 'UltraPuck-Linux.tar.gz'

if ($NoPublish) { return }
if ($Notes -eq '') { $Notes = "Ultra Puck $version" }
gh release create "v$version" $zip $tar (Join-Path $dist 'version.txt') --repo $repo --title "Ultra Puck $version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "Publishing the release failed" }
Write-Host "Published https://github.com/$repo/releases/tag/v$version"
