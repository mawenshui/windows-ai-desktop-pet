using System.Diagnostics;
using System.IO;
using System.Windows;
using AiPet.Search;

namespace AiPet.ToolWindow;

/// <summary>
/// User-initiated Windows actions for one search result. Keeping these calls
/// behind a small boundary lets the view-model report stable failures without
/// coupling tests to Explorer or the clipboard.
/// </summary>
public interface ISearchResultActions
{
    void Open(SearchItem item);
    void Reveal(SearchItem item);
    void CopyPath(SearchItem item);
}

public sealed class WindowsSearchResultActions : ISearchResultActions
{
    private readonly Action<ProcessStartInfo> _startProcess;
    private readonly Action<string> _writeClipboard;

    public WindowsSearchResultActions(
        Action<ProcessStartInfo>? startProcess = null,
        Action<string>? writeClipboard = null)
    {
        _startProcess = startProcess ?? (startInfo => Process.Start(startInfo));
        _writeClipboard = writeClipboard ?? Clipboard.SetText;
    }

    public void Open(SearchItem item)
    {
        EnsureTargetExists(item);
        _startProcess(new ProcessStartInfo
        {
            FileName = item.FullPath,
            UseShellExecute = true,
        });
    }

    public void Reveal(SearchItem item)
    {
        EnsureTargetExists(item);

        // A drive root has no parent to select, so opening it is the only
        // useful Explorer action. All other targets are selected in place.
        if (item.Kind == SearchItemKind.Folder
            && Directory.GetParent(item.FullPath) is null)
        {
            Open(item);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add("/select," + item.FullPath);
        _startProcess(startInfo);
    }

    public void CopyPath(SearchItem item) => _writeClipboard(item.FullPath);

    private static void EnsureTargetExists(SearchItem item)
    {
        if (!item.ExistsNow)
            throw new FileNotFoundException("The selected search result no longer exists.");
    }
}
