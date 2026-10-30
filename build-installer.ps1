# Publishes MediaForge.exe and wraps it in an Inno Setup installer:
#   .\build-installer.ps1                  ->  installer\Output\MediaForge-Setup-<version>.exe
#   .\build-installer.ps1 -Version 1.2.3   override the version from MediaForge.csproj
# Needs Inno Setup 6 (winget install JRSoftware.InnoSetup).
param([string]$Version)
$ErrorActionPreference = 'Stop'

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue)?.Source
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'ISCC.exe not found. Install Inno Setup 6: winget install JRSoftware.InnoSetup' }

if (-not $Version) {
    [xml]$csproj = Get-Content (Join-Path $PSScriptRoot 'src\MediaForge\MediaForge.csproj')
    $Version = ($csproj.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot 'installer\MediaForge.iss')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "`nDone: $(Join-Path $PSScriptRoot "installer\Output\MediaForge-Setup-$Version.exe")"
