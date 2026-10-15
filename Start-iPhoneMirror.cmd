@echo off
rem Personal version launcher: choose which branch build of iPhoneMirror to start.
rem Running it from the repo also installs/updates the launcher copy and its desktop shortcut.
rem A copy of this file elsewhere (e.g. on the desktop) opens the installed launcher.
set "LAUNCHER=%~dp0tools\version-launcher\iPhoneMirrorLauncher.ps1"
set "REPO=%~dp0."
if not exist "%LAUNCHER%" (
    set "LAUNCHER=%LOCALAPPDATA%\iPhoneMirrorVersions\launcher\iPhoneMirrorLauncher.ps1"
    set "REPO="
)
if not exist "%LAUNCHER%" (
    echo Launcher nicht gefunden. Bitte Start-iPhoneMirror.cmd einmal im iPhoneMirror-Repo ausfuehren.
    pause
    exit /b 1
)
if defined REPO (
    start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%LAUNCHER%" -RepoPath "%REPO%"
) else (
    start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%LAUNCHER%"
)
