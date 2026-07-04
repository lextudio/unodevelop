using System;
using System.Collections.ObjectModel;
using System.Linq;
using ICSharpCode.CodeCoverage;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using WpfToolBar = System.Windows.Controls.ToolBar;

namespace UnoDevelop.AddIns.Analysis.CodeCoverage;

public sealed class CodeCoveragePad : UserControl
{
    private readonly ObservableCollection<CoverageRow> _rows = new();
    private readonly TextBlock _summary;
    private readonly ListView _list;
    private readonly Button _run;
    private readonly Button _stop;
    private readonly Button _open;

    public CodeCoveragePad()
    {
        _summary = new TextBlock
        {
            Margin = new Thickness(8, 6, 8, 4),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        var toolbar = new WpfToolBar();

        _run = CreateToolbarButton("RunTest_16x", "Run all tests with coverage", () => _ = CodeCoverageService.Instance.RunAllTestsWithCoverageAsync());
        _stop = CreateToolbarButton("Stop_16x", "Stop coverage run", () => CodeCoverageService.Instance.Stop());
        _open = CreateToolbarButton("OpenfileDialog_16x", "Open coverage file", () => new OpenCoverageFileCommand().Run());
        toolbar.Items.Add(_run);
        toolbar.Items.Add(_stop);
        toolbar.Items.Add(_open);

        _list = new ListView
        {
            ItemsSource = _rows,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateTemplate(),
            Margin = new Thickness(0)
        };
        _list.DoubleTapped += (_, _) => JumpToSelected();
        BuildContextMenu();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_summary, 0);
        Grid.SetRow(toolbar, 1);
        Grid.SetRow(_list, 2);
        root.Children.Add(_summary);
        root.Children.Add(toolbar);
        root.Children.Add(_list);
        Content = root;

        CodeCoverageService.Instance.SessionChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
    }

    private static Button CreateToolbarButton(string iconName, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Content = new Image
            {
                Width = 16,
                Height = 16,
                Source = new SvgImageSource(new Uri($"ms-appx:///Icons/{iconName}.svg"))
            },
            Margin = new Thickness(0, 2, 4, 2)
        };

        ApplyFlatToolbarChrome(button);
        ToolTipService.SetToolTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void ApplyFlatToolbarChrome(ButtonBase button)
    {
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundDisabled", "ButtonBorderBrush", "ButtonBorderBrushDisabled" })
            button.Resources[key] = transparent;
    }

    private void Refresh()
    {
        var isRunning = CodeCoverageService.Instance.IsRunning;
        _run.IsEnabled = !isRunning;
        _open.IsEnabled = !isRunning;
        _stop.IsEnabled = isRunning;

        var session = CodeCoverageService.Instance.CurrentSession;
        _summary.Text = $"{session.Title}: {session.CoveragePercent}% ({session.VisitedSequencePoints}/{session.SequencePoints})";
        _rows.Clear();

        foreach (var result in session.Results)
        {
            foreach (var module in result.Modules.OrderBy(module => module.Name))
            {
                AddModuleRow(module);
            }
        }

        foreach (var line in session.LogLines.TakeLast(30))
        {
            _rows.Add(CoverageRow.Log(line));
        }
    }

    private void AddModuleRow(CodeCoverageModule module)
    {
        var total = module.Methods.Sum(method => method.SequencePointsCount);
        var visited = module.Methods.Sum(method => method.VisitedSequencePointsCount);
        _rows.Add(CoverageRow.Summary(module.Name, Percent(visited, total), visited, total));

        foreach (var method in module.Methods.OrderBy(method => method.FullClassName).ThenBy(method => method.Name))
        {
            _rows.Add(CoverageRow.Method(method));
        }
    }

    private static decimal Percent(int visited, int total)
        => total == 0 ? 0 : decimal.Round((decimal)visited * 100 / total, 1);

    private static DataTemplate CreateTemplate()
    {
        return new DataTemplate(() =>
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                Padding = new Thickness(8, 3, 8, 3)
            };

            var title = new TextBlock
            {
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            title.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(CoverageRow.Title)) });

            var detail = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            detail.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(CoverageRow.Detail)) });

            panel.Children.Add(title);
            panel.Children.Add(detail);
            return panel;
        });
    }

    private void BuildContextMenu()
    {
        var menu = new MenuFlyout();
        var goTo = new MenuFlyoutItem { Text = "Go to first uncovered line" };
        goTo.Click += (_, _) => JumpToSelected();
        menu.Items.Add(goTo);

        var copy = new MenuFlyoutItem { Text = "Copy" };
        copy.Click += (_, _) => CopySelected();
        menu.Items.Add(copy);

        _list.ContextFlyout = menu;
    }

    private void JumpToSelected()
    {
        if (_list.SelectedItem is not CoverageRow { FirstUncovered: { } point })
            return;

        SD.FileService.JumpToFilePosition(FileName.Create(point.Document), point.Line, point.Column);
    }

    private void CopySelected()
    {
        if (_list.SelectedItem is not CoverageRow row)
            return;

        var package = new DataPackage();
        package.SetText(row.Title + Environment.NewLine + row.Detail);
        Clipboard.SetContent(package);
    }

    private sealed record CoverageRow(string Title, string Detail, CodeCoverageSequencePoint? FirstUncovered)
    {
        public static CoverageRow Log(string text) => new(text, string.Empty, null);

        public static CoverageRow Summary(string name, decimal percent, int visited, int total)
            => new($"{name} - {percent}%", $"{visited}/{total} sequence points visited", null);

        public static CoverageRow Method(CodeCoverageMethod method)
        {
            var total = method.SequencePointsCount;
            var visited = method.VisitedSequencePointsCount;
            var firstUncovered = method.SequencePoints.FirstOrDefault(point => point.VisitCount == 0 && point.HasDocument());
            return new(
                $"{method.FullClassName}.{method.Name} - {Percent(visited, total)}%",
                $"{visited}/{total} sequence points, branch {decimal.Round(method.BranchCoverage, 1)}%",
                firstUncovered);
        }
    }
}
