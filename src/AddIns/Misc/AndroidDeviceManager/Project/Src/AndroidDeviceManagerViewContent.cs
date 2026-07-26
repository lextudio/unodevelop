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

namespace ICSharpCode.AndroidDeviceManager;

/// <summary>
/// Native Microsoft.UI.Xaml tool view for the Android Device Manager - a document tab (not a
/// floating WPF window like OpenDevelop's AndroidDeviceManagerWindow), following the same
/// "new native UI, shared portable engine" pattern used for XamlDesigner/ResourceEditor.
/// Contract-first v1 scope: list/refresh/create/delete/launch AVDs. The hw.*-property AVD
/// editor (OpenDevelop's AvdEditorWindow) is deliberately not ported in this pass.
/// </summary>
public sealed class AndroidDeviceManagerViewContent : IViewContent
{
    readonly AvdManagerService _service = new();
    readonly ObservableCollection<AvdInfo> _avds = new();
    readonly TextBox _sdkPathBox;
    readonly TextBlock _status;
    readonly ListView _list;
    readonly Grid _root;

    public AndroidDeviceManagerViewContent()
    {
        _sdkPathBox = new TextBox
        {
            PlaceholderText = "Android SDK root path",
            Text = AvdManagerService.GetSavedSdkPath(),
            Margin = new Thickness(8, 8, 8, 4)
        };
        _sdkPathBox.LostFocus += (_, _) => PropertyService.Set("AndroidSdkManager.SdkPath", _sdkPathBox.Text);

        _status = new TextBlock { Margin = new Thickness(8, 0, 8, 4), Text = "Not refreshed yet" };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 8, 4) };
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += async (_, _) => await RefreshAsync();
        var launch = new Button { Content = "Launch" };
        launch.Click += (_, _) => LaunchSelected();
        var delete = new Button { Content = "Delete" };
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        toolbar.Children.Add(refresh);
        toolbar.Children.Add(launch);
        toolbar.Children.Add(delete);

        _list = new ListView { ItemsSource = _avds, SelectionMode = ListViewSelectionMode.Single };
        _list.ItemTemplate = new DataTemplate(() =>
        {
            var text = new TextBlock { Margin = new Thickness(4) };
            text.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding
            {
                Path = new PropertyPath("Name")
            });
            return text;
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
    public IReadOnlyList<AvdInfo> GetAvdsForTesting() => _avds.ToList();

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
            var avds = await _service.ListAvdsAsync(sdkRoot);
            _avds.Clear();
            foreach (var avd in avds)
                _avds.Add(avd);
            _status.Text = $"{_avds.Count} AVD(s)";
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
    }

    async void LaunchSelected()
    {
        if (_list.SelectedItem is not AvdInfo avd)
            return;
        try
        {
            _service.StartAvd(_sdkPathBox.Text, avd.Name);
            _status.Text = $"Launching {avd.Name}...";
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
        await Task.CompletedTask;
    }

    async Task DeleteSelectedAsync()
    {
        if (_list.SelectedItem is not AvdInfo avd)
            return;
        try
        {
            var ok = await _service.DeleteAvdAsync(_sdkPathBox.Text, avd.Name);
            if (ok)
            {
                _avds.Remove(avd);
                _status.Text = $"Deleted {avd.Name}";
            }
            else
            {
                _status.Text = $"Failed to delete {avd.Name}";
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
    }

    public object Control => _root;
    public object InitiallyFocusedControl => _sdkPathBox;
    public IWorkbenchWindow? WorkbenchWindow { get; set; }
    public string TabPageText => "Android Device Manager";
    public string TitleName => TabPageText;
    public string InfoTip => "Android Virtual Device Manager";

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
        public string Description => "Android Device Manager";
        public string ShortDescription => "Android Device Manager";
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
