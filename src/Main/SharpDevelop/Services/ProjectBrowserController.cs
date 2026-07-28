using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using ICSharpCode.SharpDevelop.Services;
using ICSharpCode.SharpDevelop.Templates;

namespace UnoDevelop.Services;

/// <summary>The WinUI concrete Project Browser controller - see ProjectBrowserControllerBase (shared,
/// in ICSharpCode.SharpDevelop.Services) for the command surface. Only the native dialog/clipboard
/// touchpoints live here.</summary>
internal sealed class ProjectBrowserController : ProjectBrowserControllerBase
{
    public ProjectBrowserController() : base((IProjectBrowserService)ICSharpCode.SharpDevelop.SD.ProjectService)
    {
    }

    protected override async Task<NewItemDialogOutcome?> ShowNewItemDialogAsync(TemplateDiscoveryService service, string targetDirectory)
    {
        var dialog = await UnoDevelop.Templates.NewItemDialog.ShowAsync(service, targetDirectory);
        if (dialog is null || dialog.SelectedTemplate is null)
            return null;

        return new NewItemDialogOutcome(dialog.SelectedTemplate, dialog.ItemName,
            new Dictionary<string, string>(dialog.AdditionalParameters, StringComparer.OrdinalIgnoreCase));
    }

    protected override async Task<NewProjectDialogOutcome?> ShowNewProjectDialogAsync(TemplateDiscoveryService service, string defaultLocation)
    {
        var dialog = await UnoDevelop.Templates.NewProjectDialog.ShowAsync(service, defaultLocation);
        if (dialog is null || dialog.SelectedTemplate is null)
            return null;

        return new NewProjectDialogOutcome(dialog.SelectedTemplate, dialog.ProjectName, dialog.Location,
            new Dictionary<string, string>(dialog.AdditionalParameters, StringComparer.OrdinalIgnoreCase));
    }

    protected override void CopyTextToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
