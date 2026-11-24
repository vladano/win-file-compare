using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FileCompare.UiTests.Infrastructure;

/// <summary>
/// Runs the real Setup (dist\FileCompare-Setup-*-x64.exe) and Uninstall, and knows where they put things.
/// Setup and the app it starts inherit the test environment variables, so the Explorer menu entries go to the
/// throwaway registry key and the settings to the test data folder, as in the other UI tests.
/// </summary>
public sealed class Installer
{
    /// <summary>Must match AppId in installer\FileCompare.iss. Inno Setup adds "_is1" to the uninstall key.</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8F4C6E2A-3B1D-4F7A-9C55-2E6B1A9D0C47}_is1";

    private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(3);
    private readonly string _logDir;
    private int _runs;

    public Installer(string logDir)
    {
        _logDir = logDir;
        SetupPath = FindSetup();
        var match = Regex.Match(Path.GetFileName(SetupPath), @"FileCompare-Setup-(?<v>[\d.]+)-x64\.exe", RegexOptions.IgnoreCase);
        Version = match.Success ? match.Groups["v"].Value : "";
    }

    public string SetupPath { get; }

    /// <summary>The version in the Setup file name, for example "1.0.0".</summary>
    public string Version { get; }

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key is not null;
        }
    }

    /// <summary>%LocalAppData%\Programs\File Compare: where a per-user install goes ({autopf}).</summary>
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "File Compare");

    public static string InstalledExe => Path.Combine(InstallDir, "FileCompare.exe");

    public static string Uninstaller => Path.Combine(InstallDir, "unins000.exe");

    /// <summary>The Start menu entry ({autoprograms} for a per-user install).</summary>
    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "File Compare.lnk");

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "File Compare.lnk");

    public static string? UninstallValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue(name)?.ToString();
    }

    /// <summary>
    /// Installs without any window (/VERYSILENT) and waits for the end. <paramref name="tasks"/> goes to /MERGETASKS,
    /// for example "!explorermenu" or "desktopicon".
    /// </summary>
    public void Install(string? tasks = null)
    {
        using var setup = StartSetup(silent: "/VERYSILENT /SUPPRESSMSGBOXES", tasks);
        WaitForExit(setup, "Setup");
        Assert.True(setup.ExitCode == 0, $"Setup ended with exit code {setup.ExitCode}. Log: {LastLog}");
        Assert.True(File.Exists(InstalledExe), $"{InstalledExe} is missing after Setup.");
    }

    /// <summary>Starts Setup with its progress window and message boxes (/SILENT), and does not wait.</summary>
    public Process StartVisibleSetup() => StartSetup(silent: "/SILENT", tasks: null);

    private Process StartSetup(string silent, string? tasks)
    {
        string log = Path.Combine(_logDir, $"setup-{++_runs}.log");
        LastLog = log;
        string arguments = $"{silent} /NORESTART /LOG=\"{log}\"" + (tasks is null ? "" : $" /MERGETASKS=\"{tasks}\"");
        return Process.Start(new ProcessStartInfo(SetupPath, arguments) { UseShellExecute = false })
               ?? throw new InvalidOperationException("Setup did not start.");
    }

    public string LastLog { get; private set; } = "";

    public static void WaitForExit(Process process, string what)
    {
        if (!process.WaitForExit((int)SetupTimeout.TotalMilliseconds))
            throw new TimeoutException($"{what} did not finish within {SetupTimeout.TotalMinutes} minutes.");
    }

    /// <summary>
    /// Uninstalls without any window, as Settings > Apps > Uninstall does (it runs the same unins000.exe), and waits
    /// until it is done. The uninstaller copies itself to %TEMP% and runs from there, so the end is detected by the
    /// uninstall key disappearing.
    /// </summary>
    public static void Uninstall()
    {
        if (!File.Exists(Uninstaller)) return;
        using (var process = Process.Start(new ProcessStartInfo(Uninstaller, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = false }))
            if (process is not null) WaitForExit(process, "Uninstall");

        // The folder is removed last, a moment after the uninstall key.
        Wait.Until(() => !IsInstalled && !Directory.Exists(InstallDir), "the uninstall to finish", TimeSpan.FromMinutes(1));
    }

    /// <summary>Running copies of the installed exe.</summary>
    public static List<Process> Running() =>
        Process.GetProcessesByName("FileCompare").Where(p =>
        {
            try { return string.Equals(p.MainModule?.FileName, InstalledExe, StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
        }).ToList();

    public static void KillInstalled()
    {
        foreach (var process in Running())
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException) { /* already gone */ }
        }
    }

    /// <summary>FILECOMPARE_SETUP, or the newest dist\FileCompare-Setup-*-x64.exe of this repository.</summary>
    private static string FindSetup()
    {
        if (Environment.GetEnvironmentVariable("FILECOMPARE_SETUP") is { Length: > 0 } custom) return Path.GetFullPath(custom);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "FileCompare.sln"))) continue;
            var dist = new DirectoryInfo(Path.Combine(dir.FullName, "dist"));
            var setup = dist.Exists ? dist.GetFiles("FileCompare-Setup-*-x64.exe").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
            return setup?.FullName ?? throw new FileNotFoundException(
                "No installer in dist\\. Build it with scripts\\build-installer.ps1 (or run scripts\\run-ui-tests.ps1 -Installer).");
        }
        throw new FileNotFoundException("FileCompare.sln not found above " + AppContext.BaseDirectory);
    }
}
