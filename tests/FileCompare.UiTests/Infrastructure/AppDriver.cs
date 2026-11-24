using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Microsoft.Win32;

namespace FileCompare.UiTests.Infrastructure;

/// <summary>Where the Explorer menu entries live.</summary>
public enum MenuTarget
{
    Files,
    Folders,
    FolderBackground
}

/// <summary>Starts, finds and stops the app under test, and runs the commands its Explorer menu entries hold.</summary>
public sealed class AppDriver
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly UiTestEnvironment _env;

    public AppDriver(UiTestEnvironment env) => _env = env;

    // ----- Processes -----

    /// <summary>Starts the app with arguments, like a double-click or a shortcut would.</summary>
    public Process Start(params string[] args)
    {
        var info = new ProcessStartInfo(_env.ExePath) { UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new InvalidOperationException("The app did not start.");
    }

    /// <summary>Running copies of the app under test (other copies of File Compare, such as an installed one, are left alone).</summary>
    public List<Process> Running() => Process.GetProcessesByName("FileCompare").Where(IsUnderTest).ToList();

    public void KillAll()
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

    private bool IsUnderTest(Process process)
    {
        try { return string.Equals(process.MainModule?.FileName, _env.ExePath, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    // ----- Windows -----

    /// <summary>Top-level windows of the app under test (main windows and message boxes).</summary>
    public List<Window> Windows()
    {
        var desktop = _env.Automation.GetDesktop();
        var windows = new List<Window>();
        foreach (var process in Running())
        {
            foreach (var element in desktop.FindAllChildren(cf => cf.ByProcessId(process.Id)))
            {
                if (element.ControlType == ControlType.Window) windows.Add(element.AsWindow());
            }
        }
        return windows;
    }

    /// <summary>Waits until a window of the app has a title that matches.</summary>
    public Window WaitForWindow(Func<string, bool> title, string what) =>
        Wait.For(() => Windows().FirstOrDefault(w => title(w.Title)), what)!;

    /// <summary>The texts on the tabs of a main window, in order.</summary>
    public static List<string> Tabs(Window window)
    {
        var strip = window.FindFirstDescendant(cf => cf.ByName("Tabs"));
        if (strip is null) return new List<string>();
        return strip.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))
            .Select(item => item.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text))?.Name ?? "")
            .ToList();
    }

    /// <summary>Selects a tab by its text (through UI Automation, without the mouse).</summary>
    public static void SelectTab(Window window, Func<string, bool> text)
    {
        var strip = window.FindFirstDescendant(cf => cf.ByName("Tabs"))!;
        var tab = strip.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))
            .First(item => text(item.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text))?.Name ?? ""));
        tab.AsListBoxItem().Select();
    }

    /// <summary>The text of a named text element of the page shown in a window (for example DiffCountText).</summary>
    public static string? TextOf(Window window, string automationId) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.Name;

    /// <summary>A message box of the app whose text contains <paramref name="text"/>, or null.</summary>
    public AutomationElement? FindMessageBox(string text)
    {
        foreach (var window in Windows())
        {
            var candidates = new List<AutomationElement> { window };
            candidates.AddRange(window.FindAllChildren(cf => cf.ByControlType(ControlType.Window)));
            foreach (var candidate in candidates)
            {
                bool hasText = candidate.FindAllChildren(cf => cf.ByControlType(ControlType.Text))
                    .Any(t => t.Name.Contains(text, StringComparison.Ordinal));
                if (hasText) return candidate;
            }
        }
        return null;
    }

    // ----- Explorer menu (the throwaway registry key) -----

    /// <summary>Runs "FileCompare.exe --register-shell" and waits until it is done.</summary>
    public void RegisterMenu() => RunToEnd("--register-shell");

    public void UnregisterMenu() => RunToEnd("--unregister-shell");

    private void RunToEnd(string argument)
    {
        using var process = Start(argument);
        if (!process.WaitForExit((int)Timeout.TotalMilliseconds)) throw new TimeoutException($"{argument} did not finish.");
    }

    public string VerbKey(MenuTarget target, string verb)
    {
        string type = target switch
        {
            MenuTarget.Files => "*",
            MenuTarget.Folders => "Directory",
            _ => @"Directory\Background"
        };
        return $@"{_env.ShellClassesKey}\{type}\shell\{verb}";
    }

    public bool HasEntry(MenuTarget target, string verb)
    {
        using var key = Registry.CurrentUser.OpenSubKey(VerbKey(target, verb));
        return key is not null;
    }

    /// <summary>The text Explorer shows for an entry, as stored (a literal "&amp;" is stored as "&amp;&amp;").</summary>
    public string? Label(MenuTarget target, string verb)
    {
        using var key = Registry.CurrentUser.OpenSubKey(VerbKey(target, verb));
        return key?.GetValue("MUIVerb") as string;
    }

    public string? Value(MenuTarget target, string verb, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(VerbKey(target, verb));
        return key?.GetValue(name) as string;
    }

    /// <summary>
    /// Does what Explorer does when the entry is clicked: takes the command line stored for it, puts the clicked item
    /// in place of %1 (or %V for the folder background) and starts it.
    /// </summary>
    public Process RunMenuCommand(MenuTarget target, string verb, string item)
    {
        using var key = Registry.CurrentUser.OpenSubKey(VerbKey(target, verb) + @"\command")
                        ?? throw new InvalidOperationException($"No command for {verb} on {target}.");
        string command = (string)key.GetValue(null)!;
        command = command.Replace("%1", item, StringComparison.Ordinal).Replace("%V", item, StringComparison.Ordinal);

        // The command is "<quoted exe>" <arguments>.
        int end = command.IndexOf('"', 1);
        string exe = command[1..end];
        string arguments = command[(end + 1)..].Trim();
        return Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false })!;
    }
}

/// <summary>Waiting for the app, which starts and updates in its own time.</summary>
public static class Wait
{
    /// <summary>Polls until <paramref name="probe"/> returns a value, or fails the test with <paramref name="what"/>.</summary>
    public static T For<T>(Func<T?> probe, string what, TimeSpan? timeout = null) where T : class
    {
        var limit = Stopwatch.StartNew();
        Exception? last = null;
        while (limit.Elapsed < (timeout ?? AppDriver.Timeout))
        {
            try
            {
                if (probe() is { } value) return value;
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                last = ex; // UI Automation can throw while the window is still being built
            }
            Thread.Sleep(200);
        }
        throw new TimeoutException($"Timed out waiting for: {what}" + (last is null ? "" : $" (last error: {last.Message})"));
    }

    /// <summary>Polls until <paramref name="condition"/> is true, or fails the test with <paramref name="what"/>.</summary>
    public static void Until(Func<bool> condition, string what, TimeSpan? timeout = null) =>
        For(() => condition() ? "ok" : null, what, timeout);
}
