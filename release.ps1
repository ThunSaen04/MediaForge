# Builds the installer and publishes it as a GitHub release, which installed copies pick up as an update.
#   1. Bump <Version> in src\MediaForge\MediaForge.csproj, commit and push.
#   2. .\release.ps1 -Notes "What changed"          (or -NotesFile notes.md)
# Add -PreRelease to publish without offering it to users yet (the app ignores pre-releases).
# Needs: Inno Setup 6, GitHub CLI logged in (gh auth login) with access to the repository.
param(
    [string]$Notes,
    [string]$NotesFile,
    [switch]$PreRelease
)
$ErrorActionPreference = 'Stop'
$repo = 'ThunSaen04/MediaForge'   # keep in sync with Updater.Repository

if (-not $Notes -and -not $NotesFile) { throw 'Give release notes with -Notes "..." or -NotesFile <file>.' }

[xml]$csproj = Get-Content (Join-Path $PSScriptRoot 'src\MediaForge\MediaForge.csproj')
$version = ($csproj.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
$tag = "v$version"

# The tag must point at code that is committed and pushed.
if (git -C $PSScriptRoot status --porcelain) { throw 'There are uncommitted changes. Commit and push before releasing.' }
git -C $PSScriptRoot fetch --quiet
$head = git -C $PSScriptRoot rev-parse HEAD
if ($head -ne (git -C $PSScriptRoot rev-parse '@{u}')) { throw 'HEAD is not pushed. Run git push first.' }

gh release view $tag --repo $repo *> $null
if ($LASTEXITCODE -eq 0) { throw "Release $tag already exists. Bump <Version> in MediaForge.csproj." }

& (Join-Path $PSScriptRoot 'build-installer.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$setup = Join-Path $PSScriptRoot "installer\Output\MediaForge-Setup-$version.exe"
# GitHub also publishes an asset digest; the .sha256 file is the fallback the app reads.
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
$hashFile = "$setup.sha256"
Set-Content $hashFile "$hash  $(Split-Path $setup -Leaf)" -NoNewline -Encoding ascii

$ghArgs = @('release', 'create', $tag, $setup, $hashFile, '--repo', $repo, '--target', $head, '--title', "MediaForge $version")
$ghArgs += if ($NotesFile) { @('--notes-file', $NotesFile) } else { @('--notes', $Notes) }
if ($PreRelease) { $ghArgs += '--prerelease' }
gh @ghArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "`nReleased $tag  sha256 $hash"
