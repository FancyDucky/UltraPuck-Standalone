@echo off
title Ultra Puck — Sync cards and maps
cd /d "%~dp0"
echo Copying StreamingAssets into the Windows build (no Unity compile)…
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\sync-streaming.ps1"
if errorlevel 1 (
  echo.
  pause
  exit /b 1
)
echo.
pause
