namespace FileCompare.App;

/// <summary>What a launch asks the app to do.</summary>
public enum LaunchAction
{
    /// <summary>Open a compare window for the paths (or an empty one).</summary>
    Open,
    /// <summary>Explorer "Select left side": remember the path for a later compare.</summary>
    SelectLeft,
    /// <summary>Explorer "Compare to ...": compare the remembered left side with the path.</summary>
    CompareToLeft,
    /// <summary>Explorer "Compare selected items": one launch per selected item, collected by the running instance.</summary>
    CompareSelected,
    /// <summary>Add the Explorer context menu entries, then exit.</summary>
    RegisterShell,
    /// <summary>Remove the Explorer context menu entries, then exit.</summary>
    UnregisterShell
}

/// <summary>
/// FileCompare.exe                          -> empty compare window
/// FileCompare.exe "left" "right"           -> compare two files or two folders
/// FileCompare.exe "path"                   -> one path (left side), right side chosen later
/// FileCompare.exe --select-left "path"     -> Explorer menu commands (see <see cref="LaunchAction"/>)
/// FileCompare.exe --compare-to "path"
/// FileCompare.exe --compare-selected "path"
/// FileCompare.exe --register-shell | --unregister-shell
/// Unknown switches are ignored.
/// </summary>
public sealed record CommandLineArgs(string? Left, string? Right, LaunchAction Action = LaunchAction.Open)
{
    public static CommandLineArgs Parse(string[] args)
    {
        var action = LaunchAction.Open;
        var paths = new List<string>();

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                action = arg.ToLowerInvariant() switch
                {
                    "--select-left" => LaunchAction.SelectLeft,
                    "--compare-to" => LaunchAction.CompareToLeft,
                    "--compare-selected" => LaunchAction.CompareSelected,
                    "--register-shell" => LaunchAction.RegisterShell,
                    "--unregister-shell" => LaunchAction.UnregisterShell,
                    _ => action
                };
                continue;
            }
            paths.Add(arg);
        }

        return new CommandLineArgs(
            paths.Count > 0 ? paths[0] : null,
            paths.Count > 1 ? paths[1] : null,
            action);
    }
}
