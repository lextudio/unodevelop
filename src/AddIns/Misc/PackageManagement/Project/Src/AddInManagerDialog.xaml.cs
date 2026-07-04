using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace UnoDevelop.AddIns;

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

    public AddInManagerDialog()
    {
        InitializeComponent();
        _builtInItems = new List<AddInManagerViewModel>();
        _externalItems = new List<AddInManagerViewModel>();
        _extensions = new List<InstalledExtension>();
        ReloadList();
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
