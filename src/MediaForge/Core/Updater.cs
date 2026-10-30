using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace MediaForge.Core;

/// <summary>A newer release found on GitHub.</summary>
public sealed record UpdateInfo(
    Version Version,
    string Notes,
    string PageUrl,
    string DownloadUrl,
    string FileName,
    long Size,
    string Sha256);

/// <summary>
/// Checks GitHub Releases for a newer installer and runs it.
/// Release layout (made by release.ps1): tag "v1.2.3" with the asset "MediaForge-Setup-1.2.3.exe";
/// the installer's SHA-256 comes from GitHub's asset digest, or a "&lt;installer&gt;.sha256" asset.
/// </summary>
public static class Updater
{
    public const string Repository = "ThunSaen04/MediaForge";

    // Lets a test point the app at a local copy of the API response.
    private const string FeedOverrideVariable = "MEDIAFORGE_UPDATE_FEED";

    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    private static string FeedUrl =>
        Environment.GetEnvironmentVariable(FeedOverrideVariable) is { Length: > 0 } url
            ? url
            : $"https://api.github.com/repos/{Repository}/releases/latest";

    public static Version CurrentVersion { get; } = Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Returns the latest release when it is newer than this build, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Tools.Http.SendAsync(request, ct).WaitAsync(TimeSpan.FromSeconds(20), ct);
        // No release published yet.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

        var tag = root.Str("tag_name") ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = Normalize(latest);
        if (latest <= CurrentVersion) return null;

        var assets = root.TryGetProperty("assets", out var a) ? a.EnumerateArray().ToList() : [];
        var installer = assets.FirstOrDefault(x =>
            x.Str("name") is { } n && n.StartsWith("MediaForge-Setup", StringComparison.OrdinalIgnoreCase) &&
            n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (installer.ValueKind != JsonValueKind.Object) return null;

        var fileName = installer.Str("name")!;
        var sha256 = installer.Str("digest") is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..]
            : await ReadChecksumAssetAsync(assets, fileName, ct);
        if (string.IsNullOrWhiteSpace(sha256)) return null;   // never install something we cannot verify

        return new UpdateInfo(
            latest,
            (root.Str("body") ?? "").Trim(),
            root.Str("html_url") ?? ReleasesPage,
            installer.Str("browser_download_url")!,
            fileName,
            installer.Lng("size"),
            sha256.Trim().ToLowerInvariant());
    }

    /// <summary>Downloads the installer and checks its SHA-256. Returns the local path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(AppPaths.TempDir, "update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(update.FileName));
        await Tools.DownloadFileAsync(update.DownloadUrl, path, progress, ct);

        string actual;
        await using (var stream = File.OpenRead(path))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        if (actual != update.Sha256)
        {
            Tools.TryDelete(path);
            throw new UserException(Localization.Loc.T("Upd_HashMismatch"));
        }
        return path;
    }

    /// <summary>
    /// Starts the installer in update mode: it shows only a progress window, closes this app,
    /// installs over the current version and starts the new one (see /UPDATE in MediaForge.iss).
    /// </summary>
    public static void LaunchInstaller(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1")
        {
            UseShellExecute = true,
        });

    private static async Task<string?> ReadChecksumAssetAsync(List<JsonElement> assets, string fileName, CancellationToken ct)
    {
        var asset = assets.FirstOrDefault(x => string.Equals(x.Str("name"), fileName + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind != JsonValueKind.Object || asset.Str("browser_download_url") is not { } url) return null;
        var text = await Tools.Http.GetStringAsync(url, ct);
        // "<hex>  <file name>" or just "<hex>"
        return text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
