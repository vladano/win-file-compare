namespace FileCompare.UiTests.Infrastructure;

/// <summary>
/// An installer test (docs/TESTING.md, section 1). These install File Compare for the current user, like a user
/// would, so they need a second switch on top of the UI tests: FILECOMPARE_INSTALLER_TESTS=1
/// (scripts\run-ui-tests.ps1 -Installer). They are skipped when File Compare is already installed, so a real
/// installation is never replaced or removed.
/// </summary>
public sealed class InstallerFactAttribute : FactAttribute
{
    public const string EnableVariable = "FILECOMPARE_INSTALLER_TESTS";

    public InstallerFactAttribute()
    {
        if (!UiFactAttribute.IsEnabled || !IsEnabled)
            Skip = $"Installer test. Runs only with scripts\\run-ui-tests.ps1 -Installer ({EnableVariable}=1), because it installs File Compare.";
        else if (Installer.IsInstalled)
            Skip = "File Compare is installed on this PC. The installer tests would replace and then remove it, so they are skipped. " +
                   "Uninstall it (Settings > Apps) to run them.";
    }

    public static bool IsEnabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";
}
