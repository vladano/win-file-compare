using System.Text.Json;

namespace FileCompare.Core.Settings;

/// <summary>Reads and writes <see cref="AppSettings"/> as JSON. Never throws: a broken file just gives defaults.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string? path = null) => Path = path ?? DefaultPath;

    public string Path { get; }

    /// <summary>%AppData%\FileCompare\settings.json, unless <see cref="AppDataPaths.DataDirVariable"/> moves it.</summary>
    public static string DefaultPath => AppDataPaths.SettingsFile;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions) ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <returns>True when the file was written.</returns>
    public bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
