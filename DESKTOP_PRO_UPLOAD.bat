@echo off
setlocal enabledelayedexpansion
title DESKTOP PRO GITHUB UPLOADER
color 0A

set LOG=desktop_pro_log.txt
echo ===== START %date% %time% ===== > %LOG%

:: =========================
:: ANTI DOUBLE RUN LOCK
:: =========================
if exist "%temp%\git_uploader.lock" (
    echo ERROR: Another upload is already running.
    pause
    exit /b
)
echo running > "%temp%\git_uploader.lock"

cls
echo ==========================================
echo        DESKTOP PRO UPLOADER
echo ==========================================
echo.

:: =========================
:: CHECK TOOLS
:: =========================
echo [1] Checking Git...
git --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Git missing >> %LOG%
    del "%temp%\git_uploader.lock"
    pause
    exit /b
)

echo [2] Checking GitHub CLI...
gh --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: GitHub CLI missing >> %LOG%
    del "%temp%\git_uploader.lock"
    pause
    exit /b
)

echo [3] Checking login...
gh auth status >nul 2>&1
if errorlevel 1 (
    gh auth login
)

:: =========================
:: INIT GIT
:: =========================
echo [4] Preparing repo...
if not exist ".git" git init >> %LOG%

git branch -M main >> %LOG% 2>&1

:: =========================
:: FILE COUNT
:: =========================
for /f %%A in ('dir /a /s /b ^| find /c /v ""') do set TOTAL=%%A

echo Total files: %TOTAL%
echo TOTAL=%TOTAL% >> %LOG%

:: =========================
:: UNITY SAFE IGNORE CHECK
:: =========================
if not exist ".gitignore" (
    echo Creating Unity .gitignore...
    echo Library/>>.gitignore
    echo Temp/>>.gitignore
    echo Obj/>>.gitignore
    echo Build/>>.gitignore
    echo Builds/>>.gitignore
    echo Logs/>>.gitignore
    echo UserSettings/>>.gitignore
)

:: =========================
:: ADD FILES WITH VISUAL PROGRESS
:: =========================
echo.
echo [5] Adding files (safe mode)...

set /a COUNT=0

for /f "delims=" %%F in ('git ls-files --others --exclude-standard ^& git ls-files') do (
    set /a COUNT+=1
    set /a PERCENT=COUNT*100/TOTAL

    echo [!PERCENT!%%] Processing files...

    git add "%%F" >> %LOG% 2>&1
)

echo DONE adding files >> %LOG%

:: =========================
:: COMMIT
:: =========================
echo.
echo [6] Committing...
git commit -m "desktop pro upload" >> %LOG% 2>&1

:: =========================
:: PUSH WITH AUTO RETRY
:: =========================
echo.
echo [7] Uploading to GitHub...

set TRIES=0

:pushloop
set /a TRIES+=1

echo Attempt !TRIES!/3...
git push origin main --progress >> %LOG% 2>&1

if errorlevel 1 (
    if !TRIES! LSS 3 (
        echo Retrying...
        timeout /t 3 >nul
        goto pushloop
    ) else (
        echo PUSH FAILED >> %LOG%
        color 0C
        echo ERROR: Upload failed after 3 attempts
        del "%temp%\git_uploader.lock"
        pause
        exit /b
    )
)

:: =========================
:: DONE
:: =========================
del "%temp%\git_uploader.lock"

color 0A
echo.
echo ==========================================
echo SUCCESS - DESKTOP PRO UPLOAD COMPLETE
echo ==========================================
echo Repo: https://github.com/DALI951/MiniAgeUnityFiles
echo Log: desktop_pro_log.txt
echo ==========================================
pause