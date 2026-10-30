<p align="center"><img src="assets/logo.png" width="96" alt="MediaForge logo"></p>

<h1 align="center">MediaForge</h1>

<p align="center">
Convert, edit and download video &amp; audio on Windows<br>
Developed by <a href="https://siammods.com">Siammods.com</a> · © 2026 Siammods.com<br>
<b>English</b> · <a href="README.th.md">ภาษาไทย</a>
</p>

MediaForge is a Windows desktop app built with **C# 14 / .NET 10 (LTS) + WPF (Fluent theme)**.
The heavy lifting is done by **FFmpeg** and **yt-dlp**. The interface is available in Thai and English,
with light/dark themes that follow Windows and your Windows accent color.

## Features

| Page | What it does |
|---|---|
| **Convert** | Video → audio, audio → audio (MP3, M4A/AAC, Opus, OGG, WMA, FLAC, WAV, ALAC, AIFF), "original audio" without re-encoding · video → video (MP4/MKV/MOV/WebM/AVI with H.264, H.265, VP9, AV1, MPEG-4), quality, resolution and FPS · container-only remux · GPU encoding (NVIDIA NVENC / Intel QSV / AMD AMF when available) · drag in files or whole folders (batch) |
| **Edit** | Trim (fast, no re-encode, or frame-accurate) · merge (fast, or re-encode files of different sizes/formats) · EBU R128 loudness normalization (2-pass) or ±dB gain · speed 0.25x–4x with the pitch kept · high-quality GIFs |
| **YouTube** | Paste several links, single videos or playlists · analyze and pick the entries you want · download as video (resolution, MP4/MKV/WebM) or audio (MP3/M4A/Opus/FLAC/WAV) · embed cover, metadata and subtitles · numbered file names and a folder per playlist |
| **Tags / Cover / Subtitles** | File details (ffprobe) · edit music tags · replace, remove or save cover art (MP3, M4A, FLAC) · extract subtitles as SRT/ASS/VTT · add soft subtitles · burn subtitles into the picture · Thai TIS-620/Windows-874 subtitles are converted automatically |
| **Settings** | Appearance (language, theme) · FFmpeg / yt-dlp versions, download or update them in the app · JavaScript runtime for yt-dlp · parallel jobs · app updates · about and credits |

Every task goes into the **queue** at the bottom: jobs run in parallel (separate limits for conversions and downloads),
show progress and time left, and can be cancelled (the whole process tree is stopped and partial files are removed),
retried, opened in Explorer or inspected in a log.

## Install

**Requirements:** Windows 10 (1809) or Windows 11, x64. No .NET installation is needed.

### Option 1: Installer from GitHub Releases

1. Download `MediaForge-Setup-<version>.exe` from the [latest release](https://github.com/ThunSaen04/MediaForge/releases/latest).
2. Run it. It installs for the current user only (`%LOCALAPPDATA%\Programs\MediaForge`), with no admin prompt.
3. Pick Thai or English for the setup wizard, and optionally a desktop shortcut.

> **"Windows protected your PC"?** The installer is not code-signed yet, so Microsoft SmartScreen may warn about it.
> Click **More info → Run anyway**. You can check that the file is genuine against the SHA-256 published with each release
> (`MediaForge-Setup-<version>.exe.sha256`).

### Option 2: winget

```powershell
winget install Siammods.MediaForge
```

Upgrade later with `winget upgrade Siammods.MediaForge`.
(Available once the package has been accepted into the [winget community repository](https://github.com/microsoft/winget-pkgs).)

### External tools

- **FFmpeg**: if it is not on your PATH, MediaForge offers to download it (BtbN build) into `%LOCALAPPDATA%\MediaForge\tools`.
- **yt-dlp**: downloaded by the app and updatable from Settings.
- **Deno or Node.js 22+**: newer yt-dlp versions need a JavaScript runtime to get every YouTube format.
  MediaForge finds one automatically (Deno first, then Node.js), or download Deno from Settings.

## Updates

MediaForge checks [GitHub Releases](https://github.com/ThunSaen04/MediaForge/releases) when it starts (at most once a day)
and from **Settings → About → Check for updates**. You can turn off the automatic check there.

When a new version is out, a banner shows the version and release notes. **Update now** downloads the installer,
verifies its SHA-256 against the release, closes MediaForge, installs over the current version (settings are kept)
and starts the new version. If the checksum does not match, nothing is installed.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project src/MediaForge
```

Build a single `MediaForge.exe` into `publish\`:

```powershell
.\publish.ps1                      # self-contained, ~67 MB, runs without .NET installed
.\publish.ps1 -FrameworkDependent  # small exe, needs the .NET 10 Desktop Runtime
```

Build the installer (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`):

```powershell
.\build-installer.ps1              # -> installer\Output\MediaForge-Setup-<version>.exe
```

### Smoke tests

The tests generate sample media with ffmpeg, run every feature through the real code and check the results with ffprobe
(Thai file names, 5.1 audio, TIS-620 subtitles, GPU encoders and cancellation included).

```powershell
dotnet run --project tests/MediaForge.SmokeTests -- <work folder>                # convert / edit / tags / subtitles
dotnet run --project tests/MediaForge.SmokeTests -- <work folder> --youtube      # also YouTube (needs internet)
dotnet run --project tests/MediaForge.SmokeTests -- <work folder> --filter=merge # only tests whose name contains "merge"
```

## Releasing a new version

1. Bump `<Version>` in `src/MediaForge/MediaForge.csproj`, then commit and push.
2. Publish the GitHub release. Installed copies are offered the update from this release:
   ```powershell
   .\release.ps1 -Notes "What changed"      # or -NotesFile notes.md; add -PreRelease to hold it back from users
   ```
3. Update winget (needs `winget install Microsoft.WingetCreate`):
   ```powershell
   .\winget\winget-manifest.ps1 -Submit     # writes + validates the manifests and opens a PR to microsoft/winget-pkgs
   ```

The app and icons come from `assets\logo.png` (generated with GPT through the Codex CLI).
Rebuild them with `pwsh .\assets\make-icons.ps1`.

## Project layout

```
src/MediaForge/
  Core/           UI-independent: ProcessRunner, Tools (find/install ffmpeg, yt-dlp), MediaProbe (ffprobe),
                  FFmpeg (run + progress), Converter (every ffmpeg command), YtDlp, Updater, Job, Presets, AppInfo
  ViewModels/     MVVM: one per page + QueueViewModel (job scheduler) + UpdateViewModel
  Views/          XAML for each page
  Controls/       FileBox (path box with browse and drag & drop)
  Localization/   Loc + Strings (all Thai/English text), {l:Tr Key} markup extension
  Themes/         Styles.xaml: cards, nav items, segmented tabs, text styles (Fluent theme colors)
  UI/             Dialogs, converters, Icon.Glyph, log/download windows, ThemeHelper
  Assets/         MediaForge.ico, logo.png
tests/MediaForge.SmokeTests/   end-to-end console tests
installer/        Inno Setup script
winget/           winget manifest generator (manifests/ is generated)
assets/           logo source + make-icons.ps1
```

## Files the app creates

- Settings: `%APPDATA%\MediaForge\settings.json`
- Downloaded tools: `%LOCALAPPDATA%\MediaForge\tools\`
- Temporary files and update downloads: `%TEMP%\MediaForge\`
- YouTube downloads (default): `%USERPROFILE%\Downloads\MediaForge\`

Uninstalling keeps your settings and downloaded tools; delete the folders above to remove them too.

## Notes

- If YouTube downloads fail, press **Update yt-dlp** in Settings first; YouTube changes often.
- Large static FFmpeg builds (for example gyan "full", ~217 MB) take about a second to start each process,
  so every job has a short start-up delay that does not come from MediaForge.
- Only download content you have the right to use, following YouTube's terms and copyright law.

## Credits

Developed by **[Siammods.com](https://siammods.com)**.

MediaForge uses these open-source programs, downloaded separately at run time. Each belongs to its authors and keeps its own license:

- [FFmpeg](https://ffmpeg.org): LGPL/GPL
- [yt-dlp](https://github.com/yt-dlp/yt-dlp): Unlicense
- [Deno](https://deno.com): MIT
