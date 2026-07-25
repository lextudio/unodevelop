using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoDevelop.Services;

namespace UnoDevelop.Commands;

internal sealed class ViewProjectOptionsCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        var project = projectService.CurrentProject;
        if (project is null)
        {
            MessageService.ShowError("No project is selected.");
            return;
        }
        ShowProjectOptions(project);
    }

    public static void ShowProjectOptions(IProject project)
    {
        var projectPath = project.FileName?.ToString();
        if (projectPath is null) return;

        var dialog = new ProjectOptionsWindow(projectPath);
        var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
        workbench.ShowView(new ProjectOptionsViewContent(dialog));
    }
}

internal sealed class ProjectOptionsViewContent : IViewContent
{
    private readonly ProjectOptionsWindow _window;

    public ProjectOptionsViewContent(ProjectOptionsWindow window)
    {
        _window = window;
        TabPageText = "Project Options";
    }

    public object? Control => _window;
    public object? InitiallyFocusedControl => _window;
    public IWorkbenchWindow? WorkbenchWindow { get; set; }
    public string TabPageText { get; }
    public string TitleName => TabPageText;
    public string InfoTip => TabPageText;
    public FileName? PrimaryFileName => null;
    public OpenedFile? PrimaryFile => null;
    public IList<OpenedFile> Files => Array.Empty<OpenedFile>();
    public bool IsReadOnly => false;
    public bool IsViewOnly => false;
    public bool CloseWithSolution => true;
    public bool IsDisposed { get; private set; }
    public bool IsDirty { get; set; }
    public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();
    public event EventHandler? TabPageTextChanged;
    public event EventHandler? TitleNameChanged;
    public event EventHandler? InfoTipChanged;
    public event EventHandler? IsDirtyChanged;
    public event EventHandler? Disposed;

    public INavigationPoint BuildNavPoint() => null!;
    public void Save(OpenedFile file, Stream stream) { }
    public void Load(OpenedFile file, Stream stream) { }
    public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;
    public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;
    public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
    public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }
    public void Dispose() { IsDisposed = true; Disposed?.Invoke(this, EventArgs.Empty); }
    public object? GetService(Type serviceType) => null;
}

internal sealed class ProjectOptionsWindow : Grid
{
    private readonly string _projectPath;
    private readonly TextBox _tfmTextBox;
    private readonly TextBlock _currentTfmLabel;

    public ProjectOptionsWindow(string projectPath)
    {
        _projectPath = projectPath;

        var currentTfm = MsBuildProjectHelper.GetTargetFramework(projectPath) ?? "(not set)";

        var header = new TextBlock
        {
            Text = "Project Properties",
            FontSize = 20,
            FontWeight = new Windows.UI.Text.FontWeight(600),
            Margin = new Thickness(8),
        };

        var tfmLabel = new TextBlock
        {
            Text = "Target Framework:",
            Margin = new Thickness(8, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _currentTfmLabel = new TextBlock
        {
            Text = currentTfm,
            Margin = new Thickness(8, 0, 0, 4),
            FontWeight = new Windows.UI.Text.FontWeight(600),
        };

        _tfmTextBox = new TextBox
        {
            Text = currentTfm,
            Margin = new Thickness(8),
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var saveButton = new Button
        {
            Content = "Save",
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        saveButton.Click += OnSaveClick;

        var openButton = new Button
        {
            Content = "Open .csproj",
            Margin = new Thickness(8, 8, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        openButton.Click += (_, _) => FileService.OpenFile(projectPath);

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children = { header, tfmLabel, _currentTfmLabel, new TextBlock { Text = "New value:", Margin = new Thickness(8, 8, 0, 0) }, _tfmTextBox, new StackPanel { Orientation = Orientation.Horizontal, Children = { saveButton, openButton } } }
        };

        var scroll = new ScrollViewer { Content = stack };
        Children.Add(scroll);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var newTfm = _tfmTextBox.Text.Trim();
        if (string.IsNullOrEmpty(newTfm)) return;

        if (MsBuildProjectHelper.SetTargetFramework(_projectPath, newTfm))
        {
            _currentTfmLabel.Text = newTfm;
            IsDirty = false;
        }
    }

    public bool IsDirty { get; set; }
}
