using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.NuGet;
using ICSharpCode.SharpDevelop.Project;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.AddIns;

/// <summary>
/// Read-only "installed packages" view (docs/nuget-manager.md slice 2) — lists a project's
/// installed NuGet packages via <see cref="UnoNuGetProject"/>. Search/install/update/uninstall
/// land in later slices; this only reads.
/// </summary>
public static class ManagePackagesDialog
{
    public static async Task ShowAsync(IProject project, XamlRoot? xamlRoot = null)
    {
        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            MinHeight = 300,
            MinWidth = 450,
            HeaderTemplate = BuildRowTemplate(isHeader: true),
            ItemTemplate = BuildRowTemplate(isHeader: false)
        };
        listView.Header = new InstalledPackageRow("Package", "Version");

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var statusText = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(statusText, 0);
        Grid.SetRow(listView, 1);
        content.Children.Add(statusText);
        content.Children.Add(listView);

        var dialog = new ContentDialog
        {
            Title = $"NuGet Packages — {project.Name}",
            Content = content,
            PrimaryButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        try
        {
            var nugetProject = UnoNuGetProject.FromProject(project);
            var installed = await nugetProject.GetInstalledPackagesAsync(CancellationToken.None);
            var rows = installed
                .OrderBy(reference => reference.PackageIdentity.Id, StringComparer.OrdinalIgnoreCase)
                .Select(reference => new InstalledPackageRow(reference.PackageIdentity.Id, reference.PackageIdentity.Version.ToNormalizedString()))
                .ToArray();

            listView.ItemsSource = rows;
            statusText.Text = rows.Length == 0
                ? "No installed packages."
                : $"{rows.Length} installed package(s).";
        }
        catch (Exception ex)
        {
            LoggingService.Warn($"Failed to list installed packages for {project.FileName}: {ex.Message}");
            statusText.Text = $"Failed to load packages: {ex.Message}";
        }

        await dialog.ShowAsync();
    }

    static DataTemplate BuildRowTemplate(bool isHeader)
    {
        return new DataTemplate(() =>
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var idText = new TextBlock
            {
                FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            idText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPackageRow.Id)) });

            var versionText = new TextBlock
            {
                FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            versionText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPackageRow.Version)) });

            Grid.SetColumn(idText, 0);
            Grid.SetColumn(versionText, 1);
            grid.Children.Add(idText);
            grid.Children.Add(versionText);
            return grid;
        });
    }

    sealed record InstalledPackageRow(string Id, string Version);
}
