using System.Reflection;
using System.Runtime.InteropServices;

namespace FileCompare.App;

/// <summary>
/// What the About window shows: product, version, copyright and the project links.
/// Product, version and copyright are read from the assembly attributes set in FileCompare.App.csproj.
/// </summary>
public sealed record AppInfo(string Product, string Version, string? Commit, string Copyright)
{
    public const string RepositoryUrl = "https://github.com/vladano/win-file-compare";
    public const string ReadmeUrl = RepositoryUrl + "#readme";
    public const string ChangelogUrl = RepositoryUrl + "/blob/main/CHANGELOG.md";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    public const string IssuesUrl = RepositoryUrl + "/issues/new";
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    public const string Description = "A free, open source file and folder compare tool for Windows, made for programmers.";
    public const string Tagline = "Works offline. No account. No subscription.";
    public const string LicenseName = "MIT License";
    public const string BuiltWith = "Built with C#, .NET and WPF, AvalonEdit and DiffPlex. Created using the Kiro IDE.";

    /// <summary>"1.0.0" or "1.0.0 (commit bd76b36)".</summary>
    public string VersionText => Commit is null ? Version : $"{Version} (commit {Commit})";

    public static AppInfo Current { get; } = FromAssembly(typeof(App).Assembly);

    public static AppInfo FromAssembly(Assembly assembly)
    {
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "File Compare";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? assembly.GetName().Version?.ToString(3)
                            ?? "unknown";
        var (version, commit) = SplitVersion(informational);
        return new AppInfo(product, version, commit, copyright);
    }

    /// <summary>
    /// The SDK appends the commit to the informational version when it knows it ("1.0.0+bd76b36624c2...").
    /// Keep the version, and a short commit id when there is one.
    /// </summary>
    public static (string Version, string? Commit) SplitVersion(string informational)
    {
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0) return (informational, null);
        var commit = informational[(plus + 1)..];
        return (informational[..plus], commit.Length == 0 ? null : commit[..Math.Min(7, commit.Length)]);
    }

    /// <summary>Text for "Copy details": what a bug report needs to know about this installation.</summary>
    public string Details() =>
        $"{Product} {VersionText}{Environment.NewLine}" +
        $"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}){Environment.NewLine}" +
        $".NET: {RuntimeInformation.FrameworkDescription}{Environment.NewLine}" +
        $"Source: {RepositoryUrl}";
}
