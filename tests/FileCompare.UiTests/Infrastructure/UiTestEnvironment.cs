using FileCompare.Core.Settings;
using FlaUI.UIA3;
using Microsoft.Win32;

namespace FileCompare.UiTests.Infrastructure;

/// <summary>
/// What every UI test shares: the app under test, sample files, and a sandbox. The app runs with its own data folder
/// and writes its Explorer menu entries to a throwaway registry key that Explorer never reads, so the user's settings,
/// recent list, Explorer menu and any File Compare they have open are not touched. Everything is removed afterwards.
/// </summary>
public sealed class UiTestEnvironment : IDisposable
{
    /// <summary>Registry key (below HKEY_CURRENT_USER) that holds every test key. Removed at the start and at the end.</summary>
    public const string RegistryRoot = @"Software\FileCompare.UiTests";

    public UiTestEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), "FileCompare.UiTests", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        DataDir = Path.Combine(Root, "data");
        ShellClassesKey = RegistryRoot + @"\Classes";
        ExePath = FindExe();

        if (!UiFactAttribute.IsEnabled) return; // tests are skipped: do not touch anything

        Native.UseScreenPixels();
        Registry.CurrentUser.DeleteSubKeyTree(RegistryRoot, throwOnMissingSubKey: false);
        Directory.CreateDirectory(DataDir);
        Files = TestFiles.Create(Path.Combine(Root, "files"));

        // Child processes inherit these, so every app the tests start uses the sandbox.
        Environment.SetEnvironmentVariable(AppDataPaths.DataDirVariable, DataDir);
        Environment.SetEnvironmentVariable(AppDataPaths.ShellClassesVariable, ShellClassesKey);

        Automation = new UIA3Automation();
        App = new AppDriver(this);
        App.KillAll();
    }

    /// <summary>Temporary folder of this run: the data folder and the sample files.</summary>
    public string Root { get; }

    public string DataDir { get; }

    public string ShellClassesKey { get; }

    public string ExePath { get; }

    public TestFiles Files { get; } = null!;

    public UIA3Automation Automation { get; } = null!;

    public AppDriver App { get; } = null!;

    public string LeftSideFile => Path.Combine(DataDir, "left-side.txt");

    /// <summary>The exe to test: FILECOMPARE_EXE, or the Debug build next to this repository.</summary>
    private static string FindExe()
    {
        if (Environment.GetEnvironmentVariable("FILECOMPARE_EXE") is { Length: > 0 } custom) return Path.GetFullPath(custom);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FileCompare.sln")))
                return Path.Combine(dir.FullName, @"src\FileCompare.App\bin\Debug\net9.0-windows\FileCompare.exe");
        }
        throw new FileNotFoundException("FileCompare.sln not found above " + AppContext.BaseDirectory);
    }

    public void Dispose()
    {
        if (!UiFactAttribute.IsEnabled) return;

        App.KillAll();
        try { ExplorerWindow.CloseTestWindows(Root); }
        catch (TimeoutException) { /* best effort; the tests report their own failures */ }
        Automation.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(RegistryRoot, throwOnMissingSubKey: false);
        Environment.SetEnvironmentVariable(AppDataPaths.DataDirVariable, null);
        Environment.SetEnvironmentVariable(AppDataPaths.ShellClassesVariable, null);
        try
        {
            Directory.Delete(Root, recursive: true);
            string parent = Path.GetDirectoryName(Root)!;
            if (!Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
        catch (IOException) { /* a file may still be in use for a moment; it is in %TEMP% */ }
    }
}
