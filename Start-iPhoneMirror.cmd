@echo off
rem Personal version launcher: choose which branch build of iPhoneMirror to start.
rem Running it from the repo also installs/updates the launcher copy and its desktop shortcut.
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0tools\version-launcher\iPhoneMirrorLauncher.ps1" -RepoPath "%~dp0."
