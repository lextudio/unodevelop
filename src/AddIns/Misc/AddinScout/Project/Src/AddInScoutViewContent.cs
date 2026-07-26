using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.NuGet;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Windows.ApplicationModel.DataTransfer;

namespace UnoDevelop.AddIns.Misc.AddInScout;

public sealed class AddInScoutViewContent : IViewContent
{
    private readonly ObservableCollection<AddInScoutPathItem> _paths = new();
    private readonly ObservableCollection<AddInScoutCodonItem> _codons = new();
    private readonly ObservableCollection<AddInScoutAddInItem> _addIns = new();
    private readonly ObservableCollection<AddInScoutSearchResultItem> _searchResults = new();
    private readonly AddInPackageManagerService _packageManager = new();
    private readonly Grid _control;
    private readonly TextBlock _details;
    private ListView? _addInList;
    private TextBox? _searchBox;
    private TextBlock? _nuGetStatus;
    private ListView? _searchResultsList;

    public AddInScoutViewContent()
    {
        LoadModel();

        TabPageText = "AddIn Scout";
        TitleName = TabPageText;
        InfoTip = "Inspect loaded AddInTree paths and addins";

        var pathList = new ListView
        {
            ItemsSource = _paths,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreatePathTemplate(),
            Margin = new Thickness(8)
        };
        pathList.SelectionChanged += (_, _) => SelectPath(pathList.SelectedItem as AddInScoutPathItem);

        var addInList = new ListView
        {
            ItemsSource = _addIns,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateAddInTemplate(),
            Margin = new Thickness(8, 8, 8, 0)
        };
        addInList.SelectionChanged += (_, _) => SelectAddIn(addInList.SelectedItem as AddInScoutAddInItem);
        _addInList = addInList;

        var toggleEnabled = new Button { Content = "Enable/Disable selected", Margin = new Thickness(8) };
        toggleEnabled.Click += (_, _) =>
        {
            if (addInList.SelectedItem is AddInScoutAddInItem item)
                ToggleEnabledByName(item.Identity ?? item.Name);
        };
        var addInsPanel = new Grid();
        addInsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        addInsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(addInList, 0);
        Grid.SetRow(toggleEnabled, 1);
        addInsPanel.Children.Add(addInList);
        addInsPanel.Children.Add(toggleEnabled);

        var nuGetPanel = CreateNuGetPanel();

        var tabs = new TabView
        {
            TabWidthMode = TabViewWidthMode.SizeToContent,
            IsAddTabButtonVisible = false,
            Margin = new Thickness(0)
        };
        tabs.TabItems.Add(new TabViewItem { Header = "Tree", Content = pathList });
        tabs.TabItems.Add(new TabViewItem { Header = "AddIns", Content = addInsPanel });
        tabs.TabItems.Add(new TabViewItem { Header = "NuGet", Content = nuGetPanel });

        _details = new TextBlock
        {
            Text = "Select an AddInTree path or addin.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8)
        };

        var codonList = new ListView
        {
            ItemsSource = _codons,
            SelectionMode = ListViewSelectionMode.Extended,
            ItemTemplate = CreateCodonTemplate(),
            Margin = new Thickness(8)
        };
        codonList.DoubleTapped += (_, _) => CopyCodons(codonList);

        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "Copy" };
        copy.Click += (_, _) => CopyCodons(codonList);
        menu.Items.Add(copy);
        codonList.ContextFlyout = menu;

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_details, 0);
        Grid.SetRow(codonList, 1);
        right.Children.Add(_details);
        right.Children.Add(codonList);

        _control = new Grid();
        _control.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        _control.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(tabs, 0);
        Grid.SetColumn(right, 1);
        _control.Children.Add(tabs);
        _control.Children.Add(right);

        if (_paths.Count > 0)
        {
            pathList.SelectedIndex = 0;
        }
    }

    public object? Control => _control;
    public object? InitiallyFocusedControl => _control;
    public IWorkbenchWindow? WorkbenchWindow { get; set; }
    public event EventHandler? TabPageTextChanged;
    public string TabPageText { get; }
    public string TitleName { get; }
    public event EventHandler? TitleNameChanged;
    public string InfoTip { get; }
    public event EventHandler? InfoTipChanged;
    public IList<OpenedFile> Files => Array.Empty<OpenedFile>();
    public OpenedFile? PrimaryFile => null;
    public FileName? PrimaryFileName => null;
    public bool IsDisposed { get; private set; }
    public event EventHandler? Disposed;
    public bool IsReadOnly => true;
    public bool IsViewOnly => true;
    public bool CloseWithSolution => false;
    public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();
    public bool IsDirty => false;
    public event EventHandler? IsDirtyChanged;

    public void Save(OpenedFile file, System.IO.Stream stream)
    {
    }

    public void Load(OpenedFile file, System.IO.Stream stream)
    {
    }

    public INavigationPoint BuildNavPoint() => new AddInScoutNavigationPoint();
    public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;
    public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;
    public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
    public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }
    public object? GetService(Type serviceType) => null;

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Read-only snapshot for DevFlow/integration-test inspection.</summary>
    public IReadOnlyList<(string Name, string? Identity, bool Enabled, bool Preinstalled)> GetAddInsForTesting()
        => _addIns.Select(item => (item.Name, item.Identity, item.Enabled, item.Preinstalled)).ToList();

    /// <summary>
    /// Toggles an AddIn's enabled state by name or primary identity, persisting the change via
    /// the real upstream <see cref="AddInManager"/> (Enable/Disable + SaveAddInConfiguration -
    /// already linked into ICSharpCode.Core.csproj, previously unused by any UI in this repo).
    /// Preinstalled AddIns can only be disabled/re-enabled, never uninstalled - matches upstream
    /// AddInManager's documented semantics.
    /// </summary>
    public bool? ToggleEnabledByName(string nameOrIdentity)
    {
        var addInTree = ServiceSingleton.GetRequiredService<IAddInTree>();
        var addIn = addInTree.AddIns.FirstOrDefault(candidate =>
            string.Equals(candidate.Manifest?.PrimaryIdentity, nameOrIdentity, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Name, nameOrIdentity, StringComparison.OrdinalIgnoreCase));
        if (addIn is null)
            return null;

        // AddInManager.Enable/Disable deliberately only set addIn.Action and persist to the
        // config file for the NEXT restart (see their XML doc comments) - they never flip the
        // live addIn.Enabled bool, since real addin loading only happens at startup. Flip it
        // directly here too so the toggle is visible immediately in this running session (the
        // AddIn.Enabled setter already keeps Action in sync), then persist via the same
        // AddInManager.SaveAddInConfiguration used by Enable/Disable internally.
        addIn.Enabled = !addIn.Enabled;

        var disabled = addInTree.AddIns.Where(candidate => !candidate.Enabled)
            .Select(candidate => candidate.Manifest?.PrimaryIdentity)
            .Where(identity => !string.IsNullOrEmpty(identity))
            .Select(identity => identity!)
            .ToList();
        AddInManager.SaveAddInConfiguration(new List<string>(), disabled);

        var index = _addIns.ToList().FindIndex(item =>
            string.Equals(item.Identity, addIn.Manifest?.PrimaryIdentity, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, addIn.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            var old = _addIns[index];
            _addIns[index] = old with { Enabled = addIn.Enabled };
        }

        return addIn.Enabled;
    }

    /// <summary>
    /// The AddInManager2 "Available" tab equivalent: search configured NuGet feeds
    /// (<see cref="AddInPackageManagerService"/>, reusing the existing project-reference NuGet
    /// search infrastructure) and install/uninstall NuGet-packaged AddIns. This is the piece the
    /// contract-first slice explicitly deferred - real download+extract+register, not just
    /// enable/disable of already-loaded AddIns.
    /// </summary>
    private Grid CreateNuGetPanel()
    {
        var searchBox = new TextBox { PlaceholderText = "Search NuGet for AddIns...", Margin = new Thickness(8, 8, 8, 0) };
        _searchBox = searchBox;
        var searchButton = new Button { Content = "Search", Margin = new Thickness(8, 8, 8, 0) };
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(searchBox, 0);
        Grid.SetColumn(searchButton, 1);
        searchRow.Children.Add(searchBox);
        searchRow.Children.Add(searchButton);

        var resultsList = new ListView
        {
            ItemsSource = _searchResults,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateSearchResultTemplate(),
            Margin = new Thickness(8)
        };
        _searchResultsList = resultsList;

        var status = new TextBlock { Text = string.Empty, Margin = new Thickness(8, 0, 8, 8), TextWrapping = TextWrapping.Wrap };
        _nuGetStatus = status;

        var installButton = new Button { Content = "Install selected", Margin = new Thickness(8, 0, 8, 8) };
        var uninstallButton = new Button { Content = "Uninstall selected AddIn", Margin = new Thickness(8, 0, 8, 8) };
        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionsRow.Children.Add(installButton);
        actionsRow.Children.Add(uninstallButton);

        searchButton.Click += async (_, _) => await SearchNuGetAsync(searchBox.Text ?? string.Empty);
        installButton.Click += async (_, _) =>
        {
            if (resultsList.SelectedItem is AddInScoutSearchResultItem selected)
                await InstallFromNuGetAsync(selected.Id, selected.Version);
        };
        uninstallButton.Click += (_, _) =>
        {
            if (_addInList?.SelectedItem is AddInScoutAddInItem item)
                UninstallByName(item.Identity ?? item.Name);
        };

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(searchRow, 0);
        Grid.SetRow(resultsList, 1);
        Grid.SetRow(actionsRow, 2);
        Grid.SetRow(status, 3);
        panel.Children.Add(searchRow);
        panel.Children.Add(resultsList);
        panel.Children.Add(actionsRow);
        panel.Children.Add(status);
        return panel;
    }

    private static DataTemplate CreateSearchResultTemplate()
    {
        return new DataTemplate(() =>
        {
            var panel = new StackPanel { Padding = new Thickness(8, 5, 8, 5) };
            panel.Children.Add(CreateCell("Id", true));
            panel.Children.Add(CreateCell("Version", false));
            panel.Children.Add(CreateCell("Description", false));
            return panel;
        });
    }

    /// <summary>Runs a NuGet search for DevFlow/integration-test use, returning results directly.</summary>
    public async Task<IReadOnlyList<NuGetSearchResult>> SearchNuGetForTestingAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        var results = await _packageManager.SearchAsync(searchTerm, includePrerelease: false, take: 25, cancellationToken);
        _searchResults.Clear();
        foreach (var result in results)
            _searchResults.Add(new AddInScoutSearchResultItem(result.Id, result.Version, result.Description ?? string.Empty));
        return results;
    }

    private async Task SearchNuGetAsync(string searchTerm)
    {
        if (_nuGetStatus is not null)
            _nuGetStatus.Text = "Searching...";
        try
        {
            var results = await SearchNuGetForTestingAsync(searchTerm);
            if (_nuGetStatus is not null)
                _nuGetStatus.Text = $"{results.Count} result(s)";
        }
        catch (Exception ex)
        {
            if (_nuGetStatus is not null)
                _nuGetStatus.Text = $"Search failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Downloads, extracts, and registers a NuGet-packaged AddIn (see
    /// <see cref="AddInPackageManagerService.InstallAsync"/>), then refreshes the AddIns list so
    /// the newly installed AddIn shows up immediately.
    /// </summary>
    public async Task<AddInPackageInstaller.InstallResult> InstallFromNuGetAsync(string packageId, string version, CancellationToken cancellationToken = default)
    {
        if (_nuGetStatus is not null)
            _nuGetStatus.Text = $"Installing {packageId} {version}...";
        var result = await _packageManager.InstallAsync(packageId, version, cancellationToken);
        if (_nuGetStatus is not null)
            _nuGetStatus.Text = result.Success
                ? $"Installed {packageId} {version} to {result.InstallDirectory}"
                : $"Install failed: {result.Error}";
        if (result.Success)
            RefreshAddInsList();
        return result;
    }

    /// <summary>
    /// Unregisters and deletes a package-installed AddIn (see
    /// <see cref="AddInPackageManagerService.Uninstall"/>). Preinstalled AddIns cannot be
    /// uninstalled this way - only disabled, via <see cref="ToggleEnabledByName"/>.
    /// </summary>
    public bool UninstallByName(string identityOrName)
    {
        var removed = _packageManager.Uninstall(identityOrName);
        if (removed)
            RefreshAddInsList();
        return removed;
    }

    private void RefreshAddInsList()
    {
        _addIns.Clear();
        var addInTree = ServiceSingleton.GetRequiredService<IAddInTree>();
        foreach (var addIn in addInTree.AddIns.OrderBy(addIn => addIn.Name, StringComparer.OrdinalIgnoreCase))
        {
            _addIns.Add(new AddInScoutAddInItem(
                addIn.Name,
                addIn.Manifest?.PrimaryIdentity,
                addIn.Version?.ToString() ?? string.Empty,
                addIn.Enabled,
                addIn.IsPreinstalled,
                addIn.FileName,
                addIn.Paths.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()));
        }
    }

    private void LoadModel()
    {
        var addInTree = ServiceSingleton.GetRequiredService<IAddInTree>();
        var root = AddInTree.GetTreeNode(string.Empty);
        foreach (var item in EnumeratePaths(root, string.Empty).OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            _paths.Add(item);
        }

        RefreshAddInsList();
    }

    private static IEnumerable<AddInScoutPathItem> EnumeratePaths(AddInTreeNode node, string path)
    {
        yield return new AddInScoutPathItem(string.IsNullOrEmpty(path) ? "/" : path, node.Codons.Count);
        foreach (var child in node.ChildNodes.OrderBy(child => child.Key, StringComparer.OrdinalIgnoreCase))
        {
            var childPath = string.IsNullOrEmpty(path) ? child.Key : path + "/" + child.Key;
            foreach (var item in EnumeratePaths(child.Value, childPath))
            {
                yield return item;
            }
        }
    }

    private void SelectPath(AddInScoutPathItem? item)
    {
        _codons.Clear();
        if (item is null)
        {
            _details.Text = "Select an AddInTree path or addin.";
            return;
        }

        var path = item.Path == "/" ? string.Empty : item.Path;
        var node = AddInTree.GetTreeNode(path, false);
        _details.Text = $"{item.Path}\n{item.CodonCount} codon(s)";
        if (node is null)
        {
            return;
        }

        foreach (var codon in node.Codons)
        {
            _codons.Add(AddInScoutCodonItem.FromCodon(codon));
        }
    }

    private void SelectAddIn(AddInScoutAddInItem? item)
    {
        _codons.Clear();
        if (item is null)
        {
            _details.Text = "Select an AddInTree path or addin.";
            return;
        }

        _details.Text = $"{item.Name}\nIdentity: {item.Identity}\nVersion: {item.Version}\nEnabled: {item.Enabled}\nPreinstalled: {item.Preinstalled}\n{item.FileName}";
        foreach (var path in item.Paths)
        {
            var node = AddInTree.GetTreeNode(path, false);
            if (node is null)
            {
                continue;
            }

            foreach (var codon in node.Codons.Where(codon => string.Equals(codon.AddIn.FileName, item.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                _codons.Add(AddInScoutCodonItem.FromCodon(codon, path));
            }
        }
    }

    private static DataTemplate CreatePathTemplate()
    {
        return new DataTemplate(() =>
        {
            var root = new Grid { Padding = new Thickness(8, 5, 8, 5), ColumnSpacing = 8 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            var path = CreateCell("Path", true);
            var count = CreateCell("CodonCount", false);
            Grid.SetColumn(path, 0);
            Grid.SetColumn(count, 1);
            root.Children.Add(path);
            root.Children.Add(count);
            return root;
        });
    }

    private static DataTemplate CreateAddInTemplate()
    {
        return new DataTemplate(() =>
        {
            var panel = new StackPanel { Padding = new Thickness(8, 5, 8, 5) };
            panel.Children.Add(CreateCell("Name", true));
            panel.Children.Add(CreateCell("Identity", false));
            return panel;
        });
    }

    private static DataTemplate CreateCodonTemplate()
    {
        return new DataTemplate(() =>
        {
            var root = new Grid { Padding = new Thickness(8, 5, 8, 5), ColumnSpacing = 10 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = CreateCell("Name", true);
            var id = CreateCell("Id", false);
            var addIn = CreateCell("AddInName", false);
            var properties = CreateCell("Properties", false);
            Grid.SetColumn(name, 0);
            Grid.SetColumn(id, 1);
            Grid.SetColumn(addIn, 2);
            Grid.SetColumn(properties, 3);
            root.Children.Add(name);
            root.Children.Add(id);
            root.Children.Add(addIn);
            root.Children.Add(properties);
            return root;
        });
    }

    private static TextBlock CreateCell(string path, bool strong)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };
        text.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(path) });
        return text;
    }

    private static void CopyCodons(ListView list)
    {
        var codons = list.SelectedItems.OfType<AddInScoutCodonItem>().ToArray();
        if (codons.Length == 0 && list.SelectedItem is AddInScoutCodonItem single)
        {
            codons = new[] { single };
        }

        if (codons.Length == 0)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, codons.Select(codon => $"{codon.Path}\t{codon.Name}\t{codon.Id}\t{codon.AddInName}\t{codon.Properties}")));
        Clipboard.SetContent(package);
    }

    private sealed record AddInScoutPathItem(string Path, int CodonCount);

    private sealed record AddInScoutAddInItem(
        string Name,
        string? Identity,
        string Version,
        bool Enabled,
        bool Preinstalled,
        string FileName,
        IReadOnlyList<string> Paths);

    private sealed record AddInScoutSearchResultItem(string Id, string Version, string Description);

    private sealed record AddInScoutCodonItem(string Path, string Name, string Id, string AddInName, string Properties)
    {
        public static AddInScoutCodonItem FromCodon(Codon codon, string path = "")
        {
            var properties = string.Join("; ", codon.Properties.Keys.Select(key => $"{key}={codon.Properties[key]}"));
            return new AddInScoutCodonItem(path, codon.Name, codon.Id, codon.AddIn.Name, properties);
        }
    }

    private sealed class AddInScoutNavigationPoint : INavigationPoint
    {
        public string FileName { get; private set; } = string.Empty;
        public string Description => "AddIn Scout";
        public string FullDescription => Description;
        public string ToolTip => Description;
        public object NavigationData => Description;
        public int Index => 0;
        public void JumpTo() { }
        public void FileNameChanged(string newName) => FileName = newName;
        public void ContentChanging(object sender, EventArgs e) { }
        public int CompareTo(object? obj) => obj is AddInScoutNavigationPoint ? 0 : -1;
    }
}
