using FileCompare.Core.Settings;

namespace FileCompare.App;

/// <summary>The user's saved settings, shared by the whole app. Call <see cref="Save"/> after changing <see cref="Current"/>.</summary>
public static class UserSettings
{
    private static readonly SettingsStore Store = new();

    public static AppSettings Current { get; private set; } = new();

    public static void Load() => Current = Store.Load();

    public static void Save() => Store.Save(Current);
}
