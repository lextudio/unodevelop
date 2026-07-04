// Native file/folder picker service for UnoDevelop.
// Mirrors Roma's pattern: use Microsoft.Win32 shims from LeXtudio.Windows on all
// platforms. On Windows, FileDialogHost.ActiveWindow must be set at startup so
// WinRT.Interop.InitializeWithWindow wires up the HWND; on Skia/macOS it's a no-op.
//
// Usage:
//   var path = await FileDialogService.PickFileAsync(".sln", ".csproj");
//   var paths = await FileDialogService.PickFilesAsync();
//   var folder = await FileDialogService.PickFolderAsync();

using System.Threading.Tasks;
using Microsoft.Win32;

namespace UnoDevelop.Services;

internal static class FileDialogService
{
    /// Pick a single file. Returns null if cancelled.
    /// <param name="filter">File filter string.</param>
    /// <param name="initialDirectory">Optional directory to open the dialog in.</param>
    public static async Task<string?> PickFileAsync(string filter = "All files|*.*", string? initialDirectory = null)
    {
        var dlg = new OpenFileDialog { Filter = filter };
        if (!string.IsNullOrEmpty(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return await dlg.ShowDialogAsync() == true ? dlg.FileName : null;
    }

    /// Pick one or more files. Returns empty array if cancelled.
    public static async Task<string[]> PickFilesAsync(string filter = "All files|*.*")
    {
        var dlg = new OpenFileDialog { Filter = filter, Multiselect = true };
        return await dlg.ShowDialogAsync() == true ? dlg.FileNames : [];
    }

    /// Pick a folder. Returns null if cancelled.
    public static async Task<string?> PickFolderAsync()
    {
        var dlg = new OpenFolderDialog();
        return await dlg.ShowDialogAsync() == true ? dlg.FolderName : null;
    }

    /// Pick a save path. Returns null if cancelled.
    public static async Task<string?> PickSaveFileAsync(string filter = "All files|*.*", string suggestedName = "")
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = suggestedName };
        return await dlg.ShowDialogAsync() == true ? dlg.FileName : null;
    }
}
