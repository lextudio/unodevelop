#nullable enable

using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.NuGet;
using ICSharpCode.SharpDevelop.Project;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace UnoDevelop.AddIns;

/// <summary>
/// Native Uno package manager surface over the shared OpenDevelop NuGet services.
/// </summary>
public static class ManagePackagesDialog
{
    public static async Task ShowAsync(IProject project, XamlRoot? xamlRoot = null)
    {
        var model = new PackageManagerDialogModel(project, xamlRoot);
        await model.LoadInstalledAsync(CancellationToken.None);

        var installedList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MinHeight = 300,
            ItemTemplate = BuildInstalledRowTemplate(model)
        };
        installedList.ItemsSource = model.InstalledPackages;
        var includePrerelease = new CheckBox { Content = "Prerelease", VerticalAlignment = VerticalAlignment.Center };
        var checkUpdatesButton = new Button { Content = "Check Updates" };
        checkUpdatesButton.Click += (sender, args) =>
        {
            _ = model.CheckUpdatesAsync(includePrerelease.IsChecked == true, CancellationToken.None);
        };

        var searchBox = new TextBox { PlaceholderText = "Search NuGet packages...", MinWidth = 260 };
        var searchButton = new Button { Content = "Search" };
        var installButton = new Button { Content = "Install", IsEnabled = false };
        var searchResults = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MinHeight = 300,
            ItemTemplate = BuildSearchRowTemplate()
        };
        searchResults.ItemsSource = model.SearchResults;

        searchResults.SelectionChanged += (_, _) =>
        {
            installButton.IsEnabled = searchResults.SelectedItem is NuGetSearchResult;
            if (searchResults.SelectedItem is NuGetSearchResult result)
            {
                _ = model.LoadDependencyPreviewAsync(result, CancellationToken.None);
            }
        };

        searchButton.Click += (sender, args) =>
        {
            _ = model.SearchAsync(searchBox.Text ?? string.Empty, includePrerelease.IsChecked == true, CancellationToken.None);
        };
        installButton.Click += (sender, args) =>
        {
            if (searchResults.SelectedItem is NuGetSearchResult result)
            {
                _ = model.InstallAsync(result, CancellationToken.None);
            }
        };

        var installedTab = new Grid();
        installedTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        installedTab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var installedBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        installedBar.Children.Add(new TextBlock { Text = "Installed", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        installedBar.Children.Add(checkUpdatesButton);
        Grid.SetRow(installedBar, 0);
        Grid.SetRow(installedList, 1);
        installedTab.Children.Add(installedBar);
        installedTab.Children.Add(installedList);

        var searchTab = new Grid();
        searchTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        searchTab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        searchTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var searchBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        searchBar.Children.Add(searchBox);
        searchBar.Children.Add(includePrerelease);
        searchBar.Children.Add(searchButton);
        searchBar.Children.Add(installButton);
        Grid.SetRow(searchBar, 0);
        Grid.SetRow(searchResults, 1);
        var dependencyPreview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        dependencyPreview.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Source = model, Path = new PropertyPath(nameof(PackageManagerDialogModel.DependencyPreview)) });
        Grid.SetRow(dependencyPreview, 2);
        searchTab.Children.Add(searchBar);
        searchTab.Children.Add(searchResults);
        searchTab.Children.Add(dependencyPreview);

        var consoleOutput = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 260,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")
        };
        var consoleInput = new TextBox { PlaceholderText = "install <id> [version] | update <id> | uninstall <id> | list | help" };
        var consoleRunButton = new Button { Content = "Run" };
        Func<Task> runConsoleCommand = async () =>
        {
            var command = consoleInput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(command))
                return;

            var output = await model.RunConsoleCommandAsync(command, CancellationToken.None);
            consoleOutput.Text = string.IsNullOrEmpty(consoleOutput.Text)
                ? $"> {command}{Environment.NewLine}{output}"
                : $"{consoleOutput.Text}{Environment.NewLine}> {command}{Environment.NewLine}{output}";
            consoleInput.Text = string.Empty;
        };
        consoleRunButton.Click += (sender, args) => { _ = runConsoleCommand(); };
        consoleInput.KeyDown += (sender, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
                _ = runConsoleCommand();
        };

        var consoleTab = new Grid();
        consoleTab.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        consoleTab.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var consoleInputBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        consoleInputBar.Children.Add(consoleInput);
        consoleInputBar.Children.Add(consoleRunButton);
        Grid.SetRow(consoleOutput, 0);
        Grid.SetRow(consoleInputBar, 1);
        consoleTab.Children.Add(consoleOutput);
        consoleTab.Children.Add(consoleInputBar);

        var tabs = new TabView { IsAddTabButtonVisible = false };
        tabs.TabItems.Add(new TabViewItem { Header = "Installed", Content = installedTab });
        tabs.TabItems.Add(new TabViewItem { Header = "Search", Content = searchTab });
        tabs.TabItems.Add(new TabViewItem { Header = "Console", Content = consoleTab });

        var content = new Grid { MinWidth = 640, MinHeight = 420 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var statusText = new TextBlock { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        statusText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Source = model, Path = new PropertyPath(nameof(PackageManagerDialogModel.Status)) });
        Grid.SetRow(statusText, 0);
        Grid.SetRow(tabs, 1);
        content.Children.Add(statusText);
        content.Children.Add(tabs);

        var dialog = new ContentDialog
        {
            Title = $"NuGet Packages - {project.Name}",
            Content = content,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();
    }

    static DataTemplate BuildInstalledRowTemplate(PackageManagerDialogModel model)
    {
        return new DataTemplate(() =>
        {
            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var idText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            idText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPackageRow.Id)) });

            var versionText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
            versionText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPackageRow.Version)) });

            var updateText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 110 };
            updateText.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPackageRow.UpdateLabel)) });

            var updateButton = new Button { Content = "Update" };
            updateButton.SetBinding(Button.IsEnabledProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(InstalledPackageRow.HasUpdate)) });
            updateButton.Click += (sender, args) =>
            {
                if (sender is FrameworkElement { DataContext: InstalledPackageRow row })
                {
                    _ = model.UpdateAsync(row, CancellationToken.None);
                }
            };

            var removeButton = new Button { Content = "Uninstall" };
            removeButton.Click += (sender, args) =>
            {
                if (sender is FrameworkElement { DataContext: InstalledPackageRow row })
                {
                    _ = model.UninstallAsync(row, CancellationToken.None);
                }
            };

            Grid.SetColumn(idText, 0);
            Grid.SetColumn(versionText, 1);
            Grid.SetColumn(updateText, 2);
            Grid.SetColumn(updateButton, 3);
            Grid.SetColumn(removeButton, 4);
            grid.Children.Add(idText);
            grid.Children.Add(versionText);
            grid.Children.Add(updateText);
            grid.Children.Add(updateButton);
            grid.Children.Add(removeButton);
            return grid;
        });
    }

    static DataTemplate BuildSearchRowTemplate()
    {
        return new DataTemplate(() =>
        {
            var panel = new StackPanel { Spacing = 2, Padding = new Thickness(0, 4, 0, 4) };
            var title = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            title.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(nameof(NuGetSearchResult.Id)) });

            var details = new TextBlock { TextWrapping = TextWrapping.Wrap };
            details.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Converter = new SearchResultDetailsConverter() });
            panel.Children.Add(title);
            panel.Children.Add(details);
            return panel;
        });
    }

    sealed class PackageManagerDialogModel : INotifyPropertyChanged
    {
        readonly IProject project;
        readonly string projectFileName;
        readonly XamlRoot? xamlRoot;
        readonly NuGetPackageSearchService searchService = new();
        readonly NuGetPackageUpdateService updateService = new();
        readonly NuGetPackageDependencyPreviewService dependencyPreviewService = new();
        readonly NuGetProjectPackageOperationService operationService = new();

        string status = string.Empty;
        string dependencyPreview = string.Empty;

        public PackageManagerDialogModel(IProject project, XamlRoot? xamlRoot)
        {
            this.project = project ?? throw new ArgumentNullException(nameof(project));
            this.xamlRoot = xamlRoot;
            projectFileName = project.FileName.ToString();
        }

        /// <summary>
        /// Explicit license-acceptance gate shown before an install/update proceeds for a package
        /// whose NuGet metadata declares <c>requireLicenseAcceptance=true</c> (see
        /// doc/technotes/package-management.md). Unlike the AddIn gallery's license flow in
        /// AddInManagerDialog.xaml.cs (whole-batch, pre-computed before a synchronous engine
        /// event), this surface installs one package per user click, so the dialog can simply be
        /// awaited directly in the async click handler - no pre-computation split is needed here.
        /// </summary>
        async Task<bool> ConfirmLicenseIfRequiredAsync(string packageId, string version, bool requireLicenseAcceptance, string? licenseUrl)
        {
            if (!requireLicenseAcceptance)
                return true;

            var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
            message.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = $"{packageId} {version} requires you to accept its license terms before installing."
            });
            if (!string.IsNullOrWhiteSpace(licenseUrl))
            {
                message.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                var link = new Microsoft.UI.Xaml.Documents.Hyperlink { NavigateUri = new Uri(licenseUrl) };
                link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = licenseUrl });
                message.Inlines.Add(link);
            }

            var dialog = new ContentDialog
            {
                Title = "License Acceptance Required",
                Content = message,
                PrimaryButtonText = "Accept",
                CloseButtonText = "Decline",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<InstalledPackageRow> InstalledPackages { get; } = new();
        public ObservableCollection<NuGetSearchResult> SearchResults { get; } = new();

        public string Status
        {
            get => status;
            private set
            {
                if (status == value)
                    return;

                status = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Status)));
            }
        }

        public string DependencyPreview
        {
            get => dependencyPreview;
            private set
            {
                if (dependencyPreview == value)
                    return;

                dependencyPreview = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DependencyPreview)));
            }
        }

        /// <summary>
        /// Native, reduced-scope Package Manager Console equivalent (see
        /// <see cref="PackageConsoleCommandProcessor"/> and doc/technotes/package-management.md);
        /// runs a single scripted install/update/uninstall/list command through the exact same
        /// shared services the Installed/Search tabs use, including conflict resolution and the
        /// same license-acceptance gate.
        /// </summary>
        public async Task<string> RunConsoleCommandAsync(string commandLine, CancellationToken cancellationToken)
        {
            try
            {
                var processor = new PackageConsoleCommandProcessor(
                    projectFileName,
                    LoadSources(),
                    GetTargetFramework(),
                    (id, version, requireLicense, licenseUrl) => ConfirmLicenseIfRequiredAsync(id, version, requireLicense, licenseUrl));
                var output = await processor.ExecuteAsync(commandLine, cancellationToken);
                await LoadInstalledAsync(cancellationToken);
                return output;
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"Package console command '{commandLine}' failed for {project.FileName}: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        public async Task LoadInstalledAsync(CancellationToken cancellationToken)
        {
            try
            {
                var packages = new SdkStylePackageReferenceEditor(projectFileName).GetPackageReferences();
                InstalledPackages.Clear();
                foreach (var package in packages)
                {
                    InstalledPackages.Add(new InstalledPackageRow(package.Id, package.Version));
                }

                Status = InstalledPackages.Count == 0
                    ? "No installed packages."
                    : $"{InstalledPackages.Count} installed package(s).";
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"Failed to list installed packages for {project.FileName}: {ex.Message}");
                Status = $"Failed to load packages: {ex.Message}";
            }

            await Task.CompletedTask;
        }

        public async Task SearchAsync(string searchTerm, bool includePrerelease, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                Status = "Enter a package search term.";
                return;
            }

            try
            {
                Status = "Searching packages...";
                var sources = LoadSources();
                var results = await searchService.SearchAsync(sources, searchTerm, includePrerelease, take: 30, cancellationToken);
                SearchResults.Clear();
                DependencyPreview = string.Empty;
                foreach (var result in results)
                {
                    SearchResults.Add(result);
                }

                Status = results.Count == 0
                    ? "No packages found."
                    : $"{results.Count} package(s) found.";
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"NuGet package search failed for {project.FileName}: {ex.Message}");
                Status = $"Search failed: {ex.Message}";
            }
        }

        public async Task InstallAsync(NuGetSearchResult result, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(result.Version, out var version))
            {
                Status = $"Cannot install {result.Id}: invalid version '{result.Version}'.";
                return;
            }

            try
            {
                if (!await ConfirmLicenseIfRequiredAsync(result.Id, result.Version, result.RequireLicenseAcceptance, result.LicenseUrl))
                {
                    Status = $"Install of {result.Id} {result.Version} cancelled: license not accepted.";
                    return;
                }

                Status = $"Installing {result.Id} {result.Version}...";
                var operation = await operationService.AddPackageReferenceAsync(projectFileName, result.Id, version, restore: true, cancellationToken);
                await LoadInstalledAsync(cancellationToken);
                Status = FormatOperationStatus("Installed", result.Id, operation, result.RequireLicenseAcceptance);
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"NuGet package install failed for {project.FileName}: {ex.Message}");
                Status = $"Install failed: {ex.Message}";
            }
        }

        public async Task LoadDependencyPreviewAsync(NuGetSearchResult result, CancellationToken cancellationToken)
        {
            if (!NuGetVersion.TryParse(result.Version, out var version))
            {
                DependencyPreview = "Dependencies unavailable: invalid package version.";
                return;
            }

            try
            {
                DependencyPreview = "Loading dependencies...";
                var preview = await dependencyPreviewService.GetDependencyPreviewAsync(
                    LoadSources(),
                    result.Id,
                    version,
                    GetTargetFramework(),
                    cancellationToken);
                DependencyPreview = FormatDependencyPreview(preview);
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"NuGet dependency preview failed for {result.Id} {result.Version}: {ex.Message}");
                DependencyPreview = $"Dependencies unavailable: {ex.Message}";
            }
        }

        public async Task CheckUpdatesAsync(bool includePrerelease, CancellationToken cancellationToken)
        {
            try
            {
                Status = "Checking package updates...";
                var installed = InstalledPackages
                    .Select(package => new SdkStylePackageReference(package.Id, package.Version))
                    .ToArray();
                var updates = await updateService.GetUpdatesAsync(LoadSources(), installed, includePrerelease, cancellationToken);
                foreach (var package in InstalledPackages)
                {
                    var update = updates.FirstOrDefault(candidate => string.Equals(candidate.Id, package.Id, StringComparison.OrdinalIgnoreCase));
                    package.SetLatestVersion(update?.LatestVersion, update?.RequireLicenseAcceptance ?? false, update?.LicenseUrl);
                }

                Status = updates.Count == 0
                    ? "All installed packages are up to date."
                    : $"{updates.Count} package update(s) available.";
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"NuGet package update check failed for {project.FileName}: {ex.Message}");
                Status = $"Update check failed: {ex.Message}";
            }
        }

        public async Task UpdateAsync(InstalledPackageRow row, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(row.LatestVersion) || !NuGetVersion.TryParse(row.LatestVersion, out var version))
            {
                Status = $"Cannot update {row.Id}: no valid update version is available.";
                return;
            }

            try
            {
                if (!await ConfirmLicenseIfRequiredAsync(row.Id, row.LatestVersion, row.RequireLicenseAcceptance, row.LicenseUrl))
                {
                    Status = $"Update of {row.Id} cancelled: license not accepted.";
                    return;
                }

                Status = $"Updating {row.Id} to {row.LatestVersion}...";
                var operation = await operationService.AddPackageReferenceAsync(projectFileName, row.Id, version, restore: true, cancellationToken);
                await LoadInstalledAsync(cancellationToken);
                Status = FormatOperationStatus("Updated", row.Id, operation, row.RequireLicenseAcceptance);
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"NuGet package update failed for {project.FileName}: {ex.Message}");
                Status = $"Update failed: {ex.Message}";
            }
        }

        public async Task UninstallAsync(InstalledPackageRow row, CancellationToken cancellationToken)
        {
            try
            {
                Status = $"Uninstalling {row.Id}...";
                var operation = await operationService.RemovePackageReferenceAsync(projectFileName, row.Id, restore: true, cancellationToken);
                await LoadInstalledAsync(cancellationToken);
                Status = FormatOperationStatus("Uninstalled", row.Id, operation);
            }
            catch (Exception ex)
            {
                LoggingService.Warn($"NuGet package uninstall failed for {project.FileName}: {ex.Message}");
                Status = $"Uninstall failed: {ex.Message}";
            }
        }

        IReadOnlyList<PackageSource> LoadSources()
        {
            var projectDirectory = Path.GetDirectoryName(projectFileName) ?? AppContext.BaseDirectory;
            return NuGetPackageSourceCatalog.LoadEnabledSources(projectDirectory);
        }

        NuGetFramework GetTargetFramework()
        {
            var msbuildProject = project as MSBuildBasedProject;
            var targetFrameworkMoniker = msbuildProject?.GetEvaluatedProperty("TargetFramework");
            return string.IsNullOrWhiteSpace(targetFrameworkMoniker)
                ? NuGetFramework.AnyFramework
                : NuGetFramework.Parse(targetFrameworkMoniker);
        }

        static string FormatOperationStatus(
            string verb,
            string packageId,
            NuGetProjectPackageOperationResult operation,
            bool licenseAcceptedByUser = false)
        {
            if (!operation.Changed)
                return $"{packageId} is already up to date.";

            if (!operation.RestoreSucceeded)
                return $"{verb} {packageId}, but restore failed with exit code {operation.RestoreExitCode}.";

            var status = operation.RestoreRequested
                ? $"{verb} {packageId} and restored project."
                : $"{verb} {packageId}.";
            return licenseAcceptedByUser
                ? status + " Package metadata requires license acceptance."
                : status;
        }

        static string FormatDependencyPreview(NuGetPackageDependencyPreview preview)
        {
            if (!preview.HasDependencies)
                return "Dependencies: none for the selected target framework.";

            var groups = preview.DependencyGroups
                .Where(group => group.Dependencies.Count > 0)
                .Take(3)
                .Select(group =>
                    group.TargetFramework + ": " +
                    string.Join(", ", group.Dependencies.Take(8).Select(dependency =>
                        string.IsNullOrWhiteSpace(dependency.VersionRange)
                            ? dependency.Id
                            : dependency.Id + " " + dependency.VersionRange)));

            return "Dependencies: " + string.Join(" | ", groups);
        }
    }

    sealed class SearchResultDetailsConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not NuGetSearchResult result)
                return string.Empty;

            var description = string.IsNullOrWhiteSpace(result.Description) ? "No description." : result.Description;
            var license = result.RequireLicenseAcceptance
                ? " Requires license acceptance."
                : string.Empty;
            return $"{result.Version} - {result.SourceName} - {description}{license}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    sealed class InstalledPackageRow : INotifyPropertyChanged
    {
        string latestVersion = string.Empty;

        public InstalledPackageRow(string id, string version)
        {
            Id = id;
            Version = version;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; }
        public string Version { get; }
        public string LatestVersion => latestVersion;
        public bool HasUpdate => !string.IsNullOrWhiteSpace(latestVersion);
        public string UpdateLabel => HasUpdate ? "Latest " + latestVersion : string.Empty;
        public bool RequireLicenseAcceptance { get; private set; }
        public string? LicenseUrl { get; private set; }

        public void SetLatestVersion(string? version, bool requireLicenseAcceptance = false, string? licenseUrl = null)
        {
            latestVersion = version ?? string.Empty;
            RequireLicenseAcceptance = requireLicenseAcceptance;
            LicenseUrl = licenseUrl;
            OnPropertyChanged(nameof(LatestVersion));
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(UpdateLabel));
        }

        void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
