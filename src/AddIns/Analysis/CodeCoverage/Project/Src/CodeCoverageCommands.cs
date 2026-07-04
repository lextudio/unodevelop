using System.Threading.Tasks;
using ICSharpCode.Core;

namespace UnoDevelop.AddIns.Analysis.CodeCoverage;

public sealed class RunAllTestsWithCodeCoverageCommand : AbstractMenuCommand
{
    public override void Run() => _ = CodeCoverageService.Instance.RunAllTestsWithCoverageAsync();
}

public sealed class OpenCoverageFileCommand : AbstractMenuCommand
{
    public override void Run() => _ = RunAsync();

    private static async Task RunAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Code Coverage Results",
            Filter = "OpenCover XML|*.xml|All files|*.*",
            FilterIndex = 1
        };

        if (await dialog.ShowDialogAsync() == true && !string.IsNullOrEmpty(dialog.FileName))
        {
            CodeCoverageService.Instance.LoadCoverageFile(dialog.FileName);
        }
    }
}
