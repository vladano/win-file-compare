namespace FileCompare.Core.Shell;

/// <summary>
/// Remembers the item picked with Explorer's "Select left side" until "Compare to ..." uses it.
/// Kept in a small file because each Explorer menu click may start a new process.
/// </summary>
public sealed class LeftSideStore
{
    public LeftSideStore(string? path = null) => FilePath = path ?? DefaultPath;

    public string FilePath { get; }

    /// <summary>%AppData%\FileCompare\left-side.txt, unless <see cref="Settings.AppDataPaths.DataDirVariable"/> moves it.</summary>
    public static string DefaultPath => Settings.AppDataPaths.LeftSideFile;

    /// <summary>The remembered path, or null when nothing is selected or the item no longer exists.</summary>
    public string? Get()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            string path = File.ReadAllText(FilePath).Trim();
            return path.Length > 0 && (File.Exists(path) || Directory.Exists(path)) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <returns>False when the file could not be written.</returns>
    public bool Set(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to clean up, or it is in use; a stale entry is checked again by Get.
        }
    }
}
