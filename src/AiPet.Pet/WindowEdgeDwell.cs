using System.Runtime.InteropServices;
using System.Windows;
using Point = System.Windows.Point;

namespace AiPet.Pet;

/// <summary>Keeps the original attachment while focus temporarily pauses following.</summary>
public sealed class WindowEdgeDwellSession
{
    public IntPtr Target { get; private set; }
    public bool IsQuiet { get; private set; }
    public bool CanFollow => Target != IntPtr.Zero && !IsQuiet;

    public void Attach(IntPtr target)
    {
        if (target == IntPtr.Zero) throw new ArgumentException("目标窗口无效。", nameof(target));
        Target = target;
    }

    public void SetQuiet(bool quiet) => IsQuiet = quiet;
    public void Detach() => Target = IntPtr.Zero;
}

/// <summary>Geometry rules in WPF device-independent pixels.</summary>
public static class WindowEdgeDwellPolicy
{
    public const double SnapTolerance = 18;
    public const double MinimumOverlap = 50;
    public const double FootInset = 6;

    public static Point? TryPlace(Rect visual, Rect target, Rect workArea)
    {
        if (visual.Width <= 0 || visual.Height <= 0 || target.Width < 200 || target.Height < 120)
            return null;
        if (Math.Abs(visual.Bottom - target.Top) > SnapTolerance)
            return null;
        if (Math.Min(visual.Right, target.Right) - Math.Max(visual.Left, target.Left) < MinimumOverlap)
            return null;
        return PlaceOnTop(visual.Width, visual.Height, visual.Left, target, workArea);
    }

    public static Point? PlaceOnTop(double visualWidth, double visualHeight, double desiredLeft, Rect target, Rect workArea)
    {
        if (visualWidth <= 0 || visualHeight <= 0 || target.Width < 200 || target.Height < 120)
            return null;
        var top = target.Top - visualHeight + FootInset;
        if (top < workArea.Top + PetWindowPositioner.WorkAreaMargin ||
            top + visualHeight > workArea.Bottom - PetWindowPositioner.WorkAreaMargin ||
            target.Top < workArea.Top + 80 || target.Bottom > workArea.Bottom + SnapTolerance)
            return null;
        var left = Math.Clamp(desiredLeft,
            workArea.Left + PetWindowPositioner.WorkAreaMargin,
            Math.Max(workArea.Left + PetWindowPositioner.WorkAreaMargin,
                workArea.Right - visualWidth - PetWindowPositioner.WorkAreaMargin));
        if (Math.Min(left + visualWidth, target.Right) - Math.Max(left, target.Left) < MinimumOverlap)
            return null;
        return new Point(left, top);
    }

    public static Point PlaceOnDesktop(Rect visual, Rect workArea)
    {
        var left = Math.Clamp(visual.Left,
            workArea.Left + PetWindowPositioner.WorkAreaMargin,
            Math.Max(workArea.Left + PetWindowPositioner.WorkAreaMargin,
                workArea.Right - visual.Width - PetWindowPositioner.WorkAreaMargin));
        var top = Math.Max(workArea.Top + PetWindowPositioner.WorkAreaMargin,
            workArea.Bottom - visual.Height - PetWindowPositioner.WorkAreaMargin);
        return new Point(left, top);
    }
}

/// <summary>Reads window bounds only; no titles, content, process names or activity history.</summary>
internal static class WindowEdgeTargetProbe
{
    private const uint MonitorDefaultToNearest = 2;

    internal static bool TryFind(PetWindow pet, Rect visual, out IntPtr handle, out Rect target, out Rect work)
    {
        handle = IntPtr.Zero;
        target = default;
        work = default;
        var petCenter = pet.PointToScreen(new Point(pet.ActualWidth / 2, pet.ActualHeight / 2));
        var monitor = MonitorFromPoint(new POINT((int)Math.Round(petCenter.X), (int)Math.Round(petCenter.Y)), MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero || !TryWorkArea(pet, monitor, out work)) return false;
        var workArea = work;
        IntPtr candidate = IntPtr.Zero;
        Rect candidateBounds = default;
        EnumWindows((window, _) =>
        {
            if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window) || MonitorFromWindow(window, MonitorDefaultToNearest) != monitor)
                return true;
            GetWindowThreadProcessId(window, out var processId);
            if (processId == (uint)Environment.ProcessId || !GetWindowRect(window, out var rect)) return true;
            var bounds = ToLogical(pet, rect);
            if (WindowEdgeDwellPolicy.TryPlace(visual, bounds, workArea) is null) return true;
            candidate = window;
            candidateBounds = bounds;
            return false;
        }, IntPtr.Zero);
        if (candidate == IntPtr.Zero) return false;
        handle = candidate;
        target = candidateBounds;
        return true;
    }

    internal static bool TryRead(PetWindow pet, IntPtr handle, out Rect bounds, out Rect work)
    {
        bounds = default;
        work = default;
        if (handle == IntPtr.Zero || !IsWindow(handle) || !IsWindowVisible(handle) || IsIconic(handle) || !GetWindowRect(handle, out var rect))
            return false;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero || !TryWorkArea(pet, monitor, out work)) return false;
        bounds = ToLogical(pet, rect);
        return bounds.Width >= 200 && bounds.Height >= 120;
    }

    private static bool TryWorkArea(PetWindow pet, IntPtr monitor, out Rect area)
    {
        area = default;
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        area = ToLogical(pet, info.Work);
        return area.Width > 0 && area.Height > 0;
    }

    private static Rect ToLogical(PetWindow pet, RECT rect)
    {
        var first = pet.PointFromScreen(new Point(rect.Left, rect.Top));
        var last = pet.PointFromScreen(new Point(rect.Right, rect.Bottom));
        return new Rect(pet.Left + first.X, pet.Top + first.Y, last.X - first.X, last.Y - first.Y);
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [StructLayout(LayoutKind.Sequential)] private struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }
}
