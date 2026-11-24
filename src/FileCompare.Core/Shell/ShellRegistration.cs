using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FileCompare.Core.Shell;

/// <summary>
/// Adds and removes the classic Explorer context menu entries (Windows 10, and "Show more options" on Windows 11).
/// Everything is written below HKEY_CURRENT_USER\Software\Classes, so no administrator rights are needed
/// and only the current user is affected. Remove takes away exactly the keys Install created.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellRegistration
{
    // The number keeps the entries in this order in Explorer, which sorts static verbs by key name.
    public const string SelectLeftVerb = "FileCompare.1SelectLeft";
    public const string CompareToVerb = "FileCompare.2CompareTo";
    public const string CompareVerb = "FileCompare.3CompareSelected";

    /// <summary>Where the entries go: files, folders, and the empty area inside an open folder.</summary>
    public enum Target { Files, Folders, FolderBackground }

    private static readonly Target[] AllTargets = { Target.Files, Target.Folders, Target.FolderBackground };

    private readonly RegistryKey _root;
    private readonly string _classesPath;

    /// <param name="exePath">The program the menu entries start.</param>
    /// <param name="root">The hive to write to. Tests pass a throwaway key.</param>
    /// <param name="classesPath">The Classes key below <paramref name="root"/>.</param>
    public ShellRegistration(string exePath, RegistryKey root, string classesPath = @"Software\Classes")
    {
        ExePath = exePath;
        _root = root;
        _classesPath = classesPath;
    }

    /// <summary>
    /// The real location for the current user (HKCU\Software\Classes), unless
    /// <see cref="Settings.AppDataPaths.ShellClassesVariable"/> points the entries somewhere else (used by the UI tests).
    /// </summary>
    public static ShellRegistration ForCurrentUser(string exePath) =>
        new(exePath, Registry.CurrentUser, Settings.AppDataPaths.ShellClassesKey);

    public string ExePath { get; }

    /// <summary>The keys Install writes, for showing the user (relative to HKEY_CURRENT_USER).</summary>
    public IEnumerable<string> KeyPaths =>
        AllTargets.SelectMany(t => VerbsFor(t).Select(v => VerbPath(t, v)));

    public bool IsInstalled
    {
        get
        {
            using var key = _root.OpenSubKey(VerbPath(Target.Files, SelectLeftVerb));
            return key is not null;
        }
    }

    /// <summary>The program the installed entries start, or null when they are not installed.</summary>
    public string? InstalledExePath
    {
        get
        {
            using var key = _root.OpenSubKey(VerbPath(Target.Files, SelectLeftVerb) + @"\command");
            return key?.GetValue(null) is string command ? FirstQuoted(command) : null;
        }
    }

    /// <summary>Writes the entries. <paramref name="leftSide"/> adds the "Compare to" entry when something is already selected.</summary>
    public void Install(string? leftSide = null)
    {
        foreach (var target in AllTargets)
        {
            WriteVerb(target, SelectLeftVerb, "Select left side to compare", "--select-left", multiSelect: false);
            if (target != Target.FolderBackground)
                WriteVerb(target, CompareVerb, "Compare selected items", "--compare-selected", multiSelect: true);
        }
        SetCompareTo(leftSide);
    }

    /// <summary>Removes every key Install wrote. Safe to call when nothing is installed.</summary>
    public void Uninstall()
    {
        foreach (var target in AllTargets)
            foreach (var verb in new[] { SelectLeftVerb, CompareToVerb, CompareVerb })
                _root.DeleteSubKeyTree(VerbPath(target, verb), throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Shows "Compare to 'name'" for <paramref name="leftSide"/>, or removes the entry when it is null.
    /// Does nothing when the entries are not installed, so it never adds a menu the user did not ask for.
    /// </summary>
    public void SetCompareTo(string? leftSide)
    {
        if (!IsInstalled) return;

        foreach (var target in AllTargets)
        {
            if (leftSide is null)
                _root.DeleteSubKeyTree(VerbPath(target, CompareToVerb), throwOnMissingSubKey: false);
            else
                WriteVerb(target, CompareToVerb, $"Compare to '{DisplayName(leftSide)}'", "--compare-to", multiSelect: false);
        }
    }

    // ----- Helpers -----

    private static IEnumerable<string> VerbsFor(Target target) =>
        target == Target.FolderBackground
            ? new[] { SelectLeftVerb, CompareToVerb }
            : new[] { SelectLeftVerb, CompareToVerb, CompareVerb };

    private string VerbPath(Target target, string verb)
    {
        string type = target switch
        {
            Target.Files => "*",
            Target.Folders => "Directory",
            _ => @"Directory\Background"
        };
        return $@"{_classesPath}\{type}\shell\{verb}";
    }

    private void WriteVerb(Target target, string verb, string text, string argument, bool multiSelect)
    {
        // %1 is the clicked item; on the folder background there is no item, %V is the open folder.
        string item = target == Target.FolderBackground ? "%V" : "%1";

        using var key = _root.CreateSubKey(VerbPath(target, verb));
        key.SetValue("MUIVerb", text);
        key.SetValue("Icon", $"\"{ExePath}\",0");
        // Single hides the entry when several items are selected; Player shows it and starts it once per item.
        key.SetValue("MultiSelectModel", multiSelect ? "Player" : "Single");

        using var command = key.CreateSubKey("command");
        command.SetValue(null, $"\"{ExePath}\" {argument} \"{item}\"");
    }

    /// <summary>The name to show in the menu. "&amp;" would become a keyboard shortcut, so it is doubled.</summary>
    public static string DisplayName(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        if (name.Length == 0) name = path; // a drive root such as C:\
        return name.Replace("&", "&&", StringComparison.Ordinal);
    }

    private static string? FirstQuoted(string command)
    {
        if (!command.StartsWith('"')) return command.Split(' ', 2)[0];
        int end = command.IndexOf('"', 1);
        return end > 0 ? command[1..end] : null;
    }
}
