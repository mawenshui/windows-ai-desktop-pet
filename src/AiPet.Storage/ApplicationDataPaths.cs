using System;
using System.IO;

namespace AiPet.Storage;

/// <summary>
/// Resolves files created while the application is running. Keeping these
/// files outside the installation directory allows a normal uninstall to
/// remove every installed file and directory.
/// </summary>
public static class ApplicationDataPaths
{
    private const string ProductDirectoryName = "WindowsAiDesktopPet";

    public static string GetDiagnosticLogPath(string? localApplicationDataRoot = null)
    {
        var root = localApplicationDataRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("The local application data directory is unavailable.");
        }

        return Path.Combine(root, ProductDirectoryName, "logs", "aipet-debug.log");
    }
}
