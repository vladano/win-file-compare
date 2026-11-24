namespace FileCompare.UiTests.Infrastructure;

/// <summary>
/// A test that drives the real app with the mouse and keyboard. It only runs when FILECOMPARE_UI_TESTS=1
/// (set by scripts\run-ui-tests.ps1); otherwise it is reported as skipped, so normal test runs and CI are not affected.
/// </summary>
public sealed class UiFactAttribute : FactAttribute
{
    public const string EnableVariable = "FILECOMPARE_UI_TESTS";

    public UiFactAttribute()
    {
        if (!IsEnabled)
            Skip = $"UI test. Runs only with scripts\\run-ui-tests.ps1 ({EnableVariable}=1), because it moves the mouse.";
    }

    public static bool IsEnabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";
}

/// <summary>All UI tests share one environment and run one after another (they share the screen and the mouse).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiCollection : ICollectionFixture<UiTestEnvironment>
{
    public const string Name = "UI";
}
