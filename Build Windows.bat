@echo off
title Ultra Puck — Build Windows
cd /d "%~dp0"
echo Building Ultra Puck (Windows)…
echo Close the Unity editor if it has this project open.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\build-windows.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. See build.log
  pause
  exit /b 1
)
echo.
pause
