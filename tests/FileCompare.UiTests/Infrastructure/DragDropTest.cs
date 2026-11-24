using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace FileCompare.UiTests.Infrastructure;

/// <summary>
/// Base of the drag and drop tests (docs/TESTING.md, section 3). Each test starts the app with a comparison, opens a
/// real Explorer window on the drag folder, and drags items with the mouse. The app is on the left 60 % of the
/// screen and Explorer on the rest, so neither covers the other.
/// </summary>
public abstract class DragDropTest : IDisposable
{
    private readonly UiTestEnvironment _env;
    private readonly Window? _app;
    private ExplorerWindow? _explorer;

    /// <param name="args">What the app is started with (no arguments: the Home tab).</param>
    /// <param name="title">The window title the app shows once it is ready.</param>
    protected DragDropTest(UiTestEnvironment env, string[] args, string title)
    {
        _env = env;
        if (!UiFactAttribute.IsEnabled) return;

        App.KillAll();
        App.Start(args).Dispose();
        _app = App.WaitForWindow(t => t == title, $"the app window \"{title}\"");
        OpenExplorer(Files.DragFolder);
    }

    protected AppDriver App => _env.App;
    protected TestFiles Files => _env.Files;
    protected Window AppWindow => _app!;
    protected int AppProcessId => AppWindow.Properties.ProcessId.Value;

    /// <summary>The Explorer window, on the drag folder unless a test opened another one.</summary>
    protected ExplorerWindow Explorer => _explorer!;

    /// <summary>Shows another folder (or a .zip, which Explorer treats as a folder) in the Explorer window.</summary>
    protected ExplorerWindow OpenExplorer(string folder)
    {
        _explorer?.Dispose();
        ExplorerWindow.CloseTestWindows(_env.Root);
        _explorer = ExplorerWindow.Open(_env.Automation, folder);
        Arrange();
        return _explorer;
    }

    private void Arrange()
    {
        var area = Native.WorkArea();
        int appWidth = area.Width * 60 / 100;
        Place(AppWindow, area.Left, area.Top, appWidth, area.Height);
        if (_explorer is not null) Place(_explorer.Window, area.Left + appWidth, area.Top, area.Width - appWidth, area.Height);
        AppWindow.SetForeground();
        Thread.Sleep(500);
    }

    private static void Place(Window window, int x, int y, int width, int height)
    {
        window.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
        window.Patterns.Transform.Pattern.Move(x, y);
        window.Patterns.Transform.Pattern.Resize(width, height);
    }

    // ----- Finding things in the app -----

    /// <summary>An element of the page shown in the app (path box, editor, status text...), by its x:Name.</summary>
    protected AutomationElement Target(string automationId) =>
        Wait.For(() => AppWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), $"the app's {automationId}");

    protected string PathBox(string automationId) => Target(automationId).AsTextBox().Text;

    protected string? TextOf(string automationId) => AppDriver.TextOf(AppWindow, automationId);

    protected void WaitForTitle(string title) =>
        Wait.Until(() => AppWindow.Title == title, $"the window title \"{title}\" (it is \"{AppWindow.Title}\")");

    protected void WaitForTitle(Func<string, bool> title, string what) =>
        Wait.Until(() => title(AppWindow.Title), $"{what} (the title is \"{AppWindow.Title}\")");

    protected void WaitForText(string automationId, Func<string, bool> text, string what) =>
        Wait.Until(() => TextOf(automationId) is { } value && text(value), $"{what} (it is \"{TextOf(automationId)}\")");

    protected List<string> Tabs() => AppDriver.Tabs(AppWindow);

    /// <summary>The window title for two paths, as the app writes it.</summary>
    protected static string TitleOf(string left, string right, string view) =>
        $"{Path.GetFileName(left)} <--> {Path.GetFileName(right)} - {view}";

    /// <summary>Two dropped items may arrive in either order (Explorer decides), so both titles are accepted.</summary>
    protected static Func<string, bool> EitherOrder(string first, string second, string view) =>
        t => t == TitleOf(first, second, view) || t == TitleOf(second, first, view);

    /// <summary>The text on a tab for two paths.</summary>
    protected static string TabOf(string left, string right) => $"{Path.GetFileName(left)} \u2194 {Path.GetFileName(right)}";

    // ----- Dragging -----

    /// <summary>Selects items in Explorer and drags them onto the middle of <paramref name="automationId"/>.</summary>
    protected void Drop(string automationId, params string[] items) => Drop(DragAndDrop.Center(Target(automationId)), items);

    /// <summary>Selects items in Explorer and drags them to a point of the app.</summary>
    protected void Drop(Point to, params string[] items)
    {
        Explorer.Select(items);
        EnsureAppAt(to);
        DragAndDrop.Drag(Explorer.PointOn(items[^1]), to);
    }

    /// <summary>A point near the start of an element, for elements whose middle may be empty space (headings, rows).</summary>
    protected static Point NearStart(AutomationElement element)
    {
        var r = element.BoundingRectangle;
        return new Point(r.Left + Math.Min(20, r.Width / 2), r.Top + r.Height / 2);
    }

    /// <summary>If another window covers the target, a drop would go there; fail with a clear reason instead.</summary>
    protected void EnsureAppAt(Point point)
    {
        var underMouse = _env.Automation.FromPoint(point);
        Assert.True(underMouse?.Properties.ProcessId.ValueOrDefault == AppProcessId,
            $"Something covers the app at {point} (found \"{underMouse?.Name}\"). Close or move other windows and run again.");
    }

    /// <summary>Clicks a button of the app's message box that contains <paramref name="text"/>.</summary>
    protected void Answer(string text, string button)
    {
        var box = Wait.For(() => App.FindMessageBox(text), $"a message \"{text}\"");
        var target = Wait.For(() => box.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(button))),
            $"the {button} button");
        target.AsButton().Invoke();
        Wait.Until(() => App.FindMessageBox(text) is null, "the message to close");
    }

    public virtual void Dispose()
    {
        if (UiFactAttribute.IsEnabled)
        {
            _explorer?.Dispose();
            ExplorerWindow.CloseTestWindows(_env.Root);
            App.KillAll();
        }
        GC.SuppressFinalize(this);
    }
}
