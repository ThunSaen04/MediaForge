using System.Reflection;

namespace MediaForge.Core;

/// <summary>Product and developer details shown in the UI. Company/Copyright come from MediaForge.csproj.</summary>
public static class AppInfo
{
    public const string Website = "https://siammods.com";

    // Hyperlink.NavigateUri needs a Uri; x:Static skips the string-to-Uri converter.
    public static Uri WebsiteUri { get; } = new(Website);

    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    public static string Developer { get; } =
        Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Siammods.com";

    public static string Copyright { get; } =
        Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? $"© {Developer}";

    public static string Version { get; } = Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
}
