using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AiPet.SystemIntegration;

public static class WindowClickThrough
{
    private const int ExtendedStyle = -20;
    private const long TransparentStyle = 0x00000020L;

    public static void Set(Window window, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var current = GetWindowLongPtr(handle, ExtendedStyle).ToInt64();
        var updated = enabled ? current | TransparentStyle : current & ~TransparentStyle;
        if (updated == current) return;
        SetLastError(0);
        var result = SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(updated));
        var error = Marshal.GetLastWin32Error();
        if (result == IntPtr.Zero && error != 0) throw new Win32Exception(error);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr window, int index);
    private static IntPtr GetWindowLongPtr(IntPtr window, int index) => IntPtr.Size == 8
        ? GetWindowLongPtr64(window, index)
        : new IntPtr(GetWindowLong32(window, index));

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr window, int index, int value);
    private static IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value) => IntPtr.Size == 8
        ? SetWindowLongPtr64(window, index, value)
        : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));

    [DllImport("kernel32.dll")] private static extern void SetLastError(uint errorCode);
}
