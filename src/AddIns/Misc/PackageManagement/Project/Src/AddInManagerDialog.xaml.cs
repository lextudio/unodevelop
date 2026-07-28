using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using GalleryServices = ICSharpCode.AddInManager2.AddInManagerServices;
using NuGet;

namespace UnoDevelop.AddIns;

/// <summary>
/// Row shown in the "Online Gallery" tab, wrapping a legacy NuGet.Core <see cref="IPackage"/>
/// from the shared AddInManager2 engine (see doc/technotes/addin-manager2.md). Read-only snapshot
/// computed once per page load - it does not track live INotifyPropertyChanged updates the way
/// OpenDevelop's WPF NuGetPackageViewModel does, since this dialog re-queries and rebuilds the
/// gallery list wholesale after every install/update instead.
/// </summary>
internal sealed class GalleryPackageViewModel
{
    public GalleryPackageViewModel(IPackage package, AddIn? installedAddIn)
    {
        Package = package;
        InstalledAddIn = installedAddIn;
    }

    public IPackage Package { get; }
    public AddIn? InstalledAddIn { get; }

    public string Id => Package.Id;
    public string VersionLabel => Package.Version?.ToString() ?? "";
    public string Summary => Package.Summary ?? Package.Description ?? "";

    public bool IsInstalled =>
        InstalledAddIn != null && GalleryServices.Setup.IsAddInInstalled(InstalledAddIn);

    public bool IsUpdate =>
        InstalledAddIn != null
        && GalleryServices.Setup.IsAddInInstalled(InstalledAddIn)
        && GalleryServices.Setup.CompareAddInToPackageVersion(InstalledAddIn, Package) < 0;

    public string StatusLabel => IsUpdate ? "update available" : (IsInstalled ? "installed" : "");
    public string ActionLabel => IsUpdate ? "Update" : (IsInstalled ? "Installed" : "Install");
    public bool CanInstallOrUpdate => !IsInstalled || IsUpdate;
}

internal sealed class AddInManagerViewModel : INotifyPropertyChanged
{
    private readonly AddIn _addIn;

    public AddInManagerViewModel(AddIn addIn)
    {
        _addIn = addIn;
    }

    public AddIn AddIn => _addIn;
    public string DisplayName => _addIn.Name;
    public string Version => _addIn.Version?.ToString() ?? "";
    public string Author => _addIn.Properties["author"] ?? "";

    public string Status => _addIn.Enabled ? "Enabled" : "Disabled";
    public string ToggleLabel => _addIn.Enabled ? "Disable" : "Enable";

    public bool IsExternalOrUser =>
        !_addIn.IsPreinstalled;

    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleLabel)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class AddInManagerDialog : ContentDialog
{
    private readonly List<AddInManagerViewModel> _builtInItems;
    private readonly List<AddInManagerViewModel> _externalItems;
    private readonly List<InstalledExtension> _extensions;

    // Online-gallery state (see doc/technotes/addin-manager2.md): the shared AddInManager2 engine
    // (ICSharpCode.AddInManager2.Model, moved into Base) is consumed directly, no MVVM layer.
    private const int GalleryPageSize = 10;
    private int _galleryPageIndex;
    private int _galleryTotalCount;
    private List<GalleryPackageViewModel> _galleryAllMatches = new();
    private readonly HashSet<string> _preAcceptedLicensePackageIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _galleryEventsHooked;

    public AddInManagerDialog()
    {
        InitializeComponent();
        _builtInItems = new List<AddInManagerViewModel>();
        _externalItems = new List<AddInManagerViewModel>();
        _extensions = new List<InstalledExtension>();
        ReloadList();
        HookGalleryEvents();
        ReloadGalleryPage(resetToFirstPage: true);
    }

    private void HookGalleryEvents()
    {
        if (_galleryEventsHooked)
            return;
        _galleryEventsHooked = true;

        // The shared engine's AcceptLicenses event fires synchronously, from inside a plain
        // (non-async) call stack, so it cannot itself await a ContentDialog on the UI thread.
        // Instead, OnGalleryInstall pre-computes which packages need license acceptance and
        // shows the ContentDialog *before* calling into the engine; this handler then just
        // honors that already-collected decision. This is the closest reasonable equivalent to
        // OpenDevelop's synchronous WPF LicenseAcceptanceView.ShowDialog() - functionally
        // equivalent (user must accept before install proceeds) but not byte-identical timing.
        GalleryServices.Events.AcceptLicenses += (s, e) =>
        {
            e.IsAccepted = e.Packages.All(p => _preAcceptedLicensePackageIds.Contains(p.Id));
        };
    }

    private void ReloadGalleryPage(bool resetToFirstPage)
    {
        if (resetToFirstPage)
            _galleryPageIndex = 0;

        string? searchTerm = _gallerySearchBox.Text;
        bool updatesOnly = _galleryUpdatesOnlyCheckBox.IsChecked == true;

        IQueryable<IPackage> packages = GalleryServices.Repositories.AllRegistered.GetPackages();
        if (!string.IsNullOrWhiteSpace(searchTerm))
            packages = packages.Find(searchTerm);

        // Group by Id and keep only the highest version per package (equivalent to OpenDevelop's
        // NuGetAddInsViewModelBase.GetFilteredPackagesBeforePagingResults DistinctLast-by-Id, which
        // relies on an AddInManager2-internal extension method not available from this project).
        var ordered = packages
            .Where(p => p.IsReleaseVersion())
            .AsEnumerable()
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.Version).First())
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _galleryAllMatches = ordered
            .Select(p => new GalleryPackageViewModel(p, GalleryServices.Setup.GetAddInForNuGetPackage(p)))
            .Where(vm => !updatesOnly || vm.IsUpdate)
            .ToList();

        _galleryTotalCount = _galleryAllMatches.Count;

        int maxPageIndex = Math.Max(0, (_galleryTotalCount - 1) / GalleryPageSize);
        if (_galleryPageIndex > maxPageIndex)
            _galleryPageIndex = maxPageIndex;

        var pageItems = _galleryAllMatches
            .Skip(_galleryPageIndex * GalleryPageSize)
            .Take(GalleryPageSize)
            .ToList();

        _galleryList.ItemsSource = null;
        _galleryList.ItemsSource = pageItems;

        int totalPages = _galleryTotalCount == 0 ? 1 : (_galleryTotalCount + GalleryPageSize - 1) / GalleryPageSize;
        _galleryPageLabel.Text = $"Page {_galleryPageIndex + 1} of {totalPages} ({_galleryTotalCount} package(s))";
    }

    private void OnGallerySearch(object sender, RoutedEventArgs e) => ReloadGalleryPage(resetToFirstPage: true);

    private void OnGalleryPreviousPage(object sender, RoutedEventArgs e)
    {
        if (_galleryPageIndex > 0)
        {
            _galleryPageIndex--;
            ReloadGalleryPage(resetToFirstPage: false);
        }
    }

    private void OnGalleryNextPage(object sender, RoutedEventArgs e)
    {
        int maxPageIndex = Math.Max(0, (_galleryTotalCount - 1) / GalleryPageSize);
        if (_galleryPageIndex < maxPageIndex)
        {
            _galleryPageIndex++;
            ReloadGalleryPage(resetToFirstPage: false);
        }
    }

    private async void OnGalleryInstall(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GalleryPackageViewModel vm })
            return;

        GalleryServices.Events.OnOperationStarted();
        _statusText.Text = "";

        try
        {
            var package = vm.Package;

            if (GalleryServices.NuGet.Packages.LocalRepository.Exists(package))
            {
                // Already downloaded but not registered as an AddIn (e.g. leftover from a
                // previous failed install) - same fallback path as OpenDevelop's
                // NuGetPackageViewModel.TryInstallingPackage().
                GalleryServices.Setup.InstallAddIn(package, GalleryServices.NuGet.GetLocalPackageDirectory(package));
                ReloadGalleryPage(resetToFirstPage: false);
                _statusText.Text = $"Installed: {package.Id}";
                return;
            }

            var resolver = GalleryServices.NuGet.CreateInstallPackageOperationResolver(allowPrereleaseVersions: false);
            var operations = resolver.ResolveOperations(package).ToList();

            var packagesToInstall = operations
                .Where(op => op.Action == PackageAction.Install)
                .Select(op => op.Package)
                .ToList();

            var packagesNeedingLicense = packagesToInstall
                .Where(p => p.RequireLicenseAcceptance && !GalleryServices.NuGet.Packages.LocalRepository.Exists(p))
                .ToList();

            _preAcceptedLicensePackageIds.Clear();
            if (packagesNeedingLicense.Count > 0)
            {
                bool accepted = await ShowLicenseAcceptanceDialogAsync(packagesNeedingLicense);
                if (!accepted)
                {
                    _statusText.Text = "Install cancelled: license not accepted.";
                    return;
                }

                foreach (var p in packagesNeedingLicense)
                    _preAcceptedLicensePackageIds.Add(p.Id);
            }

            foreach (var operation in operations)
                GalleryServices.NuGet.ExecuteOperation(operation);

            ReloadGalleryPage(resetToFirstPage: false);
            ReloadList();
            _statusText.Text = $"{(vm.IsUpdate ? "Updated" : "Installed")}: {package.Id}";
        }
        catch (Exception ex)
        {
            GalleryServices.Events.OnAddInOperationError(new ICSharpCode.AddInManager2.Model.AddInOperationErrorEventArgs(ex));
            _statusText.Text = "Install failed: " + ex.Message;
        }
        finally
        {
            _preAcceptedLicensePackageIds.Clear();
        }
    }

    private async Task<bool> ShowLicenseAcceptanceDialogAsync(IEnumerable<IPackage> packages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("The following package(s) require you to accept their license before installing:");
        sb.AppendLine();
        foreach (var p in packages)
        {
            sb.Append("• ").Append(p.Id).Append(' ').Append(p.Version);
            if (p.LicenseUrl != null)
                sb.Append(" — ").Append(p.LicenseUrl);
            sb.AppendLine();
        }

        var dialog = new ContentDialog
        {
            Title = "License Acceptance",
            Content = sb.ToString(),
            PrimaryButtonText = "I Accept",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void ReloadList()
    {
        var addInTree = ServiceSingleton.GetRequiredService<IAddInTree>();
        _builtInItems.Clear();
        _externalItems.Clear();
        foreach (var addIn in addInTree.AddIns.OrderBy(a => a.Name))
        {
            if (!string.Equals(addIn.Properties["addInManagerHidden"], "true", StringComparison.OrdinalIgnoreCase))
            {
                var vm = new AddInManagerViewModel(addIn);
                if (vm.IsExternalOrUser)
                    _externalItems.Add(vm);
                else
                    _builtInItems.Add(vm);
            }
        }

        _builtInAddInList.ItemsSource = null;
        _builtInAddInList.ItemsSource = _builtInItems;

        _externalAddInList.ItemsSource = null;
        _externalAddInList.ItemsSource = _externalItems;

        _extensions.Clear();
        _extensions.AddRange(ExtensionRegistry.GetAll().OrderBy(e => e.DisplayName));

        _extensionList.ItemsSource = null;
        _extensionList.ItemsSource = _extensions;
    }

    private void OnToggleAddIn(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AddInManagerViewModel vm })
        {
            var addIn = vm.AddIn;
            if (addIn.Enabled)
                AddInManager.Disable(new[] { addIn });
            else
                AddInManager.Enable(new[] { addIn });

            vm.Refresh();
        }
    }

    private async void OnInstallFromFile(object sender, RoutedEventArgs e)
    {
        _statusText.Text = "";
        _installButton.IsEnabled = false;

        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.Downloads
            };
            picker.FileTypeFilter.Add(".addin");
            picker.FileTypeFilter.Add(".sdaddin");
            picker.FileTypeFilter.Add(".vsix");
            picker.FileTypeFilter.Add(".zip");

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var filePath = file.Path;
            if (string.IsNullOrEmpty(filePath))
                return;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".addin":
                    InstallAddInFile(filePath);
                    break;

                case ".sdaddin":
                case ".zip":
                    InstallAddInArchive(filePath);
                    break;

                case ".vsix":
                    InstallVsixExtension(filePath);
                    break;

                default:
                    _statusText.Text = "Unsupported file format: " + ext;
                    return;
            }

            ReloadList();
            _statusText.Text = "Installed successfully.";
        }
        catch (Exception ex)
        {
            _statusText.Text = "Install failed: " + ex.Message;
        }
        finally
        {
            _installButton.IsEnabled = true;
        }
    }

    private void InstallAddInFile(string filePath)
    {
        var addInTree = ServiceSingleton.GetRequiredService<IAddInTree>();
        var addIn = AddIn.Load(addInTree, filePath);

        if (addIn?.Manifest?.PrimaryIdentity is null)
        {
            _statusText.Text = "The .addin file has no identity and cannot be installed.";
            return;
        }

        AddInManager.AddExternalAddIns(new[] { addIn });
    }

    private void InstallAddInArchive(string filePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "UnoDevelop_AddInInstall_" + Guid.NewGuid().ToString("N"));

        try
        {
            ZipFile.ExtractToDirectory(filePath, tempDir);
            var addinFiles = Directory.GetFiles(tempDir, "*.addin", SearchOption.AllDirectories);

            if (addinFiles.Length == 0)
            {
                _statusText.Text = "No .addin manifest found in the archive.";
                return;
            }

            var addInTree = ServiceSingleton.GetRequiredService<IAddInTree>();
            var installed = new List<AddIn>();

            foreach (var addinFile in addinFiles)
            {
                var addIn = AddIn.Load(addInTree, addinFile);
                if (addIn?.Manifest?.PrimaryIdentity is not null)
                    installed.Add(addIn);
            }

            if (installed.Count == 0)
            {
                _statusText.Text = "No valid addins found in the archive.";
                return;
            }

            AddInManager.AddExternalAddIns(installed);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private void InstallVsixExtension(string filePath)
    {
        var info = VsixPackageParser.Parse(filePath);

        var installed = ExtensionInstaller.Install(filePath);

        var result = $"Installed '{installed.DisplayName}' ({installed.KindLabel})";

        if (installed.GrammarCount > 0)
            result += $" — {installed.GrammarCount} grammar(s)";

        if (installed.ThemeCount > 0)
            result += $" — {installed.ThemeCount} theme(s)";

        if (installed.ServerCount > 0)
            result += $" — {installed.ServerCount} server file(s)";

        _statusText.Text = result;
    }

    private async void OnRemoveAddIn(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AddInManagerViewModel vm })
        {
            var addIn = vm.AddIn;
            if (addIn.IsPreinstalled)
            {
                _statusText.Text = "Preinstalled addins cannot be removed.";
                return;
            }

            AddInManager.RemoveExternalAddIns(new[] { addIn });
            ReloadList();
            _statusText.Text = "Removed: " + addIn.Name;
        }
    }

    private void OnUninstallExtension(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InstalledExtension ext })
        {
            ExtensionInstaller.Uninstall(ext.Id);
            ReloadList();
            _statusText.Text = "Uninstalled: " + ext.DisplayName;
        }
    }
}
