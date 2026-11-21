@echo off
title Ultra Puck — Release
cd /d "%~dp0"
echo Ultra Puck release
echo.
echo Close the Unity editor if it has this project open.
echo This bumps the version, builds Windows + Linux, publishes on GitHub,
echo then commits and pushes.
echo.
set /p NOTES=What changed (plain English): 
if "%NOTES%"=="" (
  echo Need a short note for the release.
  pause
  exit /b 1
)
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\release.ps1" -Bump -Notes "%NOTES%"
if errorlevel 1 (
  echo.
  echo Release failed. See build.log / build-linux.log
  pause
  exit /b 1
)
for /f "usebackq delims=" %%v in ("version.txt") do set VER=%%v
echo.
echo Committing and pushing %VER%…
git add -A
git commit -m "%VER%: %NOTES%"
if errorlevel 1 (
  echo.
  echo Nothing to commit, or commit failed. Release zip is on GitHub already.
  pause
  exit /b 1
)
git push
if errorlevel 1 (
  echo.
  echo Push failed. Commit is local; push when you can.
  pause
  exit /b 1
)
echo.
echo Done: https://github.com/FancyDucky/UltraPuck-Standalone/releases/tag/v%VER%
echo.
pause
