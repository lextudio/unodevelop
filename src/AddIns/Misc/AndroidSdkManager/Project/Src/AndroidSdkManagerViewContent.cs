using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ICSharpCode.AndroidSdkManager;

/// <summary>
/// Native Microsoft.UI.Xaml tool view for the Android SDK Manager - a document tab, not a
/// floating WPF window like OpenDevelop's AndroidSdkManagerWindow. Contract-first v1 scope: a
/// flat package list (installed/available/has-update) with install/uninstall by selection.
/// OpenDevelop's hierarchical tree grouping (SdkPackageTreeBuilder/SdkTreeNodes) is deliberately
/// not ported in this pass - a flat sortable list covers the same information.
/// </summary>
public sealed class AndroidSdkManagerViewContent : IViewContent
{
    readonly AndroidSdkManagerService _service = new();
    readonly ObservableCollection<SdkPackage> _packages = new();
    readonly TextBox _sdkPathBox;
    readonly TextBlock _status;
    readonly ListView _list;
    readonly Grid _root;

    public AndroidSdkManagerViewContent()
    {
        _sdkPathBox = new TextBox
        {
            PlaceholderText = "Android SDK root path",
            Text = AndroidSdkManagerService.GetSavedSdkPath(),
            Margin = new Thickness(8, 8, 8, 4)
        };
        _sdkPathBox.LostFocus += (_, _) => AndroidSdkManagerService.SaveSdkPath(_sdkPathBox.Text);

        _status = new TextBlock { Margin = new Thickness(8, 0, 8, 4), Text = "Not refreshed yet" };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 8, 4) };
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += async (_, _) => await RefreshAsync();
        var install = new Button { Content = "Install" };
        install.Click += async (_, _) => await InstallSelectedAsync();
        var uninstall = new Button { Content = "Uninstall" };
        uninstall.Click += async (_, _) => await UninstallSelectedAsync();
        toolbar.Children.Add(refresh);
        toolbar.Children.Add(install);
        toolbar.Children.Add(uninstall);

        _list = new ListView { ItemsSource = _packages, SelectionMode = ListViewSelectionMode.Multiple };
        _list.ItemTemplate = new DataTemplate(() =>
        {
            var grid = new Grid { Padding = new Thickness(4), ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var id = new TextBlock();
            id.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Id") });
            var version = new TextBlock();
            version.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("VersionText") });
            var status = new TextBlock();
            status.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("StatusText") });
            Grid.SetColumn(id, 0);
            Grid.SetColumn(version, 1);
            Grid.SetColumn(status, 2);
            grid.Children.Add(id);
            grid.Children.Add(version);
            grid.Children.Add(status);
            return grid;
        });

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_sdkPathBox, 0);
        Grid.SetRow(toolbar, 1);
        Grid.SetRow(_status, 2);
        Grid.SetRow(_list, 3);
        _root.Children.Add(_sdkPathBox);
        _root.Children.Add(toolbar);
        _root.Children.Add(_status);
        _root.Children.Add(_list);
    }

    /// <summary>Read-only snapshot for DevFlow/integration-test inspection.</summary>
    public IReadOnlyList<SdkPackage> GetPackagesForTesting() => _packages.ToList();

    public async Task RefreshAsync()
    {
        var sdkRoot = _sdkPathBox.Text;
        if (string.IsNullOrWhiteSpace(sdkRoot))
        {
            _status.Text = "Set the Android SDK root path first.";
            return;
        }

        try
        {
            var packages = await _service.ListPackagesAsync(sdkRoot);
            _packages.Clear();
            foreach (var package in packages)
                _packages.Add(package);
            _status.Text = $"{_packages.Count} package(s)";
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
    }

    async Task InstallSelectedAsync()
    {
        var ids = _list.SelectedItems.OfType<SdkPackage>().Select(p => p.Id).ToList();
        if (ids.Count == 0)
            return;
        try
        {
            var ok = await _service.InstallAsync(_sdkPathBox.Text, ids);
            _status.Text = ok ? $"Installed {ids.Count} package(s)" : "Install failed";
            if (ok)
                await RefreshAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
    }

    async Task UninstallSelectedAsync()
    {
        var ids = _list.SelectedItems.OfType<SdkPackage>().Select(p => p.Id).ToList();
        if (ids.Count == 0)
            return;
        try
        {
            var ok = await _service.UninstallAsync(_sdkPathBox.Text, ids);
            _status.Text = ok ? $"Uninstalled {ids.Count} package(s)" : "Uninstall failed";
            if (ok)
                await RefreshAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
    }

    public object Control => _root;
    public object InitiallyFocusedControl => _sdkPathBox;
    public IWorkbenchWindow? WorkbenchWindow { get; set; }
    public string TabPageText => "Android SDK Manager";
    public string TitleName => TabPageText;
    public string InfoTip => "Android SDK Manager";

    public event EventHandler? TabPageTextChanged;
    public event EventHandler? TitleNameChanged;
    public event EventHandler? InfoTipChanged;
    public event EventHandler? Disposed;

    public IList<OpenedFile> Files { get; } = new List<OpenedFile>();
    public OpenedFile? PrimaryFile => null;
    public FileName? PrimaryFileName => null;

    public bool IsDisposed { get; private set; }
    public bool IsDirty => false;
    public bool IsReadOnly => true;
    public bool IsViewOnly => true;
    public bool CloseWithSolution => false;

    public event EventHandler? IsDirtyChanged { add { } remove { } }

    public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();

    public INavigationPoint BuildNavPoint() => new DummyNavigationPoint();

    public void Save(OpenedFile file, System.IO.Stream stream) { }
    public void Load(OpenedFile file, System.IO.Stream stream) { }

    public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => true;
    public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => true;
    public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
    public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }

    public object? GetService(Type serviceType) => null;

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    sealed class DummyNavigationPoint : INavigationPoint
    {
        public string Description => "Android SDK Manager";
        public string ShortDescription => "Android SDK Manager";
        public string FullDescription => Description;
        public string ToolTip => Description;
        public string FileName => string.Empty;
        public object? NavigationData => null;
        public int Ordinal => 0;
        public int Index => 0;
        public void JumpTo() { }
        public void FileNameChanged(string newName) { }
        public void ContentChanging(object? sender, EventArgs e) { }
        public int CompareTo(object? obj) => 0;
        public event EventHandler? DescriptionChanged { add { } remove { } }
    }
}
