@echo off
title Ultra Puck - Card editor
cd /d "%~dp0"
echo Opening the card editor in your browser. Close this window to stop it.
python -I "%~dp0Tools\card_editor.py"
if errorlevel 1 pause
