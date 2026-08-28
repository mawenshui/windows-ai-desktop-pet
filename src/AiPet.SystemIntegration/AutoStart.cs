using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AiPet.SystemIntegration;

/// <summary>
/// Per-user autorun toggle via <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// We do NOT write to HKLM (would require admin) and we do NOT silently
/// elevate. Per PRD AUTO-02, a failed write is rolled back to the actual
/// state and surfaced with a reason.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowsAiDesktopPet";

    public static bool IsEnabled(out string? currentCommand)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        if (key is null) { currentCommand = null; return false; }
        var v = key.GetValue(ValueName) as string;
        currentCommand = v;
        return !string.IsNullOrEmpty(v);
    }

    public static void Enable()
    {
        var exe = GetExecutablePath();
        // Quote the path so spaces in the install dir don't break argv parsing.
        var cmd = "\"" + exe + "\" --tray";
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("无法打开 Run 注册表项");
        key.SetValue(ValueName, cmd, RegistryValueKind.String);
        // Verify the write actually took effect (PRD AUTO-02).
        using var verify = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var written = verify?.GetValue(ValueName) as string;
        if (!string.Equals(written, cmd, StringComparison.Ordinal))
            throw new InvalidOperationException("注册表写入未生效,已自动回滚");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return; // nothing to remove
        if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string GetExecutablePath()
    {
        // Process.MainModule.FileName is the actual exe path even when
        // running from a packaged location. Fall back to the assembly
        // location just in case.
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName
                ?? typeof(AutoStart).Assembly.Location;
        }
        catch
        {
            return typeof(AutoStart).Assembly.Location;
        }
    }
}
