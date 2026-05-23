@echo off
setlocal
title UNITY SAFE PRO UPLOADER
color 0A

set LOG=upload_log.txt
echo START %date% %time% > %LOG%

cls
echo ==========================================
echo        UNITY SAFE UPLOADER
echo ==========================================
echo.

echo [1] Checking Git...
git --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Git missing >> %LOG%
    pause
    exit /b
)

echo [2] Resetting safe state (important)...

:: 🔥 THIS FIXES 7% FREEZE PROBLEM
git rm -r --cached . >nul 2>&1

echo [3] Creating Unity .gitignore...

(
echo Library/
echo Temp/
echo Obj/
echo Logs/
echo Builds/
echo UserSettings/
echo MemoryCaptures/
echo .vs/
echo *.csproj
echo *.unityproj
echo *.sln
) > .gitignore

echo [4] Initializing repo if needed...
if not exist ".git" git init

git branch -M main

echo [5] Adding CLEAN project only...
git add Assets ProjectSettings Packages .gitignore

if errorlevel 1 (
    echo ERROR during git add >> %LOG%
    echo FAILED ADD STAGE
    pause
    exit /b
)

echo [6] Committing...
git commit -m "clean unity upload" >> %LOG%

echo [7] Pushing (this is where real progress shows)...

git push origin main

if errorlevel 1 (
    color 0C
    echo PUSH FAILED >> %LOG%
    echo ERROR uploading project
    pause
    exit /b
)

echo.
color 0A
echo ==========================================
echo SUCCESS - NO FREEZE UPLOAD COMPLETE
echo ==========================================
pause