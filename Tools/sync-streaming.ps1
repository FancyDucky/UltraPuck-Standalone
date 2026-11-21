# Copies Assets/StreamingAssets into the Windows build (cards, maps, card art).
# Use this after editing JSON/art when you only need the built exe to see the new files —
# no Unity rebuild. Face text and pictures update; gameplay numbers in Core/Tuning.cs still
# need Build Windows.bat (a full compile).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'Assets\StreamingAssets'
$dst = Join-Path $root 'Builds\Windows\UltraPuck_Data\StreamingAssets'
if (!(Test-Path $src)) { throw "Missing $src" }
if (!(Test-Path (Join-Path $root 'Builds\Windows\UltraPuck.exe'))) {
    throw "No Windows build yet. Run Build Windows.bat once first."
}
New-Item -ItemType Directory -Force -Path $dst | Out-Null
robocopy $src $dst /MIR /E /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null
# robocopy exit codes 0–7 are success
if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE)" }
Write-Host "Synced StreamingAssets -> Builds\Windows\UltraPuck_Data\StreamingAssets"
Write-Host "Card face text and maps are updated. Rules numbers (slow, range, cooldowns in play) still come from code — use Build Windows.bat after those change."
