@echo off
title iPod Player
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ipod-server.ps1" %*
