using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ICSharpCode.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using UnoDevelop.Services;
using WpfToolBar = System.Windows.Controls.ToolBar;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace UnoDevelop.Workbench;

public sealed class ErrorListPad : UserControl
{
    private readonly ObservableCollection<UnoTask> _errors = new();
    private readonly UnoTaskService _taskService;
    private readonly ListView _errorView;
    private readonly WpfToggleButton _showErrors;
    private readonly WpfToggleButton _showWarnings;
    private readonly WpfToggleButton _showMessages;
    private readonly Properties _properties;

    public ErrorListPad()
        : this(ICSharpCode.Core.ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService
            ?? throw new InvalidOperationException("UnoTaskService is not registered."))
    {
    }

    public ErrorListPad(UnoTaskService taskService)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _properties = PropertyService.NestedProperties("ErrorListPad");

        _showErrors = CreateFilterToggle("Errors", ShowErrors);
        _showWarnings = CreateFilterToggle("Warnings", ShowWarnings);
        _showMessages = CreateFilterToggle("Messages", ShowMessages);

        var toolbar = new WpfToolBar();
        toolbar.Items.Add(_showErrors);
        toolbar.Items.Add(_showWarnings);
        toolbar.Items.Add(_showMessages);

        var header = CreateHeader();
        _errorView = new ListView
        {
            ItemsSource = _errors,
            ItemTemplate = CreateItemTemplate(),
            SelectionMode = ListViewSelectionMode.Extended
        };
        _errorView.DoubleTapped += ErrorViewDoubleTapped;
        BuildContextMenu();

        var contentPanel = new Grid();
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(header, 1);
        Grid.SetRow(_errorView, 2);
        contentPanel.Children.Add(toolbar);
        contentPanel.Children.Add(header);
        contentPanel.Children.Add(_errorView);
        Content = contentPanel;

        _taskService.Cleared += TaskServiceCleared;
        _taskService.Added += TaskServiceAdded;
        _taskService.Removed += TaskServiceRemoved;
        _taskService.InUpdateChanged += TaskServiceInUpdateChanged;

        InternalShowResults();
    }

    public event Action<string, int, int>? ItemActivated;

    public bool ShowErrors
    {
        get => _properties.Get("ShowErrors", true);
        set
        {
            _properties.Set("ShowErrors", value);
            _showErrors.IsChecked = value;
            InternalShowResults();
        }
    }

    public bool ShowWarnings
    {
        get => _properties.Get("ShowWarnings", true);
        set
        {
            _properties.Set("ShowWarnings", value);
            _showWarnings.IsChecked = value;
            InternalShowResults();
        }
    }

    public bool ShowMessages
    {
        get => _properties.Get("ShowMessages", true);
        set
        {
            _properties.Set("ShowMessages", value);
            _showMessages.IsChecked = value;
            InternalShowResults();
        }
    }

    private WpfToggleButton CreateFilterToggle(string text, bool isChecked)
    {
        var button = new WpfToggleButton
        {
            Content = CreateIconTextContent(GetIconName(text), text),
            IsChecked = isChecked,
            Margin = new Thickness(0, 2, 4, 2)
        };
        AutomationProperties.SetAutomationId(button, $"ErrorListPad.Filter.{text}");
        AutomationProperties.SetName(button, text);
        ToolbarVisuals.ApplyFlatToolbarChrome(button);
        button.Checked += (_, _) => SetFilter(text, true);
        button.Unchecked += (_, _) => SetFilter(text, false);
        return button;
    }

    private void SetFilter(string text, bool value)
    {
        switch (text)
        {
            case "Errors":
                _properties.Set("ShowErrors", value);
                break;
            case "Warnings":
                _properties.Set("ShowWarnings", value);
                break;
            case "Messages":
                _properties.Set("ShowMessages", value);
                break;
        }

        InternalShowResults();
    }

    private Grid CreateHeader()
    {
        var grid = CreateRowGrid();
        grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gainsboro);
        grid.Children.Add(CreateCell("Type", 0, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        grid.Children.Add(CreateCell("Description", 1, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        grid.Children.Add(CreateCell("File", 2, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        grid.Children.Add(CreateCell("Line", 3, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        grid.Children.Add(CreateCell("Column", 4, fontWeight: Microsoft.UI.Text.FontWeights.SemiBold));
        return grid;
    }

    private static DataTemplate CreateItemTemplate()
    {
        return new DataTemplate(() =>
        {
            var grid = CreateRowGrid();
            grid.Children.Add(CreateTaskTypeCell());
            grid.Children.Add(CreateBoundCell("Description", 1));
            grid.Children.Add(CreateBoundCell("File", 2));
            grid.Children.Add(CreateBoundCell("Line", 3));
            grid.Children.Add(CreateBoundCell("Column", 4));
            return grid;
        });
    }

    private static Grid CreateRowGrid()
    {
        var grid = new Grid
        {
            MinHeight = 28,
            ColumnSpacing = 8,
            Padding = new Thickness(6, 2, 6, 2)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        return grid;
    }

    private static TextBlock CreateCell(string text, int column, Windows.UI.Text.FontWeight fontWeight = default)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = fontWeight
        };
        Grid.SetColumn(textBlock, column);
        return textBlock;
    }

    private static TextBlock CreateBoundCell(string path, int column)
    {
        var textBlock = CreateCell(string.Empty, column);
        textBlock.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Path = new PropertyPath(path) });
        return textBlock;
    }

    private static FrameworkElement CreateTaskTypeCell()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        var image = new Image
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        image.SetBinding(Image.SourceProperty, new Binding
        {
            Path = new PropertyPath("TaskType"),
            Converter = new TaskTypeIconConverter()
        });
        var text = CreateBoundCell("TaskType", 0);
        Grid.SetColumn(text, 0);
        panel.Children.Add(image);
        panel.Children.Add(text);
        Grid.SetColumn(panel, 0);
        return panel;
    }

    private static StackPanel CreateIconTextContent(string iconName, string text)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                ToolbarVisuals.CreateToolbarIcon(iconName),
                new TextBlock
                {
                    Text = text,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private static Image CreateIcon(string iconName)
    {
        return new Image
        {
            Width = 16,
            Height = 16,
            Source = CreateIconSource(iconName)
        };
    }

    private static SvgImageSource CreateIconSource(string iconName)
    {
        return new SvgImageSource(new Uri($"ms-appx:///Icons/{iconName}.svg"));
    }

    private static string GetIconName(string text)
    {
        return text switch
        {
            "Errors" => "StatusInvalid_16x",
            "Warnings" => "StatusWarning_16x",
            "Messages" => "StatusInformation_16x",
            _ => "StatusInformation_16x"
        };
    }

    private static string GetIconName(UnoTaskType taskType)
    {
        return taskType switch
        {
            UnoTaskType.Error => "StatusInvalid_16x",
            UnoTaskType.Warning => "StatusWarning_16x",
            _ => "StatusInformation_16x"
        };
    }

    private void ErrorViewDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_errorView.SelectedItem is not UnoTask task || string.IsNullOrEmpty(task.FileName))
        {
            return;
        }

        ItemActivated?.Invoke(task.FileName, task.Line, task.Column);
        e.Handled = true;
    }

    private void BuildContextMenu()
    {
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "Copy" };
        copy.Click += (_, _) => CopySelectionToClipboard();
        var selectAll = new MenuFlyoutItem { Text = "Select All" };
        selectAll.Click += (_, _) => SelectAllItems();
        menu.Items.Add(copy);
        menu.Items.Add(selectAll);
        _errorView.ContextFlyout = menu;
    }

    private void SelectAllItems()
    {
        _errorView.SelectedItems.Clear();
        foreach (var item in _errors)
        {
            _errorView.SelectedItems.Add(item);
        }
    }

    private void CopySelectionToClipboard()
    {
        var selected = _errorView.SelectedItems.OfType<UnoTask>().ToArray();
        if (selected.Length == 0 && _errorView.SelectedItem is UnoTask item)
        {
            selected = new[] { item };
        }

        if (selected.Length == 0)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, selected.Select(FormatTaskForClipboard));
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static string FormatTaskForClipboard(UnoTask task)
    {
        return string.Join("\t", new[]
        {
            task.TaskType.ToString(),
            task.Description,
            task.File ?? string.Empty,
            task.Line.ToString(System.Globalization.CultureInfo.InvariantCulture),
            task.Column.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private void TaskServiceCleared(object? sender, EventArgs e)
    {
        if (!_taskService.InUpdate)
        {
            _errors.Clear();
        }
    }

    private void TaskServiceAdded(object? sender, UnoTaskEventArgs e)
    {
        if (!_taskService.InUpdate)
        {
            AddTask(e.Task);
        }
    }

    private void TaskServiceRemoved(object? sender, UnoTaskEventArgs e)
    {
        if (!_taskService.InUpdate)
        {
            _errors.Remove(e.Task);
        }
    }

    private void TaskServiceInUpdateChanged(object? sender, EventArgs e)
    {
        if (!_taskService.InUpdate)
        {
            InternalShowResults();
        }
    }

    private void InternalShowResults()
    {
        _errors.Clear();
        foreach (var task in _taskService.Tasks)
        {
            AddTask(task);
        }
    }

    private void AddTask(UnoTask task)
    {
        switch (task.TaskType)
        {
            case UnoTaskType.Warning when !ShowWarnings:
            case UnoTaskType.Error when !ShowErrors:
            case UnoTaskType.Message when !ShowMessages:
                return;
            case UnoTaskType.Comment:
                return;
        }

        _errors.Add(task);
    }

    private sealed class TaskTypeIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is UnoTaskType taskType
                ? CreateIconSource(GetIconName(taskType))
                : CreateIconSource("StatusInformation_16x");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
