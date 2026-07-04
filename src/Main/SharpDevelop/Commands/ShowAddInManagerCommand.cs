using System;
using System.Threading.Tasks;
using ICSharpCode.Core;
using Microsoft.UI.Xaml;

namespace UnoDevelop.Commands;

internal sealed class ShowAddInManagerCommand : AbstractMenuCommand
{
    public override void Run()
    {
        _ = ShowAsync();
    }

    private static async Task ShowAsync()
    {
        var dialogType = ResolvePackageManagementType("UnoDevelop.AddIns.AddInManagerDialog");
        if (dialogType is null || Activator.CreateInstance(dialogType) is not ContentDialog dialog)
        {
            ServiceSingleton.GetRequiredService<IMessageService>().ShowError("Package Management addin is not available.");
            return;
        }

        dialog.XamlRoot = MainPage.Current?.XamlRoot;
        await dialog.ShowAsync();
    }

    private static Type? ResolvePackageManagementType(string typeName)
    {
        foreach (var addIn in ServiceSingleton.GetRequiredService<IAddInTree>().AddIns)
        {
            if (!addIn.Enabled || !addIn.Manifest.Identities.ContainsKey("UnoDevelop.PackageManagement"))
            {
                continue;
            }

            foreach (var runtime in addIn.Runtimes)
            {
                var type = runtime.LoadedAssembly?.GetType(typeName);
                if (type is not null)
                {
                    return type;
                }
            }
        }

        return Type.GetType(typeName + ", UnoDevelop.PackageManagement", throwOnError: false);
    }
}
