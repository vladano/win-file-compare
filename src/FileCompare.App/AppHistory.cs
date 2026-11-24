using FileCompare.Core.Sessions;

namespace FileCompare.App;

/// <summary>The recent comparisons and saved sessions, kept in the user's settings file.</summary>
public static class AppHistory
{
    /// <summary>Raised after the recent list or the sessions changed (the home page redraws).</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<RecentEntry> Recent => UserSettings.Current.Recent;

    public static IReadOnlyList<SavedSession> Sessions => UserSettings.Current.Sessions;

    public static void Record(CompareTarget target)
    {
        History.AddRecent(UserSettings.Current.Recent, target.Kind, target.Left, target.Right, DateTime.Now);
        SaveAndNotify();
    }

    public static void SaveSession(string name, CompareTarget target)
    {
        History.SaveSession(UserSettings.Current.Sessions, name, target.Kind, target.Left, target.Right, DateTime.Now);
        SaveAndNotify();
    }

    public static void RemoveSession(string name)
    {
        if (History.RemoveSession(UserSettings.Current.Sessions, name)) SaveAndNotify();
    }

    public static void ClearRecent()
    {
        UserSettings.Current.Recent.Clear();
        SaveAndNotify();
    }

    private static void SaveAndNotify()
    {
        UserSettings.Save();
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
