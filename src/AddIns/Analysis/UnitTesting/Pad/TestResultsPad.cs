using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.UnitTesting;
using ICSharpCode.UnitTesting.Mtp;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WpfToolBar = System.Windows.Controls.ToolBar;

namespace UnoDevelop.UnitTesting;

/// <summary>
/// Test Explorer pad: a hierarchical tree of the discovered unit tests (project → target
/// framework → namespace → class → method) with a toolbar to run/refresh, live per-test result
/// icons, and status roll-up onto the parent nodes. Consumes the classic
/// ICSharpCode.UnitTesting.ITestService/ITestSolution/ITest tree directly (see
/// doc/technotes/unit-testing.md) - no local grouping/rollup logic here, both the tree shape and
/// the composite-result rollup onto container nodes are provided by the model itself
/// (TestCollection.CompositeResult, wired via TestBase.BindResultToCompositeResultOfNestedTests).
/// </summary>
public sealed class TestResultsPad : UserControl
{
    private readonly TreeView _treeView;
    private readonly WpfToolBar _toolbar;
    private ITestService? _testService;

    public WpfToolBar Toolbar => _toolbar!;
    public TreeView Tree => _treeView;

    // ITest -> its TreeViewNode, so a ResultChanged/DisplayNameChanged event (fired by the model,
    // not by us) can update the right node in O(1), and so CollectionChanged handlers know which
    // node's children to rebuild.
    private readonly Dictionary<ITest, TreeViewNode> _nodeByTest = new();

    // Client-side only: the model's TestResultType has no "running" state (None/Success/Failure/
    // Ignored), so a live "currently executing" indicator has no home in the model - tracked here
    // instead, cleared as soon as the model reports a real result via ResultChanged.
    private readonly HashSet<ITest> _running = new();

    private static readonly Dictionary<TestResultType, string> Icons = new()
    {
        [TestResultType.None] = "○",
        [TestResultType.Success] = "✓",
        [TestResultType.Failure] = "✗",
        [TestResultType.Ignored] = "∅",
    };
    private const string RunningIcon = "◔";

    private static readonly Dictionary<TestResultType, Color> IconColors = new()
    {
        [TestResultType.None] = Color.FromArgb(255, 180, 180, 180),
        [TestResultType.Success] = Color.FromArgb(255, 40, 180, 40),
        [TestResultType.Failure] = Color.FromArgb(255, 220, 40, 40),
        [TestResultType.Ignored] = Color.FromArgb(255, 180, 180, 40),
    };
    private static readonly Color RunningColor = Color.FromArgb(255, 60, 120, 220);

    public TestResultsPad()
    {
        _toolbar = new WpfToolBar
        {
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _treeView = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            ItemTemplate = CreateNodeTemplate(),
        };
        AutomationProperties.SetAutomationId(_treeView, "test-results-tree");
        // SharpDevelop's UnitTestsPad only selects a node on click; running a test is an explicit
        // action via the toolbar or the node's context menu, never a side effect of selection.
        _treeView.RightTapped += OnTreeRightTapped;
        _treeView.DoubleTapped += OnTreeDoubleTapped;

        Content = BuildLayout();
    }

    private UIElement BuildLayout()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _treeView,
        };

        var treeBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            Child = scrollViewer,
        };

        var grid = new Grid
        {
            Margin = new Thickness(8),
            RowSpacing = 8,
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(_toolbar, 0);
        grid.Children.Add(_toolbar);
        Grid.SetRow(treeBorder, 1);
        grid.Children.Add(treeBorder);

        return grid;
    }

    public void RunSelectedTest()
    {
        if (_treeView.SelectedNode is { } node)
            _ = RunNodeAsync(node);
    }

    public void ExpandAll() => SetAllExpanded(true);
    public void CollapseAll() => SetAllExpanded(false);

    private static DataTemplate CreateNodeTemplate()
    {
        return new DataTemplate(() =>
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var iconBlock = new TextBlock
            {
                FontSize = 15,
                Width = 18,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            iconBlock.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath("Content.Icon"),
                Mode = BindingMode.OneWay,
            });
            iconBlock.SetBinding(TextBlock.ForegroundProperty, new Binding
            {
                Path = new PropertyPath("Content.IconBrush"),
                Mode = BindingMode.OneWay,
            });
            stack.Children.Add(iconBlock);

            var nameBlock = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            nameBlock.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath("Content.DisplayName"),
                Mode = BindingMode.OneWay,
            });
            stack.Children.Add(nameBlock);

            var resultBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.6,
            };
            resultBlock.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath("Content.ResultLabel"),
                Mode = BindingMode.OneWay,
            });
            stack.Children.Add(resultBlock);

            return stack;
        });
    }

    public void Attach(ITestService testService)
    {
        _testService = testService;
        testService.OpenSolutionChanged += OnOpenSolutionChanged;
        RefreshTests();
    }

    private void OnOpenSolutionChanged(object? sender, EventArgs e) => RefreshTests();

    public void RefreshTests() => _ = RefreshTestsAsync();

    public async Task RefreshTestsAsync()
    {
        if (_testService is null) return;
        // Discovery starts an MTP host process per target framework and can take tens of seconds,
        // so the status bar offers a cancel button for as long as this monitor lives rather than
        // leaving the user to wait it out.
        using var cancellation = new CancellationTokenSource();
        using var monitor = SD.StatusBar.CreateCancellableProgressMonitor(cancellation);
        monitor.TaskName = "Discovering tests...";

        var solution = _testService.OpenSolution;
        // Force a fresh MTP discovery pass on every already-known project - mirrors the explicit
        // "Refresh Tests" UX this pad has always had. Discovery also happens automatically on
        // solution-open and after every build (MtpTestProject.OnBuildFinished), this just lets the
        // user ask for it on demand too. Accessing .NestedTests here is itself what lazily triggers
        // OnNestedTestsInitialized() the very first time for a project never touched before.
        //
        // Awaiting every pass (rather than firing them off and rebuilding straight away) is what
        // makes the rebuilt tree actually show the refreshed results: discovery spawns an MTP host
        // process per target framework, so it completes well after this method's first continuation.
        var refreshes = solution.NestedTests
            .OfType<MtpTestProject>()
            .Select(mtpProject => mtpProject.RefreshAsync(cancellation.Token))
            .ToList();
        await Task.WhenAll(refreshes);

        // Rebuild even when cancelled: the tree still holds the approximate (Roslyn) results, and
        // showing those beats leaving the pad looking like the refresh never happened.
        _ = DispatcherQueue.TryEnqueue(() => Rebuild(solution));
    }

    private void Rebuild(ITestSolution solution)
    {
        foreach (var root in _treeView.RootNodes)
            if (root.Content is TestNodeData data)
                UnbindRecursive(data.Test);
        _treeView.RootNodes.Clear();
        _nodeByTest.Clear();

        foreach (var project in solution.NestedTests.OrderBy(t => t.DisplayName, StringComparer.Ordinal))
        {
            var node = new TreeViewNode { IsExpanded = true };
            _treeView.RootNodes.Add(node);
            BindNode(node, project);
        }
    }

    // Wires a TreeViewNode to the ITest it represents: subscribes to the model's own
    // ResultChanged/DisplayNameChanged/NestedTests.CollectionChanged, then (re)builds its children.
    // Re-entrant: NestedTests.CollectionChanged calls back into this same rebuild path, which is
    // exactly how a project's approximate (Roslyn-scanned) children get replaced by MTP-confirmed
    // ones once discovery completes in the background (MtpTestProject.PopulateTree() clears and
    // repopulates NestedTestCollection - this only has to react, not know why it changed).
    private void BindNode(TreeViewNode node, ITest test)
    {
        node.Content = new TestNodeData(this, test);
        _nodeByTest[test] = node;
        test.ResultChanged += OnTestResultChanged;
        test.DisplayNameChanged += OnTestDisplayNameChanged;
        test.NestedTests.CollectionChanged += (_, _) =>
            _ = DispatcherQueue.TryEnqueue(() => RebuildChildren(node, test));

        RebuildChildren(node, test);
    }

    private void RebuildChildren(TreeViewNode node, ITest test)
    {
        foreach (var child in node.Children)
            if (child.Content is TestNodeData data)
                UnbindRecursive(data.Test);
        node.Children.Clear();

        foreach (var child in test.NestedTests.OrderBy(t => t.DisplayName, StringComparer.Ordinal))
        {
            var childNode = new TreeViewNode { IsExpanded = true };
            node.Children.Add(childNode);
            BindNode(childNode, child);
        }
    }

    private void UnbindRecursive(ITest test)
    {
        test.ResultChanged -= OnTestResultChanged;
        test.DisplayNameChanged -= OnTestDisplayNameChanged;
        _running.Remove(test);
        _nodeByTest.Remove(test);
        // Safe to always enumerate (not gated on whether NestedTests was already initialized):
        // every ITest reachable here came from a node this pad itself created via BindNode, which
        // already touched .NestedTests once - this never lazily triggers a fresh discovery pass
        // for a node the pad never visited.
        foreach (var child in test.NestedTests)
            UnbindRecursive(child);
    }

    private void OnTestResultChanged(object? sender, TestResultTypeChangedEventArgs e)
    {
        if (sender is not ITest test) return;
        _running.Remove(test);
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_nodeByTest.TryGetValue(test, out var node) && node.Content is TestNodeData data)
                data.Refresh();
        });
    }

    private void OnTestDisplayNameChanged(object? sender, EventArgs e)
    {
        if (sender is not ITest test) return;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_nodeByTest.TryGetValue(test, out var node) && node.Content is TestNodeData data)
                data.Refresh();
        });
    }

    private void SetAllExpanded(bool expanded)
    {
        foreach (var root in _treeView.RootNodes)
            SetExpandedRecursive(root, expanded);
    }

    private static void SetExpandedRecursive(TreeViewNode node, bool expanded)
    {
        if (node.Children.Count == 0)
            return;
        node.IsExpanded = expanded;
        foreach (var child in node.Children)
            SetExpandedRecursive(child, expanded);
    }

    private void OnTreeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TryResolveNode(e.OriginalSource) is not { Content: TestNodeData { Test: MtpTestMethod method } })
            return;

        e.Handled = true;
        _ = NavigateToTestAsync(method);
    }

    // Double-click-to-source: the MTP test host reports "class X, method Y" (location.type/
    // location.method), not a file/line - it has no reason to read PDBs just to answer
    // --list-tests. Roslyn already knows where every type/method is declared, so resolve the
    // rest here instead of waiting on the protocol to ever report source locations itself.
    private static async Task NavigateToTestAsync(MtpTestMethod method)
    {
        var typeFullName = method.Node.LocationType;
        var methodName = method.Node.LocationMethodName;
        if (string.IsNullOrEmpty(typeFullName) || string.IsNullOrEmpty(methodName))
            return;

        var registry = ServiceSingleton.ServiceProvider.GetService(typeof(LanguageServiceRegistry)) as LanguageServiceRegistry;
        var languageService = registry?.GetService(".cs");
        if (languageService is null)
            return;

        var targets = await languageService.FindMemberAsync(typeFullName, methodName, method.Node.LocationMethodParameterCount, CancellationToken.None);
        if (targets.Count == 0)
            return;

        var target = targets[0];
        SD.FileService.JumpToFilePosition(FileName.Create(target.FileName), target.Position.Line, target.Position.Column);
    }

    private void OnTreeRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var node = TryResolveNode(e.OriginalSource);
        if (node is not null)
            _treeView.SelectedNode = node;

        var menu = BuildContextMenu(node);
        menu.ShowAt(_treeView, new FlyoutShowOptions { Position = e.GetPosition(_treeView) });
        e.Handled = true;
    }

    private static TreeViewNode? TryResolveNode(object originalSource)
    {
        for (var current = originalSource as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TreeViewItem item && item.DataContext is TreeViewNode node)
                return node;
        }
        return null;
    }

    private MenuFlyout BuildContextMenu(TreeViewNode? node)
    {
        var running = _testService?.IsRunningTests ?? false;
        var menu = new MenuFlyout();

        var run = new MenuFlyoutItem { Text = "Run", IsEnabled = node is not null && !running };
        run.Click += (_, _) => { if (node is not null) _ = RunNodeAsync(node); };
        menu.Items.Add(run);

        var runAll = new MenuFlyoutItem { Text = "Run All Tests", IsEnabled = !running };
        runAll.Click += (_, _) => _ = RunAllAsync();
        menu.Items.Add(runAll);

        var stop = new MenuFlyoutItem { Text = "Stop", IsEnabled = running };
        stop.Click += (_, _) => _testService?.CancelRunningTests();
        menu.Items.Add(stop);

        menu.Items.Add(new MenuFlyoutSeparator());

        var refresh = new MenuFlyoutItem { Text = "Refresh Tests", IsEnabled = !running };
        refresh.Click += (_, _) => RefreshTests();
        menu.Items.Add(refresh);

        menu.Items.Add(new MenuFlyoutSeparator());

        var expandAll = new MenuFlyoutItem { Text = "Expand All" };
        expandAll.Click += (_, _) => ExpandAll();
        menu.Items.Add(expandAll);

        var collapseAll = new MenuFlyoutItem { Text = "Collapse All" };
        collapseAll.Click += (_, _) => CollapseAll();
        menu.Items.Add(collapseAll);

        return menu;
    }

    public async Task RunAllAsync()
    {
        if (_testService is null || _testService.IsRunningTests) return;
        var solution = _testService.OpenSolution;
        MarkRunning(solution);
        await _testService.RunTestsAsync([solution], new TestExecutionOptions());
    }

    private async Task RunNodeAsync(TreeViewNode node)
    {
        if (_testService is null || _testService.IsRunningTests) return;
        if (node.Content is not TestNodeData data) return;
        MarkRunning(data.Test);
        await _testService.RunTestsAsync([data.Test], new TestExecutionOptions());
    }

    // Client-side-only "running" indicator (see the field's own comment) - marks the selected
    // test and everything already-known beneath it so the icon shows immediately, rather than
    // staying on its last (possibly stale) result until the run actually completes.
    private void MarkRunning(ITest test)
    {
        _running.Add(test);
        if (_nodeByTest.TryGetValue(test, out var node) && node.Content is TestNodeData data)
            data.Refresh();
        foreach (var child in test.NestedTests)
            MarkRunning(child);
    }

    public new void Dispose()
    {
        if (_testService is not null)
            _testService.OpenSolutionChanged -= OnOpenSolutionChanged;
        foreach (var root in _treeView.RootNodes)
            if (root.Content is TestNodeData data)
                UnbindRecursive(data.Test);
    }

    private sealed class TestNodeData : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly TestResultsPad _owner;

        public TestNodeData(TestResultsPad owner, ITest test)
        {
            _owner = owner;
            Test = test;
        }

        public ITest Test { get; }

        public string DisplayName => Test.DisplayName;

        public string Icon => IsRunning ? RunningIcon : Icons.GetValueOrDefault(Test.Result, "○");

        public Brush IconBrush => new SolidColorBrush(IsRunning ? RunningColor : IconColors.GetValueOrDefault(Test.Result, IconColors[TestResultType.None]));

        public string ResultLabel => IsRunning
            ? "Run..."
            : Test.Result switch
            {
                TestResultType.Success => "Pass",
                TestResultType.Failure => "Fail",
                TestResultType.Ignored => "Skip",
                _ => "",
            };

        private bool IsRunning => _owner._running.Contains(Test);

        public void Refresh()
        {
            Raise(nameof(Icon));
            Raise(nameof(IconBrush));
            Raise(nameof(ResultLabel));
        }

        private void Raise(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
