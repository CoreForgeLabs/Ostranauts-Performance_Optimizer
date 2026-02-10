# SaveForce + Run — Quick build + kill + launch
# Usage: .\run.ps1 [-NoBuild] [-NoLaunch] [-Kill]
param(
    [switch]$NoBuild,
    [switch]$NoLaunch,
    [switch]$Kill
)

$ErrorActionPreference = "Stop"

# --- Auto-detect game path ---
function Find-GamePath {
    # 1. game_path.cfg in repo root (manual override)
    $cfgFile = Join-Path $PSScriptRoot "game_path.cfg"
    if (Test-Path $cfgFile) {
        $p = (Get-Content $cfgFile -First 1).Trim()
        if ($p -and (Test-Path "$p\Ostranauts_Data")) { return $p }
    }
    # 2. Steam registry (most reliable)
    $reg = Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1022980" -ErrorAction SilentlyContinue
    if ($reg -and $reg.InstallLocation -and (Test-Path "$($reg.InstallLocation)\Ostranauts_Data")) {
        return $reg.InstallLocation
    }
    # 3. Common Steam library paths
    $steamRoot = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -ErrorAction SilentlyContinue).InstallPath
    if ($steamRoot) {
        $candidate = Join-Path $steamRoot "steamapps\common\Ostranauts"
        if (Test-Path "$candidate\Ostranauts_Data") { return $candidate }
    }
    return $null
}

$GAME = Find-GamePath
if (-not $GAME) {
    Write-Host "ERROR: Ostranauts not found! Create game_path.cfg with the game path." -ForegroundColor Red
    Write-Host "Example: echo C:\SteamLibrary\steamapps\common\Ostranauts > game_path.cfg" -ForegroundColor Yellow
    exit 1
}
Write-Host "[PATH] $GAME" -ForegroundColor DarkGray

$MANAGED = "$GAME\Ostranauts_Data\Managed"
$BEPINEX = "$GAME\BepInEx\core"
$PLUGINS = "$GAME\BepInEx\plugins"
$CSC = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

$REFS = @(
    "$MANAGED\mscorlib.dll",
    "$MANAGED\Assembly-CSharp.dll",
    "$MANAGED\UnityEngine.dll",
    "$MANAGED\UnityEngine.UI.dll",
    "$BEPINEX\0Harmony.dll",
    "$BEPINEX\BepInEx.dll",
    "$MANAGED\System.dll",
    "$MANAGED\System.Core.dll"
)

# Kill running instances
Write-Host "[KILL] " -NoNewline -ForegroundColor Red
$killed = Get-Process -Name "Ostranauts" -ErrorAction SilentlyContinue
if ($killed) {
    $killed | Stop-Process -Force
    Write-Host "Killed $($killed.Count) instance(s)" -ForegroundColor Yellow
    Start-Sleep -Seconds 3
} else {
    Write-Host "No running instances" -ForegroundColor DarkGray
}

if ($Kill) { exit 0 }

function Build-Plugin($Name, $SourceFile, $OutputDll) {
    Write-Host "[BUILD] $Name... " -NoNewline -ForegroundColor Cyan
    $refArgs = $REFS | ForEach-Object { "/reference:$_" }
    & $CSC /target:library /optimize+ /nologo /nostdlib /noconfig /out:"$OutputDll" @refArgs "$SourceFile"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED!" -ForegroundColor Red
        exit 1
    }
    $dll = Get-Item $OutputDll
    Write-Host "OK ($([math]::Round($dll.Length/1KB, 1)) KB)" -ForegroundColor Green
}

# Build
if (-not $NoBuild) {
    Build-Plugin "SaveForce" "$PSScriptRoot\src\SaveForcePlugin.cs" "$PLUGINS\SaveForce.dll"
    Build-Plugin "Run" "$PSScriptRoot\src\RunPlugin.cs" "$PLUGINS\Run.dll"
}

# Launch
if (-not $NoLaunch) {
    Write-Host "[LAUNCH] " -NoNewline -ForegroundColor Green
    Start-Process "steam://rungameid/1022980"
    Write-Host "Game starting via Steam..." -ForegroundColor Green
}