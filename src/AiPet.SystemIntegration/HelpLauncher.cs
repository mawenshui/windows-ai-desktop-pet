using System;
using System.Diagnostics;
using System.IO;

namespace AiPet.SystemIntegration;

/// <summary>
/// Opens the offline user manual with the system default handler. If the
/// file is missing or no app is associated, we surface a clean error
/// string instead of opening an unverified URL (PRD HELP-02).
/// </summary>
public static class HelpLauncher
{
    /// <summary>
    /// Resolves the manual path: AIPET_HELP environment override first,
    /// then a path next to the executable (portable + install-friendly),
    /// then docs/USER_MANUAL.html in the repo (dev mode).
    /// </summary>
    public static string? FindManualPath()
    {
        var byEnv = Environment.GetEnvironmentVariable("AIPET_HELP");
        if (!string.IsNullOrEmpty(byEnv) && File.Exists(byEnv)) return byEnv;

        var exeDir = Path.GetDirectoryName(typeof(HelpLauncher).Assembly.Location);
        if (exeDir is not null)
        {
            var alongside = Path.Combine(exeDir, "docs", "USER_MANUAL.html");
            if (File.Exists(alongside)) return alongside;
        }
        // Dev tree fallback: walk up from the assembly dir looking for repo root.
        var dir = new DirectoryInfo(exeDir ?? ".");
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "USER_MANUAL.html");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static HelpOpenResult OpenManual()
    {
        var path = FindManualPath();
        if (path is null) return HelpOpenResult.Missing();
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return HelpOpenResult.Ok();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return HelpOpenResult.NoAssociation();
        }
        catch (Exception ex)
        {
            return HelpOpenResult.Other(ex.Message);
        }
    }
}

public enum HelpOpenKind { Ok, Missing, NoAssociation, OtherError }
public sealed record HelpOpenResult(HelpOpenKind Kind, string? Path, string? Message)
{
    public static HelpOpenResult Ok()                  => new(HelpOpenKind.Ok, null, null);
    public static HelpOpenResult Missing()            => new(HelpOpenKind.Missing, null, "未找到离线手册,请检查安装");
    public static HelpOpenResult NoAssociation()      => new(HelpOpenKind.NoAssociation, null, "系统未关联 .html 程序");
    public static HelpOpenResult Other(string msg)     => new(HelpOpenKind.OtherError, null, msg);
}
