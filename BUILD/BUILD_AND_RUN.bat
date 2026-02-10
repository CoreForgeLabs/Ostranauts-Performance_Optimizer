@echo off
chcp 65001 >nul
title SaveForce + Run — Build and Launch

:: --- Auto-detect game path ---
set GAME_DIR=

:: 1. game_path.cfg in repo root (manual override)
if exist "%~dp0game_path.cfg" (
    set /p GAME_DIR=<"%~dp0game_path.cfg"
    if exist "%GAME_DIR%\Ostranauts_Data" goto :found
    set GAME_DIR=
)

:: 2. Steam registry
for /f "tokens=2*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1022980" /v InstallLocation 2^>nul') do set GAME_DIR=%%b
if defined GAME_DIR if exist "%GAME_DIR%\Ostranauts_Data" goto :found
set GAME_DIR=

:: 3. Default Steam library
for /f "tokens=2*" %%a in ('reg query "HKLM\SOFTWARE\WOW6432Node\Valve\Steam" /v InstallPath 2^>nul') do set STEAM_DIR=%%b
if defined STEAM_DIR (
    set GAME_DIR=%STEAM_DIR%\steamapps\common\Ostranauts
    if exist "%GAME_DIR%\Ostranauts_Data" goto :found
    set GAME_DIR=
)

echo ERROR: Ostranauts not found!
echo Create game_path.cfg with the game path, e.g.:
echo   echo C:\SteamLibrary\steamapps\common\Ostranauts ^> game_path.cfg
pause
exit /b 1

:found
echo Game path: %GAME_DIR%
set MANAGED=%GAME_DIR%\Ostranauts_Data\Managed
set BEPINEX=%GAME_DIR%\BepInEx\core
set PLUGIN_DIR=%GAME_DIR%\BepInEx\plugins

echo ============================================
echo  SaveForce + Run — Build ^& Launch
echo ============================================

:: Step 1: Kill running Ostranauts
echo.
echo [1/4] Killing running Ostranauts instances...
taskkill /F /IM Ostranauts.exe >nul 2>&1
if %errorlevel%==0 (
    echo       Killed!
    timeout /t 2 /nobreak >nul
) else (
    echo       No running instance found.
)

:: Find C# compiler
set CSC=
where csc >nul 2>&1
if %errorlevel%==0 (
    set CSC=csc
) else (
    if exist "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
        set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    ) else if exist "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
        set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
    )
)

if "%CSC%"=="" (
    echo ERROR: C# compiler not found!
    pause
    exit /b 1
)

set REFS=/reference:"%MANAGED%\mscorlib.dll" /reference:"%MANAGED%\Assembly-CSharp.dll" /reference:"%MANAGED%\UnityEngine.dll" /reference:"%MANAGED%\UnityEngine.UI.dll" /reference:"%BEPINEX%\0Harmony.dll" /reference:"%BEPINEX%\BepInEx.dll" /reference:"%MANAGED%\System.dll" /reference:"%MANAGED%\System.Core.dll"

:: Step 2: Build SaveForce
echo.
echo [2/4] Building SaveForce plugin...
"%CSC%" /target:library /optimize+ /nologo /nostdlib /noconfig /out:"%PLUGIN_DIR%\SaveForce.dll" %REFS% "%~dp0src\SaveForcePlugin.cs"
if %errorlevel% neq 0 (
    echo       SaveForce BUILD FAILED!
    pause
    exit /b 1
)
echo       SaveForce.dll OK

:: Step 3: Build Run
echo.
echo [3/4] Building Run plugin...
"%CSC%" /target:library /optimize+ /nologo /nostdlib /noconfig /out:"%PLUGIN_DIR%\Run.dll" %REFS% "%~dp0src\RunPlugin.cs"
if %errorlevel% neq 0 (
    echo       Run BUILD FAILED!
    pause
    exit /b 1
)
echo       Run.dll OK

:: Step 4: Launch game via Steam
echo.
echo [4/4] Launching Ostranauts via Steam...
start "" "steam://rungameid/1022980"
echo       Game starting...
echo.
echo ============================================
echo  Done! Game will auto-load your last save.
echo ============================================
timeout /t 5