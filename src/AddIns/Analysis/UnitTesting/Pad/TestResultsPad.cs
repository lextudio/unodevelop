using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.UnitTesting.Simple;
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
/// Test Explorer pad: a hierarchical tree of the discovered unit tests (target framework →
/// project → namespace → class → method) with a toolbar to run/refresh, live per-test result
/// icons, and status roll-up onto the parent nodes. Ported to match SharpDevelop's UnitTestsPad UX.
/// </summary>
public sealed class TestResultsPad : UserControl
{
    private readonly TreeView _treeView;
    private readonly WpfToolBar _toolbar;
    private ITestService? _testService;

    public WpfToolBar Toolbar => _toolbar!;
    public TreeView Tree => _treeView;

    // Test key → leaf node, for O(1) live result updates.
    private readonly Dictionary<string, TreeViewNode> _leafByKey = new(StringComparer.Ordinal);

    private static readonly Dictionary<TestResultType, string> Icons = new()
    {
        [TestResultType.None] = "○",     // ○
        [TestResultType.Passing] = "✓",  // ✓
        [TestResultType.Failing] = "✗",  // ✗
        [TestResultType.Skipped] = "∅",  // ∅
        [TestResultType.Running] = "◔",  // ◔
    };

    private static readonly Dictionary<TestResultType, Color> IconColors = new()
    {
        [TestResultType.None] = Color.FromArgb(255, 180, 180, 180),
        [TestResultType.Passing] = Color.FromArgb(255, 40, 180, 40),
        [TestResultType.Failing] = Color.FromArgb(255, 220, 40, 40),
        [TestResultType.Skipped] = Color.FromArgb(255, 180, 180, 40),
        [TestResultType.Running] = Color.FromArgb(255, 60, 120, 220),
    };

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
        testService.TestResultUpdated += OnTestResultUpdated;
        testService.TestRunStarted += OnTestRunStarted;
        testService.TestRunCompleted += OnTestRunCompleted;
        RefreshTests();
    }

    private void OnTestRunStarted() { }

    private void OnTestResultUpdated(TestResultInfo result)
        => _ = DispatcherQueue.TryEnqueue(() => ApplyResult(result));

    private void OnTestRunCompleted() { }

    public void RefreshTests() => _ = RefreshTestsAsync();

    public async Task RefreshTestsAsync()
    {
        if (_testService is null) return;
        using var monitor = SD.StatusBar.CreateProgressMonitor();
        monitor.TaskName = "Refreshing tests...";
        _testService.RefreshTests();
        var tests = await Task.Run(() => _testService.GetTests(monitor));
        var lastResults = _testService.GetLastResults();
        _ = DispatcherQueue.TryEnqueue(() => Rebuild(tests, lastResults));
    }

    private void Rebuild(IReadOnlyList<TestInfo> tests, IReadOnlyDictionary<string, TestResultInfo> lastResults)
    {
        _treeView.RootNodes.Clear();
        _leafByKey.Clear();

        foreach (var targetGroup in tests
            .GroupBy(t => t.TargetFramework ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var targetDisplay = string.IsNullOrWhiteSpace(targetGroup.Key) ? "(default target)" : targetGroup.Key;
            var targetNode = new TreeViewNode
            {
                Content = new TestNodeData(targetDisplay),
                IsExpanded = true,
            };
            _treeView.RootNodes.Add(targetNode);

            foreach (var projectGroup in targetGroup
                .GroupBy(t => t.ProjectName)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var projectNode = GetOrAddChild(targetNode, "project:" + projectGroup.Key, projectGroup.Key);

                foreach (var test in projectGroup.OrderBy(t => t.FullyQualifiedName, StringComparer.Ordinal))
                {
                    var (ns, className, method) = SplitFqn(test.FullyQualifiedName, test.DisplayName);

                    var parent = projectNode;
                    if (ns.Length > 0)
                        parent = GetOrAddChild(parent, ns, ns);
                    parent = GetOrAddChild(parent, className, className);

                    var key = test.EffectiveKey;
                    var initial = lastResults.TryGetValue(key, out var r)
                        ? r.Result
                        : TestResultType.None;
                    var leaf = new TreeViewNode
                    {
                        Content = new TestNodeData(method, test.FullyQualifiedName, key, initial, test.TypeFullName, test.MethodName, test.ParameterCount),
                    };
                    parent.Children.Add(leaf);
                    _leafByKey[key] = leaf;
                }
            }
        }

        foreach (var root in _treeView.RootNodes)
            RollUp(root);
    }

    private static TreeViewNode GetOrAddChild(TreeViewNode parent, string key, string display)
    {
        foreach (var child in parent.Children)
        {
            if (child.Content is TestNodeData data && !data.IsLeaf && data.Key == key)
                return child;
        }
        var node = new TreeViewNode
        {
            Content = new TestNodeData(display) { Key = key },
            IsExpanded = true,
        };
        parent.Children.Add(node);
        return node;
    }

    // Splits "Ns.Sub.Class.Method(args)" into (namespace, class, methodLabel).
    private static (string Namespace, string ClassName, string Method) SplitFqn(string fqn, string displayName)
    {
        var parenIndex = fqn.IndexOf('(');
        var structural = parenIndex >= 0 ? fqn[..parenIndex] : fqn;
        var parts = structural.Split('.');
        if (parts.Length < 2)
            return (string.Empty, string.Empty, displayName);

        var method = parts[^1];
        var className = parts[^2];
        var ns = string.Join('.', parts[..^2]);

        // Prefer the display name's method portion (keeps parameterized-test suffixes).
        var prefix = (ns.Length > 0 ? ns + "." : string.Empty) + className + ".";
        var methodLabel = displayName.StartsWith(prefix, StringComparison.Ordinal)
            ? displayName[prefix.Length..]
            : method;

        return (ns, className, methodLabel);
    }

    private void ApplyResult(TestResultInfo result)
    {
        if (!_leafByKey.TryGetValue(result.EffectiveKey, out var leaf))
            return;
        if (leaf.Content is TestNodeData data)
            data.SetResult(result.Result);
        for (var node = leaf.Parent; node is not null; node = node.Parent)
            RollUpSingle(node);
    }

    // Recompute a container node's aggregate status from its descendants.
    private static void RollUp(TreeViewNode node)
    {
        if (node.Content is TestNodeData data && data.IsLeaf)
            return;
        foreach (var child in node.Children)
            RollUp(child);
        RollUpSingle(node);
    }

    private static void RollUpSingle(TreeViewNode node)
    {
        if (node.Content is not TestNodeData data || data.IsLeaf)
            return;
        data.SetResult(Aggregate(node.Children));
    }

    private static TestResultType Aggregate(IEnumerable<TreeViewNode> children)
    {
        var any = false;
        var anyRunning = false;
        var anyFailing = false;
        var allSkipped = true;
        var allDone = true;

        foreach (var child in children)
        {
            if (child.Content is not TestNodeData data)
                continue;
            any = true;
            var r = data.Result;
            if (r == TestResultType.Running) anyRunning = true;
            if (r == TestResultType.Failing) anyFailing = true;
            if (r != TestResultType.Skipped) allSkipped = false;
            if (r == TestResultType.None || r == TestResultType.Running) allDone = false;
        }

        if (!any) return TestResultType.None;
        if (anyRunning) return TestResultType.Running;
        if (anyFailing) return TestResultType.Failing;
        if (allSkipped) return TestResultType.Skipped;
        return allDone ? TestResultType.Passing : TestResultType.None;
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

    private static IEnumerable<string> CollectLeafKeys(TreeViewNode node)
    {
        if (node.Content is TestNodeData data && data.IsLeaf && data.TestKey is not null)
        {
            yield return data.TestKey;
            yield break;
        }
        foreach (var child in node.Children)
            foreach (var key in CollectLeafKeys(child))
                yield return key;
    }

    private void OnTreeDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TryResolveNode(e.OriginalSource) is not { Content: TestNodeData { IsLeaf: true } data })
            return;

        e.Handled = true;
        _ = NavigateToTestAsync(data);
    }

    // Double-click-to-source: the MTP test host reports "class X, method Y" (location.type/
    // location.method), not a file/line - it has no reason to read PDBs just to answer
    // --list-tests. Roslyn already knows where every type/method is declared, so resolve the
    // rest here instead of waiting on the protocol to ever report source locations itself.
    private static async Task NavigateToTestAsync(TestNodeData data)
    {
        if (string.IsNullOrEmpty(data.TypeFullName) || string.IsNullOrEmpty(data.MethodName))
            return;

        var registry = ServiceSingleton.ServiceProvider.GetService(typeof(LanguageServiceRegistry)) as LanguageServiceRegistry;
        var languageService = registry?.GetService(".cs");
        if (languageService is null)
            return;

        var targets = await languageService.FindMemberAsync(data.TypeFullName, data.MethodName, data.ParameterCount, CancellationToken.None);
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
        var running = _testService?.IsRunning ?? false;
        var menu = new MenuFlyout();

        var run = new MenuFlyoutItem { Text = "Run", IsEnabled = node is not null && !running };
        run.Click += (_, _) => { if (node is not null) _ = RunNodeAsync(node); };
        menu.Items.Add(run);

        var runAll = new MenuFlyoutItem { Text = "Run All Tests", IsEnabled = !running };
        runAll.Click += (_, _) => { if (_testService is not null) _ = _testService.RunAllTestsAsync(); };
        menu.Items.Add(runAll);

        var stop = new MenuFlyoutItem { Text = "Stop", IsEnabled = running };
        stop.Click += (_, _) => _testService?.Stop();
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

    private async Task RunNodeAsync(TreeViewNode node)
    {
        if (_testService is null || _testService.IsRunning) return;
        var keys = CollectLeafKeys(node).Distinct().ToList();
        if (keys.Count == 0) return;
        await _testService.RunTestsAsync(keys);
    }

    public new void Dispose()
    {
        if (_testService is not null)
        {
            _testService.TestResultUpdated -= OnTestResultUpdated;
            _testService.TestRunStarted -= OnTestRunStarted;
            _testService.TestRunCompleted -= OnTestRunCompleted;
        }
    }

    private sealed class TestNodeData : System.ComponentModel.INotifyPropertyChanged
    {
        private TestResultType _result;

        // Container node.
        public TestNodeData(string displayName)
        {
            DisplayName = displayName;
            Key = displayName;
            IsLeaf = false;
        }

        // Leaf (test method) node.
        public TestNodeData(
            string displayName,
            string fullyQualifiedName,
            string testKey,
            TestResultType result,
            string? typeFullName = null,
            string? methodName = null,
            int? parameterCount = null)
        {
            DisplayName = displayName;
            FullyQualifiedName = fullyQualifiedName;
            TestKey = testKey;
            Key = testKey;
            _result = result;
            IsLeaf = true;
            TypeFullName = typeFullName;
            MethodName = methodName;
            ParameterCount = parameterCount;
        }

        public string DisplayName { get; }
        public string? FullyQualifiedName { get; }
        public string? TypeFullName { get; }
        public string? MethodName { get; }
        public int? ParameterCount { get; }
        public string? TestKey { get; }
        public string Key { get; set; }
        public bool IsLeaf { get; }

        public TestResultType Result => _result;

        public string Icon => Icons.GetValueOrDefault(_result, "○");

        public Brush IconBrush => new SolidColorBrush(IconColors.GetValueOrDefault(_result, IconColors[TestResultType.None]));

        public string ResultLabel => _result switch
        {
            TestResultType.Passing => "Pass",
            TestResultType.Failing => "Fail",
            TestResultType.Skipped => "Skip",
            TestResultType.Running => "Run...",
            _ => "",
        };

        public void SetResult(TestResultType result)
        {
            if (_result == result)
                return;
            _result = result;
            Raise(nameof(Icon));
            Raise(nameof(IconBrush));
            Raise(nameof(ResultLabel));
        }

        private void Raise(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
