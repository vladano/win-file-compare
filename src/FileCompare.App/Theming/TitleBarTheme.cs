using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FileCompare.App.Theming;

/// <summary>Switches the native title bar between light and dark (works on Windows 10 2004 and newer).</summary>
public static class TitleBarTheme
{
    private const int DwmUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
