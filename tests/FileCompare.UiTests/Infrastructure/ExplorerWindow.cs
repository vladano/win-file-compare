using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace FileCompare.UiTests.Infrastructure;

/// <summary>A File Explorer window showing one folder, used as the source of real drag and drop.</summary>
public sealed class ExplorerWindow : IDisposable
{
    private readonly bool _openedByUs;

    private readonly IntPtr _handle;

    private ExplorerWindow(Window window, IntPtr handle, bool openedByUs)
    {
        Window = window;
        _handle = handle;
        _openedByUs = openedByUs;
    }

    public Window Window { get; }

    /// <summary>Opens <paramref name="folder"/> in Explorer. If Windows shows it in a window that was already open
    /// (as a new tab), that window is used but is not closed afterwards.</summary>
    public static ExplorerWindow Open(UIA3Automation automation, string folder)
    {
        string full = Path.GetFullPath(folder).TrimEnd('\\');
        var before = ShellWindows().Select(w => w.Handle).ToHashSet();

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = false })?.Dispose();

        // Found by the folder it shows, not by its title: the title may be the folder name or the full path, and
        // Windows cuts long titles short. A new window is preferred; an existing one is used if Explorer showed the
        // folder there.
        List<string> seen = new();
        IntPtr handle;
        try
        {
            handle = (IntPtr)Wait.For<object>(() =>
            {
                var all = ShellWindows();
                seen = all.Select(w => w.Path).ToList();
                var matches = all.Where(w => string.Equals(w.Path, full, StringComparison.OrdinalIgnoreCase)).Select(w => w.Handle).ToList();
                var match = matches.FirstOrDefault(h => !before.Contains(h));
                if (match == IntPtr.Zero) match = matches.FirstOrDefault();
                return match == IntPtr.Zero ? null : match;
            }, $"an Explorer window for {Path.GetFileName(folder)}");
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{ex.Message}. Explorer shows: {string.Join(", ", seen.Select(s => $"\"{s}\""))}", ex);
        }

        var window = automation.FromHandle(handle).AsWindow();
        // The items appear a moment after the window.
        Wait.For(() => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)), "the items in the Explorer window");
        return new ExplorerWindow(window, handle, !before.Contains(handle));
    }

    /// <summary>The open Explorer windows (and tabs) with the folder each one shows, from Shell.Application.</summary>
    private static List<(IntPtr Handle, string Path)> ShellWindows()
    {
        var list = new List<(IntPtr, string)>();
        if (Type.GetTypeFromProgID("Shell.Application") is not { } type) return list;

        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            foreach (dynamic window in shell.Windows())
            {
                try
                {
                    string url = window.LocationURL;
                    long hwnd = window.HWND;
                    if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                        list.Add((new IntPtr(hwnd), new Uri(url).LocalPath.TrimEnd('\\')));
                }
                catch (Exception ex) when (ex is COMException or RuntimeBinderException or UriFormatException)
                {
                    // the window is closing
                }
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
        return list;
    }

    /// <summary>The item for a file or folder (matched with and without its extension, since Explorer may hide it).</summary>
    public AutomationElement Item(string path)
    {
        string full = Path.GetFileName(path), bare = Path.GetFileNameWithoutExtension(path);
        return Wait.For(() => Window.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .FirstOrDefault(i => i.Name == full || i.Name == bare), $"the Explorer item {full}");
    }

    /// <summary>A point on the item's name (in the details view the row's middle can be an empty column).</summary>
    public Point PointOn(string path)
    {
        var item = Item(path);
        if (item.Patterns.ScrollItem.TryGetPattern(out var scroll))
        {
            scroll.ScrollIntoView();
            Thread.Sleep(150);
        }
        var nameCell = item.FindFirstChild(cf => cf.ByName(item.Name));
        var rect = (nameCell ?? item).BoundingRectangle;
        return new Point(rect.Left + Math.Min(40, rect.Width / 2), rect.Top + rect.Height / 2);
    }

    /// <summary>Selects the items as a user would: a click on the first, Ctrl+click on the others.</summary>
    public void Select(params string[] paths)
    {
        Mouse.Click(PointOn(paths[0]));
        Thread.Sleep(200);
        foreach (var path in paths.Skip(1))
        {
            using (Keyboard.Pressing(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL)) Mouse.Click(PointOn(path));
            Thread.Sleep(200);
        }
    }

    public void Dispose()
    {
        if (_openedByUs) Close(_handle);
    }

    /// <summary>
    /// Closes every Explorer window that shows a folder below <paramref name="root"/> (the test files) and waits until
    /// they are gone. Otherwise Explorer would reuse a window that is still closing for the next test.
    /// </summary>
    public static void CloseTestWindows(string root)
    {
        string prefix = Path.GetFullPath(root).TrimEnd('\\');
        bool IsTest((IntPtr Handle, string Path) w) =>
            w.Path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || w.Path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase);

        foreach (var handle in ShellWindows().Where(IsTest).Select(w => w.Handle).Distinct().ToList())
            Close(handle);
    }

    private static void Close(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !Native.IsWindow(handle)) return;
        Native.CloseWindow(handle);
        Wait.Until(() => !Native.IsWindow(handle), "the Explorer window to close");
    }
}

/// <summary>Real drag and drop with mouse input, the way a user does it.</summary>
public static class DragAndDrop
{
    /// <summary>
    /// Presses the left button at <paramref name="from"/>, moves past the drag threshold so the drag starts, moves
    /// to <paramref name="to"/> in small steps, waits there so the target sees the drag, and releases.
    /// </summary>
    public static void Drag(Point from, Point to)
    {
        Native.MoveMouse(from);
        Thread.Sleep(150);
        Mouse.Down(MouseButton.Left);
        Thread.Sleep(200);

        for (int i = 1; i <= 6; i++)
        {
            Native.MoveMouse(new Point(from.X + i * 3, from.Y + i * 2));
            Thread.Sleep(40);
        }

        var start = new Point(from.X + 18, from.Y + 12);
        const int steps = 30;
        for (int i = 1; i <= steps; i++)
        {
            Native.MoveMouse(new Point(start.X + (to.X - start.X) * i / steps, start.Y + (to.Y - start.Y) * i / steps));
            Thread.Sleep(20);
        }

        for (int i = 0; i < 8; i++)
        {
            Native.MoveMouse(new Point(to.X + i % 2, to.Y));
            Thread.Sleep(60);
        }

        Mouse.Up(MouseButton.Left);
        Thread.Sleep(400);
    }

    public static Point Center(AutomationElement element)
    {
        var r = element.BoundingRectangle;
        return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
    }
}
