using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace UnoDevelop.StartPage;

// Start Page tab — shown at startup when no solution is loaded, closes automatically when one opens.
// Mirrors the SharpDevelop WPF start page (StartPage addin) adapted to WinUI/Uno.
public sealed class StartPageViewContent : IViewContent
{
    private readonly ScrollViewer _root;
    private readonly ListView _recentList;
    private bool _isDisposed;

    public StartPageViewContent()
    {
        _recentList = BuildRecentListView();
        _root = BuildRoot(_recentList);
        _ = LoadRecentProjectsAsync();
    }

    // ── IViewContent ──────────────────────────────────────────────────────────

    public object? Control => _root;
    public object? InitiallyFocusedControl => _root;
    public IWorkbenchWindow? WorkbenchWindow { get; set; }

    public string TabPageText => "Start Page";
    public string TitleName => "Start Page";
    public string InfoTip => "Start Page";

    public FileName? PrimaryFileName => null;
    public OpenedFile? PrimaryFile => null;
    public IList<OpenedFile> Files => Array.Empty<OpenedFile>();

    public bool IsReadOnly => true;
    public bool IsViewOnly => true;
    public bool CloseWithSolution => false;
    public bool IsDisposed => _isDisposed;
    public bool IsDirty => false;

    public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();

    public event EventHandler TabPageTextChanged { add { } remove { } }
    public event EventHandler TitleNameChanged { add { } remove { } }
    public event EventHandler InfoTipChanged { add { } remove { } }
    public event EventHandler IsDirtyChanged { add { } remove { } }
    public event EventHandler Disposed;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    public INavigationPoint BuildNavPoint() => null!;
    public void Save(OpenedFile file, Stream stream) { }
    public void Load(OpenedFile file, Stream stream) { }
    public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;
    public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;
    public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
    public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }
    public object? GetService(Type serviceType) => null;

    // ── UI construction ───────────────────────────────────────────────────────

    private static ScrollViewer BuildRoot(ListView recentList)
    {
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1B, 0x50, 0x8C)),
            Padding = new Thickness(16, 10, 16, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "UnoDevelop",
                        FontSize = 24,
                        FontWeight = new Windows.UI.Text.FontWeight(700),
                        Foreground = new SolidColorBrush(Colors.White),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "  SharpDevelop on Uno Platform",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 2),
                    },
                }
            }
        };

        var sectionHeaderBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xA8, 0xC6, 0xE3)),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Padding = new Thickness(15, 4, 15, 4),
            Child = new TextBlock
            {
                Text = "Recent Projects",
                FontSize = 13,
                FontWeight = new Windows.UI.Text.FontWeight(700),
                Foreground = new SolidColorBrush(Colors.White),
            }
        };

        var openButton = new Button
        {
            Content = "Open Solution or Project...",
            Margin = new Thickness(0, 8, 0, 0),
        };
        openButton.Click += async (_, _) => await OpenProjectDialogAsync();

        var sectionBody = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xDC, 0xDD, 0xDE)),
            CornerRadius = new CornerRadius(0, 0, 10, 10),
            Padding = new Thickness(15),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children = { recentList, openButton }
            }
        };

        var section = new StackPanel
        {
            Margin = new Thickness(8, 12, 8, 8),
            Orientation = Orientation.Vertical,
            Children = { sectionHeaderBorder, sectionBody }
        };

        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xA8, 0xC6, 0xE3)),
            Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock
            {
                Text = "UnoDevelop — open-source. Licensed under the MIT License.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap,
            }
        };

        var outer = new Grid { MinWidth = 260 };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(headerBorder, 0);
        Grid.SetRow(section, 1);
        Grid.SetRow(footer, 2);

        outer.Children.Add(headerBorder);
        outer.Children.Add(section);
        outer.Children.Add(footer);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = outer,
        };
    }

    private ListView BuildRecentListView()
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            MaxHeight = 320,
            Visibility = Visibility.Collapsed,
        };

        list.ItemTemplate = BuildRecentItemTemplate();
        list.ItemClick += OnRecentItemClick;
        return list;
    }

    private static DataTemplate BuildRecentItemTemplate()
    {
        return new DataTemplate(() =>
        {
            var nameBlock = new TextBlock { FontSize = 13 };
            nameBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Name") });

            var dateBlock = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.6,
                Margin = new Thickness(12, 0, 0, 0),
            };
            dateBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("LastModification") });

            var pathBlock = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0),
            };
            pathBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath("Path") });

            var topRow = new StackPanel { Orientation = Orientation.Horizontal };
            topRow.Children.Add(nameBlock);
            topRow.Children.Add(dateBlock);

            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Padding = new Thickness(0, 4, 0, 4),
                Children = { topRow, pathBlock }
            };
            return panel;
        });
    }

    private void OnRecentItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecentItem item)
            OpenProject(item.Path);
    }

    private static void OpenProject(string path)
    {
        var projectService = ServiceSingleton.ServiceProvider.GetService(typeof(IProjectService)) as IProjectService;
        if (projectService is null) return;
        var fn = FileName.Create(path);
        if (fn is null) return;
        if (!projectService.OpenSolutionOrProject(fn))
            ServiceSingleton.GetRequiredService<IMessageService>().ShowError("Failed to open: " + path);
    }

    private static async Task OpenProjectDialogAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Solution and Project Files (*.sln;*.slnx;*.csproj)|*.sln;*.slnx;*.csproj|All Files (*.*)|*.*",
            Multiselect = false,
        };
        // Must use the async picker here: this runs on the UI thread, and ShowDialog() would
        // deadlock waiting for a native picker that itself needs the UI thread to pump.
        if (await dialog.ShowDialogAsync() == true)
            OpenProject(dialog.FileName);
    }

    private async Task LoadRecentProjectsAsync()
    {
        var recentOpen = ServiceSingleton.ServiceProvider.GetService(typeof(IRecentOpen)) as IRecentOpen;
        if (recentOpen is null) return;

        var paths = recentOpen.RecentProjects.Select(f => f.ToString()).ToArray();
        var items = new List<RecentItem>();

        await Task.Run(() =>
        {
            foreach (var p in paths)
            {
                var fi = new FileInfo(p);
                if (fi.Exists)
                    items.Add(new RecentItem(
                        Path.GetFileNameWithoutExtension(p),
                        fi.LastWriteTime.ToShortDateString(),
                        p));
            }
        });

        if (items.Count == 0) return;

        // Must marshal back to UI thread.
        if (_root.DispatcherQueue is { } dq)
        {
            dq.TryEnqueue(() =>
            {
                _recentList.ItemsSource = items;
                _recentList.Visibility = Visibility.Visible;
            });
        }
    }

    private sealed record RecentItem(string Name, string LastModification, string Path);
}
