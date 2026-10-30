# Builds a single MediaForge.exe into .\publish
#   .\publish.ps1                      self-contained (runs on any Windows 10/11 x64, ~70 MB)
#   .\publish.ps1 -FrameworkDependent  small exe that needs the .NET 10 Desktop Runtime installed
#   .\publish.ps1 -Version 1.2.3       override the version from MediaForge.csproj
param([switch]$FrameworkDependent, [string]$Version)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\MediaForge\MediaForge.csproj'
$output = Join-Path $PSScriptRoot 'publish'

$dotnetArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '-o', $output,
    '-p:PublishSingleFile=true',
    '-p:PublishReadyToRun=true',
    '-p:DebugType=none'
)
if ($FrameworkDependent) {
    $dotnetArgs += '--self-contained', 'false'
} else {
    $dotnetArgs += '--self-contained', 'true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true'
}

if ($Version) { $dotnetArgs += "-p:Version=$Version" }

dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "`nDone: $(Join-Path $output 'MediaForge.exe')"
