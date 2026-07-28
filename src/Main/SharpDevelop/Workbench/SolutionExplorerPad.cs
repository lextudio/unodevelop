using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using UnoDevelop.Services;
using Windows.UI;
using WpfToolBar = System.Windows.Controls.ToolBar;
using ICSharpCode.SharpDevelop.Services;

namespace UnoDevelop.Workbench;

public sealed class SolutionExplorerPad : UserControl
{
    private MainPage? _host;

    public SolutionExplorerPad()
    {
        Toolbar = new WpfToolBar();

        Tree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            ItemTemplate = CreateNodeTemplate()
        };

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = Tree
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(Toolbar, 0);
        grid.Children.Add(Toolbar);
        Grid.SetRow(scrollViewer, 1);
        grid.Children.Add(scrollViewer);

        Content = grid;
    }

    public WpfToolBar Toolbar { get; }

    public TreeView Tree { get; }

    public void Attach(MainPage host)
    {
        if (_host is not null)
        {
            Tree.KeyDown -= _host.OnSolutionTreeKeyDown;
            Tree.RightTapped -= _host.OnSolutionTreeRightTapped;
            Tree.SelectionChanged -= _host.OnSolutionTreeSelectionChanged;
            Tree.ItemInvoked -= _host.OnSolutionTreeItemInvoked;
        }

        _host = host;
        Tree.KeyDown += host.OnSolutionTreeKeyDown;
        Tree.RightTapped += host.OnSolutionTreeRightTapped;
        Tree.SelectionChanged += host.OnSolutionTreeSelectionChanged;
        Tree.ItemInvoked += host.OnSolutionTreeItemInvoked;
    }

    private static DataTemplate CreateNodeTemplate()
    {
        return new DataTemplate(() =>
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.SetBinding(OpacityProperty, new Binding
            {
                Path = new PropertyPath("Content.Kind"),
                Converter = NodeOpacityConverter.Instance
            });

            var icon = new Image
            {
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetBinding(Image.SourceProperty, new Binding
            {
                Path = new PropertyPath("Content.IconUri")
            });

            var iconHost = new Grid
            {
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconHost.Children.Add(icon);
            iconHost.Children.Add(CreateOverlay(
                "ms-appx:///Icons/LinkOverlay_16x.svg",
                ProjectBrowserNodeKind.LinkedFile));
            iconHost.Children.Add(CreateOverlay(
                "ms-appx:///Icons/MissingOverlay_16x.svg",
                ProjectBrowserNodeKind.MissingFile));
            iconHost.Children.Add(CreateOverlay(
                "ms-appx:///Icons/GhostOverlay_16x.svg",
                ProjectBrowserNodeKind.GhostFile,
                ProjectBrowserNodeKind.GhostFolder));

            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath("Content.Name")
            });
            text.SetBinding(TextBlock.ForegroundProperty, new Binding
            {
                Path = new PropertyPath("Content"),
                Converter = NodeForegroundConverter.Instance
            });
            text.SetBinding(TextBlock.FontStyleProperty, new Binding
            {
                Path = new PropertyPath("Content.Kind"),
                Converter = NodeFontStyleConverter.Instance
            });

            stack.Children.Add(iconHost);
            stack.Children.Add(text);
            return stack;
        });
    }

    // A corner badge composited over the node's base icon. The VS2017 overlay assets are full
    // 16x16 canvases with the mark in the bottom-right corner and a transparent remainder, so they
    // stack directly on the icon. Visibility is driven by the node kind.
    private static Image CreateOverlay(string iconUri, params ProjectBrowserNodeKind[] kinds)
    {
        var overlay = new Image
        {
            Width = 16,
            Height = 16,
            Source = new SvgImageSource(new Uri(iconUri)),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        overlay.SetBinding(VisibilityProperty, new Binding
        {
            Path = new PropertyPath("Content.Kind"),
            Converter = new NodeKindVisibilityConverter(kinds)
        });
        return overlay;
    }

    private sealed class NodeKindVisibilityConverter : IValueConverter
    {
        private readonly ProjectBrowserNodeKind[] _kinds;

        public NodeKindVisibilityConverter(ProjectBrowserNodeKind[] kinds) => _kinds = kinds;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is ProjectBrowserNodeKind kind && Array.IndexOf(_kinds, kind) >= 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NodeOpacityConverter : IValueConverter
    {
        public static readonly NodeOpacityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is ProjectBrowserNodeKind.GhostFile or ProjectBrowserNodeKind.GhostFolder ? 0.58 : 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    // Bound to the whole node context (not just Kind) so it can weigh Kind and GitStatus
    // together: a missing file stays red regardless of git status, but everything else defers
    // to git status color (VS convention: orange = modified, green = added/untracked, gray
    // strikethrough-ish = deleted) when there is one, falling back to the default text color.
    private sealed class NodeForegroundConverter : IValueConverter
    {
        public static readonly NodeForegroundConverter Instance = new();
        private static readonly SolidColorBrush MissingBrush = new(Color.FromArgb(0xFF, 0xA4, 0x26, 0x2C));
        private static readonly SolidColorBrush ModifiedBrush = new(Color.FromArgb(0xFF, 0xE3, 0x7D, 0x00));
        private static readonly SolidColorBrush AddedBrush = new(Color.FromArgb(0xFF, 0x00, 0x8A, 0x00));
        private static readonly SolidColorBrush ConflictedBrush = new(Color.FromArgb(0xFF, 0xD2, 0x1B, 0x1B));
        private static readonly SolidColorBrush DeletedBrush = new(Color.FromArgb(0xFF, 0x80, 0x80, 0x80));

        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not ProjectBrowserNodeContext context)
                return null;

            if (context.Kind == ProjectBrowserNodeKind.MissingFile)
                return MissingBrush;

            return context.GitStatus switch
            {
                GitFileStatus.Modified or GitFileStatus.Renamed => ModifiedBrush,
                GitFileStatus.Added or GitFileStatus.Untracked => AddedBrush,
                GitFileStatus.Conflicted => ConflictedBrush,
                GitFileStatus.Deleted => DeletedBrush,
                _ => null
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NodeFontStyleConverter : IValueConverter
    {
        public static readonly NodeFontStyleConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is ProjectBrowserNodeKind.LinkedFile
                ? Windows.UI.Text.FontStyle.Italic
                : Windows.UI.Text.FontStyle.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
