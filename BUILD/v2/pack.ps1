# pack.ps1 — Build + package SaveForce distribution archive
# Usage: .\pack.ps1
# Output: SaveForce-vX.X.X.zip (extracts into Ostranauts game folder)

$ErrorActionPreference = "Stop"

# --- Auto-detect game path (same as run.ps1) ---
function Find-GamePath {
    $cfgFile = Join-Path $PSScriptRoot "game_path.cfg"
    if (Test-Path $cfgFile) {
        $p = (Get-Content $cfgFile -First 1).Trim()
        if ($p -and (Test-Path "$p\Ostranauts_Data")) { return $p }
    }
    $reg = Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1022980" -ErrorAction SilentlyContinue
    if ($reg -and $reg.InstallLocation -and (Test-Path "$($reg.InstallLocation)\Ostranauts_Data")) {
        return $reg.InstallLocation
    }
    $steamRoot = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -ErrorAction SilentlyContinue).InstallPath
    if ($steamRoot) {
        $candidate = Join-Path $steamRoot "steamapps\common\Ostranauts"
        if (Test-Path "$candidate\Ostranauts_Data") { return $candidate }
    }
    return $null
}

$GAME = Find-GamePath
if (-not $GAME) {
    Write-Host "ERROR: Ostranauts not found!" -ForegroundColor Red; exit 1
}
Write-Host "[PATH] $GAME" -ForegroundColor DarkGray

$MANAGED  = "$GAME\Ostranauts_Data\Managed"
$BEPINEX  = "$GAME\BepInEx\core"
$PLUGINS  = "$GAME\BepInEx\plugins"
$CSC      = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

$REFS = @(
    "$MANAGED\mscorlib.dll", "$MANAGED\Assembly-CSharp.dll",
    "$MANAGED\UnityEngine.dll", "$MANAGED\UnityEngine.UI.dll",
    "$BEPINEX\0Harmony.dll", "$BEPINEX\BepInEx.dll",
    "$MANAGED\System.dll", "$MANAGED\System.Core.dll"
)

# --- Step 1: Build DLLs ---
function Build-Plugin($Name, $Src, $Out) {
    Write-Host "[BUILD] $Name... " -NoNewline -ForegroundColor Cyan
    $refArgs = $REFS | ForEach-Object { "/reference:$_" }
    & $CSC /target:library /optimize+ /nologo /nostdlib /noconfig /out:"$Out" @refArgs "$Src"
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED!" -ForegroundColor Red; exit 1 }
    $sz = [math]::Round((Get-Item $Out).Length / 1KB, 1)
    Write-Host "OK ($sz KB)" -ForegroundColor Green
}

Build-Plugin "SaveForce" "$PSScriptRoot\src\SaveForcePlugin.cs" "$PLUGINS\SaveForce.dll"
Build-Plugin "Run"       "$PSScriptRoot\src\RunPlugin.cs"       "$PLUGINS\Run.dll"

# --- Step 2: Read version from SaveForcePlugin.cs ---
$verLine = Select-String -Path "$PSScriptRoot\src\SaveForcePlugin.cs" -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
$version = if ($verLine) { $verLine.Matches[0].Groups[1].Value } else { "dev" }
Write-Host "[VER]  v$version" -ForegroundColor Yellow

# --- Step 3: Assemble staging folder ---
$staging = Join-Path $PSScriptRoot "dist"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

# BepInEx core (DLLs only, no XMLs)
$coreDir = New-Item "$staging\BepInEx\core" -ItemType Directory -Force
Get-ChildItem "$BEPINEX\*.dll" | Copy-Item -Destination $coreDir

# Our plugins
$plugDir = New-Item "$staging\BepInEx\plugins" -ItemType Directory -Force
Copy-Item "$PLUGINS\SaveForce.dll"          -Destination $plugDir
Copy-Item "$PLUGINS\Run.dll"                -Destination $plugDir
Copy-Item "$PLUGINS\OstronautsOptimizer.dll" -Destination $plugDir

# BepInEx bootstrap (root)
Copy-Item "$GAME\doorstop_config.ini" -Destination $staging
Copy-Item "$GAME\winhttp.dll"         -Destination $staging

# RUNSAVE.bat
Copy-Item "$GAME\RUNSAVE.bat" -Destination $staging

# --- Step 4: Create ZIP ---
$zipName = "SaveForce-v$version.zip"
$zipPath = Join-Path $PSScriptRoot $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath }
Compress-Archive -Path "$staging\*" -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " Archive: $zipName ($zipSize KB)" -ForegroundColor Green
Write-Host " Extract into Ostranauts game folder." -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green

# Cleanup staging
Remove-Item $staging -Recurse -Force