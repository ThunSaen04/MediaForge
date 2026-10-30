# Writes the winget manifests for a released version and validates them:
#   .\winget\winget-manifest.ps1                 version from MediaForge.csproj
#   .\winget\winget-manifest.ps1 -Submit         also opens a pull request to microsoft/winget-pkgs
# Run it after release.ps1: the installer must already be on the GitHub release.
# -Submit needs wingetcreate (winget install Microsoft.WingetCreate) and the GitHub CLI logged in.
param([string]$Version, [switch]$Submit)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$repo = 'ThunSaen04/MediaForge'
$id = 'Siammods.MediaForge'
$schema = '1.10.0'

if (-not $Version) {
    [xml]$csproj = Get-Content (Join-Path $root 'src\MediaForge\MediaForge.csproj')
    $Version = ($csproj.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
}
$fileName = "MediaForge-Setup-$Version.exe"
$url = "https://github.com/$repo/releases/download/v$Version/$fileName"

# Hash the file that is actually on the release, not a local rebuild.
$tmp = Join-Path ([IO.Path]::GetTempPath()) "winget-$fileName"
Invoke-WebRequest $url -OutFile $tmp -UseBasicParsing
$sha = (Get-FileHash $tmp -Algorithm SHA256).Hash
Remove-Item $tmp

$released = (gh release view "v$Version" --repo $repo --json publishedAt --jq .publishedAt)
$releaseDate = ([datetime]$released).ToString('yyyy-MM-dd')
$productCode = '{6E0F2C1B-8D5A-4B7E-9C3F-2A1D7E4B9F60}_is1'   # Inno AppId + "_is1"

$dir = Join-Path $PSScriptRoot "manifests\s\Siammods\MediaForge\$Version"
New-Item -ItemType Directory -Force $dir | Out-Null

function Write-Manifest([string]$name, [string]$type, [string]$body) {
    $text = "# yaml-language-server: `$schema=https://aka.ms/winget-manifest.$type.$schema.schema.json`n`n$body".TrimEnd() + "`n"
    [IO.File]::WriteAllText((Join-Path $dir $name), $text, [Text.UTF8Encoding]::new($false))
}

Write-Manifest "$id.yaml" 'version' @"
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@

Write-Manifest "$id.installer.yaml" 'installer' @"
PackageIdentifier: $id
PackageVersion: $Version
Platform:
- Windows.Desktop
MinimumOSVersion: 10.0.17763.0
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '$productCode'
ReleaseDate: $releaseDate
AppsAndFeaturesEntries:
- DisplayName: MediaForge
  Publisher: Siammods.com
  ProductCode: '$productCode'
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@

Write-Manifest "$id.locale.en-US.yaml" 'defaultLocale' @"
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Siammods.com
PublisherUrl: https://siammods.com
PublisherSupportUrl: https://github.com/$repo/issues
Author: Siammods.com
PackageName: MediaForge
PackageUrl: https://github.com/$repo
License: Freeware
Copyright: © 2026 Siammods.com
ShortDescription: Convert, trim and merge video and audio, edit tags and subtitles, and download from YouTube.
Description: |-
  MediaForge is a Windows desktop app built on FFmpeg and yt-dlp.
  Convert video to audio and between audio and video formats (with GPU encoding), trim, merge, normalize loudness,
  change speed, make GIFs, edit music tags, cover art and subtitles, and download single videos or whole
  YouTube playlists. The interface is available in Thai and English.
Tags:
- audio
- converter
- ffmpeg
- media
- video
- youtube
- yt-dlp
ReleaseNotesUrl: https://github.com/$repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@

Write-Manifest "$id.locale.th-TH.yaml" 'locale' @"
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: th-TH
Publisher: Siammods.com
PackageName: MediaForge
License: ฟรีแวร์
ShortDescription: แปลง ตัด และรวมไฟล์วิดีโอ/เสียง แก้แท็กและซับไตเติล และดาวน์โหลดจาก YouTube
Description: |-
  MediaForge เป็นโปรแกรมบน Windows ที่ทำงานด้วย FFmpeg และ yt-dlp
  แปลงวิดีโอเป็นเสียง แปลงไฟล์เสียงและวิดีโอเป็นรูปแบบอื่น (ใช้การ์ดจอช่วยได้) ตัดช่วง รวมไฟล์ ปรับความดัง
  ปรับความเร็ว ทำ GIF แก้แท็กเพลง ภาพปก และซับไตเติล และดาวน์โหลดคลิปหรือทั้ง playlist จาก YouTube
  หน้าโปรแกรมเลือกได้ทั้งภาษาไทยและอังกฤษ
ManifestType: locale
ManifestVersion: $schema
"@

winget validate --manifest $dir
if ($LASTEXITCODE -ne 0) { throw "winget validate failed for $dir" }
Write-Host "`nManifests: $dir"

if ($Submit) {
    if (-not (Get-Command wingetcreate -ErrorAction SilentlyContinue)) {
        throw 'wingetcreate not found. Install it: winget install Microsoft.WingetCreate'
    }
    # wingetcreate reads the token from this variable, so it never appears on a command line or in logs.
    $env:WINGET_CREATE_GITHUB_TOKEN = (gh auth token)
    try {
        wingetcreate submit --prtitle "$id $Version" $dir
    } finally {
        Remove-Item Env:WINGET_CREATE_GITHUB_TOKEN
    }
}
