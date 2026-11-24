using System.Security.Cryptography;
using System.Text;

namespace FileCompare.Core.Settings;

/// <summary>
/// Where File Compare keeps its data. Two environment variables move it, so automated UI tests (and a portable
/// setup) never touch the user's real settings or Explorer menu. Without them, the normal locations are used.
/// </summary>
public static class AppDataPaths
{
    /// <summary>Folder for settings.json and the remembered left side. Default: %AppData%\FileCompare.</summary>
    public const string DataDirVariable = "FILECOMPARE_DATA_DIR";

    /// <summary>Registry key below HKEY_CURRENT_USER for the Explorer menu entries. Default: Software\Classes.</summary>
    public const string ShellClassesVariable = "FILECOMPARE_SHELL_CLASSES_KEY";

    public const string DefaultShellClassesKey = @"Software\Classes";

    public static string Root =>
        Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileCompare");

    public static bool IsDataDirOverridden => Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 };

    public static string ShellClassesKey =>
        Environment.GetEnvironmentVariable(ShellClassesVariable) is { Length: > 0 } custom ? custom : DefaultShellClassesKey;

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string LeftSideFile => Path.Combine(Root, "left-side.txt");

    /// <summary>
    /// Name for the single-instance channel. A moved data folder gets its own name, so a copy using it never hands its
    /// work to (or takes work from) a copy that uses the normal folder.
    /// </summary>
    public static string InstanceName
    {
        get
        {
            if (!IsDataDirOverridden) return "FileCompare";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(Root).ToUpperInvariant()));
            return "FileCompare-" + Convert.ToHexString(hash, 0, 4);
        }
    }
}
