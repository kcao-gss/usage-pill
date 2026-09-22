using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UsagePill.Ui;

/// <summary>
/// Keeps a window out of the Alt+Tab switcher.
/// </summary>
/// <remarks>
/// WPF's <see cref="Window.ShowInTaskbar"/>=false only clears WS_EX_APPWINDOW and reparents the
/// window to a hidden owner window. That is enough to drop the taskbar button, but the Windows 11
/// switcher still lists the window, so an overlay that should never be a switch target has to set
/// WS_EX_TOOLWINDOW itself. WindowStyle="ToolWindow" is not an option: it would set the same bit
/// but also force WS_CAPTION, and these windows are chromeless.
///
/// WPF reads the live extended style back off the HWND whenever it recomputes styles (see
/// Window._StyleEx), so the bit set here survives later WPF-driven style updates.
/// </remarks>
internal static class TaskSwitcher
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>
    /// Excludes <paramref name="window"/> from the switcher. Call no earlier than
    /// <see cref="Window.OnSourceInitialized"/>, which is the first point the HWND exists, and
    /// still before the window is shown, so the switcher never sees it as a normal app window.
    /// </summary>
    internal static void Exclude(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var exStyle = NativeMethods.GetWindowLong(handle, GWL_EXSTYLE);
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return;

        NativeMethods.SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
    }

    private static class NativeMethods
    {
        // GWL_EXSTYLE holds a 32-bit value on every architecture, so the non-Ptr entry points are
        // correct here; only pointer-valued indices (GWLP_WNDPROC and friends) need GetWindowLongPtr.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
