using System.Diagnostics;
using FileCompare.Core.Shell;
using FileCompare.UiTests.Infrastructure;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Win32;

namespace FileCompare.UiTests;

/// <summary>
/// docs/TESTING.md, section 1 (IN-01..IN-11). Each test installs File Compare with the real Setup and uninstalls it
/// at the end, so the tests do not depend on their order. Setup inherits the test environment variables: the Explorer
/// menu entries go to the throwaway key and the settings to the test data folder. A guard fails the test (and cleans
/// up) if the real Explorer menu was changed anyway.
/// </summary>
[Collection(UiCollection.Name)]
public sealed class InstallerTests : IDisposable
{
    private readonly UiTestEnvironment _env;
    private readonly Installer? _installer;
    private readonly bool _realMenuBefore;
    private readonly bool _desktopShortcutBefore;

    public InstallerTests(UiTestEnvironment env)
    {
        _env = env;
        if (!UiFactAttribute.IsEnabled || !InstallerFactAttribute.IsEnabled || Installer.IsInstalled) return;

        App.KillAll();
        _installer = new Installer(Path.Combine(env.Root, "logs"));
        Directory.CreateDirectory(Path.Combine(env.Root, "logs"));
        _realMenuBefore = RealMenuExists();
        _desktopShortcutBefore = File.Exists(Installer.DesktopShortcut);
    }

    private AppDriver App => _env.App;
    private TestFiles Files => _env.Files;
    private Installer Setup => _installer!;

    // ----- Tests -----

    [InstallerFact]
    public void IN01_IN02_IN03_IN04_DefaultInstall()
    {
        Setup.Install();

        // IN-01: per user, in %LocalAppData%\Programs\File Compare (Setup ran without elevation, from this process).
        Assert.True(File.Exists(Installer.InstalledExe));
        Assert.True(File.Exists(Path.Combine(Installer.InstallDir, "LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(Installer.InstallDir, "THIRD-PARTY-NOTICES.md")));

        // IN-02: the Start menu entry starts the installed app, which shows Home.
        Assert.True(File.Exists(Installer.StartMenuShortcut), $"No Start menu entry at {Installer.StartMenuShortcut}");
        Assert.Equal(Installer.InstalledExe, ShortcutTarget(Installer.StartMenuShortcut), StringComparer.OrdinalIgnoreCase);
        StartInstalled();
        WaitForInstalledWindow(t => t == "File Compare", "the Home tab of the installed app");

        // IN-03: Settings > Apps shows these values from the uninstall key.
        Assert.Equal($"File Compare", Installer.UninstallValue("DisplayName"));
        Assert.Equal(Setup.Version, Installer.UninstallValue("DisplayVersion"));
        Assert.Equal("vladano", Installer.UninstallValue("Publisher"));

        // IN-04: the Explorer menu task is on by default, and the entries start the installed exe with its icon.
        Assert.Equal("Select left side to compare", App.Label(MenuTarget.Files, ShellRegistration.SelectLeftVerb));
        Assert.Equal("Compare selected items", App.Label(MenuTarget.Files, ShellRegistration.CompareVerb));
        Assert.Equal($"\"{Installer.InstalledExe}\",0", App.Value(MenuTarget.Files, ShellRegistration.SelectLeftVerb, "Icon"));
    }

    [InstallerFact]
    public void IN05_ExplorerIntegrationDialog_ShowsTheInstalledCopy()
    {
        Setup.Install();
        StartInstalled();
        var window = WaitForInstalledWindow(t => t == "File Compare", "the installed app");

        OpenMenu(window, "Tools", "Explorer integration...");
        var dialog = Wait.For(() => window.FindFirstChild(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Explorer integration"))),
            "the Explorer integration dialog");

        Wait.Until(() => dialog.FindFirstDescendant(cf => cf.ByAutomationId("StatusText"))?.Name == "Installed for this copy of File Compare.",
            "the status \"Installed for this copy\"");
        Assert.Equal(Installer.InstalledExe, dialog.FindFirstDescendant(cf => cf.ByAutomationId("ExeText"))?.Name, StringComparer.OrdinalIgnoreCase);
        dialog.AsWindow().Close();
    }

    [InstallerFact]
    public void IN06_SetupWhileTheAppRuns_AsksToCloseIt()
    {
        Setup.Install();
        StartInstalled();
        WaitForInstalledWindow(t => t == "File Compare", "the installed app");

        using var setup = Setup.StartVisibleSetup();
        var message = Wait.For(() => FindSetupMessage("is currently running"), "Setup's message that File Compare is running",
            TimeSpan.FromSeconds(60));

        // The user closes the app and clicks OK: Setup goes on and finishes.
        Installer.KillInstalled();
        message.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("OK")))!.AsButton().Invoke();
        Installer.WaitForExit(setup, "Setup");
        Assert.True(setup.ExitCode == 0, $"Setup ended with exit code {setup.ExitCode}. Log: {Setup.LastLog}");
    }

    [InstallerFact]
    public void IN07_IN08_MenuTask_RemovesAndAddsTheEntries()
    {
        Setup.Install();
        Assert.True(App.HasEntry(MenuTarget.Files, ShellRegistration.SelectLeftVerb), "no entries after the first install");

        Setup.Install(tasks: "!explorermenu");
        foreach (var target in new[] { MenuTarget.Files, MenuTarget.Folders, MenuTarget.FolderBackground })
            Assert.False(App.HasEntry(target, ShellRegistration.SelectLeftVerb), $"IN-07: the entries are still there for {target}");

        Setup.Install(tasks: "explorermenu");
        foreach (var target in new[] { MenuTarget.Files, MenuTarget.Folders, MenuTarget.FolderBackground })
            Assert.True(App.HasEntry(target, ShellRegistration.SelectLeftVerb), $"IN-08: the entries are missing for {target}");
    }

    [InstallerFact]
    public void IN09_DesktopShortcut_StartsTheApp()
    {
        Assert.False(_desktopShortcutBefore, $"{Installer.DesktopShortcut} already exists; the test would remove it.");

        Setup.Install(tasks: "desktopicon");

        Assert.True(File.Exists(Installer.DesktopShortcut), "no desktop shortcut");
        Assert.Equal(Installer.InstalledExe, ShortcutTarget(Installer.DesktopShortcut), StringComparer.OrdinalIgnoreCase);
        StartInstalled();
        WaitForInstalledWindow(t => t == "File Compare", "the app started from the desktop shortcut's target");
    }

    [InstallerFact]
    public void IN10_IN11_Uninstall_RemovesTheProgram_AndKeepsTheSettings()
    {
        Assert.False(_desktopShortcutBefore, $"{Installer.DesktopShortcut} already exists; the test would remove it.");
        Setup.Install(tasks: "desktopicon");

        // Something to keep: a comparison in the recent list (written to the settings file).
        StartInstalled(Files.TextLeft, Files.TextRight);
        WaitForInstalledWindow(t => t == "fcui-left.txt <--> fcui-right.txt - File Compare", "the comparison in the installed app");
        string settings = Path.Combine(_env.DataDir, "settings.json");
        Wait.Until(() => File.Exists(settings) && File.ReadAllText(settings).Contains("fcui-left.txt", StringComparison.Ordinal),
            "the comparison to be saved in the recent list");
        Installer.KillInstalled();

        // IN-10
        Installer.Uninstall();
        Assert.False(Directory.Exists(Installer.InstallDir), $"{Installer.InstallDir} is still there");
        Assert.False(File.Exists(Installer.StartMenuShortcut), "the Start menu entry is still there");
        Assert.False(File.Exists(Installer.DesktopShortcut), "the desktop shortcut is still there");
        foreach (var target in new[] { MenuTarget.Files, MenuTarget.Folders, MenuTarget.FolderBackground })
            foreach (var verb in new[] { ShellRegistration.SelectLeftVerb, ShellRegistration.CompareToVerb, ShellRegistration.CompareVerb })
                Assert.False(App.HasEntry(target, verb), $"{verb} is still there for {target}");
        Assert.True(File.Exists(settings), "the settings were removed");

        // IN-11: after a new install the recent list is still there.
        Setup.Install();
        StartInstalled();
        var window = WaitForInstalledWindow(t => t == "File Compare", "the Home tab");
        Wait.For(() => window.FindFirstDescendant(cf => cf.ByName("fcui-left.txt \u2194 fcui-right.txt")),
            "the comparison in the Recent list on Home");
    }

    // ----- Helpers -----

    private void StartInstalled(params string[] args)
    {
        var info = new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        Process.Start(info)?.Dispose();
    }

    private Window WaitForInstalledWindow(Func<string, bool> title, string what)
    {
        var desktop = _env.Automation.GetDesktop();
        return Wait.For(() => Installer.Running()
            .SelectMany(p => desktop.FindAllChildren(cf => cf.ByProcessId(p.Id)))
            .Where(e => e.ControlType == ControlType.Window)
            .Select(e => e.AsWindow())
            .FirstOrDefault(w => title(w.Title)), what, TimeSpan.FromSeconds(30));
    }

    /// <summary>Opens a menu of the main window and clicks one of its items, through UI Automation.</summary>
    private void OpenMenu(Window window, string menu, string item)
    {
        var top = Wait.For(() => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(menu))), $"the {menu} menu");
        top.AsMenuItem().Expand();

        // The items are in a popup, which UI Automation may show under the window or under the desktop.
        var desktop = _env.Automation.GetDesktop();
        int pid = window.Properties.ProcessId.Value;
        var entry = Wait.For(() =>
            window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(item)))
            ?? desktop.FindAllChildren(cf => cf.ByProcessId(pid))
                .Select(p => p.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(item))))
                .FirstOrDefault(e => e is not null), $"the menu item {item}");
        entry.AsMenuItem().Invoke();
    }

    /// <summary>A message box of Setup (FileCompare-Setup-*.exe or its .tmp helper) that contains the text.</summary>
    private AutomationElement? FindSetupMessage(string text)
    {
        var desktop = _env.Automation.GetDesktop();
        foreach (var process in Process.GetProcesses().Where(p => p.ProcessName.StartsWith("FileCompare-Setup", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var window in desktop.FindAllChildren(cf => cf.ByProcessId(process.Id)))
            {
                var candidates = new List<AutomationElement> { window };
                candidates.AddRange(window.FindAllChildren(cf => cf.ByControlType(ControlType.Window)));
                foreach (var candidate in candidates)
                {
                    if (candidate.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Any(t => t.Name.Contains(text, StringComparison.Ordinal)))
                        return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>The program a .lnk file starts.</summary>
    private static string ShortcutTarget(string shortcut)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic link = shell.CreateShortcut(shortcut);
            return (string)link.TargetPath;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>True if the real Explorer menu (HKCU\Software\Classes) has File Compare entries.</summary>
    private static bool RealMenuExists() =>
        new[] { "*", "Directory", @"Directory\Background" }.Any(type =>
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{type}\shell");
            return key?.GetSubKeyNames().Any(n => n.StartsWith("FileCompare.", StringComparison.OrdinalIgnoreCase)) == true;
        });

    private static void RemoveRealMenu()
    {
        foreach (var type in new[] { "*", "Directory", @"Directory\Background" })
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{type}\shell", writable: true);
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames().Where(n => n.StartsWith("FileCompare.", StringComparison.OrdinalIgnoreCase)))
                key.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
    }

    public void Dispose()
    {
        if (_installer is null) return;

        Installer.KillInstalled();
        foreach (var p in Process.GetProcesses().Where(p => p.ProcessName.StartsWith("FileCompare-Setup", StringComparison.OrdinalIgnoreCase)))
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* gone */ }
        }
        Installer.Uninstall();
        if (!_desktopShortcutBefore) File.Delete(Installer.DesktopShortcut);

        // The real menu must be as it was. If Setup wrote to it after all, undo that and fail loudly.
        if (!_realMenuBefore && RealMenuExists())
        {
            RemoveRealMenu();
            throw new InvalidOperationException("Setup wrote to the real Explorer menu (HKCU\\Software\\Classes). The entries were removed again.");
        }
        App.KillAll();
    }
}
