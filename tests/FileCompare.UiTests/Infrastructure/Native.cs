using System.Drawing;
using System.Runtime.InteropServices;

namespace FileCompare.UiTests.Infrastructure;

/// <summary>The few Windows calls the tests need that FlaUI does not wrap.</summary>
internal static class Native
{
    /// <summary>
    /// Makes this process per-monitor DPI aware, so UI Automation rectangles, the mouse and window positions all use
    /// real screen pixels. Without it, coordinates are scaled on displays above 100 % and drags miss their target.
    /// </summary>
    public static void UseScreenPixels() => SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2

    /// <summary>The desktop area without the taskbar, of the main screen.</summary>
    public static Rectangle WorkArea()
    {
        var rect = new RECT();
        SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, ref rect, 0);
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    /// <summary>
    /// Moves the mouse with real input events (SendInput). Drag and drop between programs is driven by these events,
    /// so a simulated drag needs them rather than just setting the cursor position.
    /// </summary>
    public static void MoveMouse(Point screen)
    {
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77);       // SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN
        int width = GetSystemMetrics(78), height = GetSystemMetrics(79);   // SM_CXVIRTUALSCREEN, SM_CYVIRTUALSCREEN
        var input = new INPUT
        {
            type = 0, // INPUT_MOUSE
            mi = new MOUSEINPUT
            {
                dx = (int)Math.Round((screen.X - left) * 65535.0 / Math.Max(1, width - 1)),
                dy = (int)Math.Round((screen.Y - top) * 65535.0 / Math.Max(1, height - 1)),
                dwFlags = 0x0001 | 0x8000 | 0x4000 // MOVE | ABSOLUTE | VIRTUALDESK
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Asks a window to close, like its close button does (WM_CLOSE).</summary>
    public static void CloseWindow(IntPtr handle) => PostMessage(handle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, ref RECT rect, uint winIni);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        // The native struct holds a union of MOUSEINPUT, KEYBDINPUT and HARDWAREINPUT. MOUSEINPUT is the largest
        // of the three, so declaring only it gives the same size and layout.
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }
}
