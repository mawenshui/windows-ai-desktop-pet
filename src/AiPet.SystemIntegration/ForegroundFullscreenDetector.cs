using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace AiPet.SystemIntegration;

public static class FullscreenWindowPolicy
{
    public static bool CoversMonitor(Rect windowBounds, Rect monitorBounds, double tolerance = 2) =>
        windowBounds.Width > 0
        && windowBounds.Height > 0
        && windowBounds.Left <= monitorBounds.Left + tolerance
        && windowBounds.Top <= monitorBounds.Top + tolerance
        && windowBounds.Right >= monitorBounds.Right - tolerance
        && windowBounds.Bottom >= monitorBounds.Bottom - tolerance;
}

/// <summary>Reads only the current foreground window and monitor geometry.</summary>
public static class ForegroundFullscreenDetector
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint ExtendedFrameBounds = 9;

    public static bool IsForegroundFullscreenOnMonitor(Rect targetBounds)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsWindowVisible(foreground) || IsIconic(foreground)) return false;
        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == (uint)Environment.ProcessId) return false;

        var foregroundMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var targetPoint = new POINT
        {
            X = (int)Math.Round(targetBounds.Left + targetBounds.Width / 2),
            Y = (int)Math.Round(targetBounds.Top + targetBounds.Height / 2),
        };
        var targetMonitor = MonitorFromPoint(targetPoint, MonitorDefaultToNearest);
        if (foregroundMonitor == IntPtr.Zero || foregroundMonitor != targetMonitor) return false;
        if (DwmGetWindowAttribute(foreground, ExtendedFrameBounds, out var windowRect, Marshal.SizeOf<RECT>()) != 0
            && !GetWindowRect(foreground, out windowRect)) return false;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(foregroundMonitor, ref info)) return false;
        return FullscreenWindowPolicy.CoversMonitor(ToRect(windowRect), ToRect(info.Monitor));
    }

    public static bool IsForegroundFullscreenOnSameMonitor(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero) return false;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsWindowVisible(foreground) || IsIconic(foreground)) return false;
        GetWindowThreadProcessId(foreground, out var processId);
        if (processId == (uint)Environment.ProcessId) return false;
        var foregroundMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var targetMonitor = MonitorFromWindow(targetWindow, MonitorDefaultToNearest);
        if (foregroundMonitor == IntPtr.Zero || foregroundMonitor != targetMonitor) return false;
        if (DwmGetWindowAttribute(foreground, ExtendedFrameBounds, out var windowRect, Marshal.SizeOf<RECT>()) != 0
            && !GetWindowRect(foreground, out windowRect)) return false;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(foregroundMonitor, ref info)
            && FullscreenWindowPolicy.CoversMonitor(ToRect(windowRect), ToRect(info.Monitor));
    }

    private static Rect ToRect(RECT rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out RECT value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }
}
