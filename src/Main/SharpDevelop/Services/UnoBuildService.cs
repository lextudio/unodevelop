using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Services;

internal sealed class UnoBuildService : IBuildService
{
    private volatile bool _isBuilding;
    private CancellationTokenSource? _cts;

    public event EventHandler<BuildEventArgs>? BuildStarted;
    public event EventHandler<BuildEventArgs>? BuildFinished;

    public bool IsBuilding => _isBuilding;

    public void CancelBuild()
    {
        _cts?.Cancel();
    }

    public async Task<BuildResults> BuildAsync(IEnumerable<IProject> projects, BuildOptions options)
    {
        if (_isBuilding)
        {
            return new BuildResults { Result = BuildResultCode.MSBuildAlreadyRunning };
        }

        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(options);

        var buildables = projects.OfType<IBuildable>().ToArray();
        if (buildables.Length == 0)
        {
            return new BuildResults { Result = BuildResultCode.Error };
        }

        _isBuilding = true;
        _cts = new CancellationTokenSource();
        var progressMonitor = SD.StatusBar.CreateProgressMonitor(_cts.Token);

        try
        {
            var projectList = projects.ToArray();
            (ICSharpCode.Core.ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService)?.ClearExceptCommentTasks();
            BuildStarted?.Invoke(this, new BuildEventArgs(projectList, options));

            IBuildable buildable = buildables.Length == 1
                ? buildables[0]
                : new MultipleProjectBuildable(buildables);

            var results = await BuildEngine.BuildAsync(
                buildable,
                options,
                new UnoBuildFeedbackSink(
                    SD.GetService<IOutputPad>(),
                    ICSharpCode.Core.ServiceSingleton.ServiceProvider.GetService(typeof(UnoTaskService)) as UnoTaskService),
                progressMonitor);

            BuildFinished?.Invoke(this, new BuildEventArgs(projectList, options, results));
            return results;
        }
        finally
        {
            progressMonitor.Dispose();
            _cts.Dispose();
            _cts = null;
            _isBuilding = false;
        }
    }

    public Task<BuildResults> BuildAsync(IProject project, BuildOptions options)
        => project is null
            ? Task.FromResult(new BuildResults { Result = BuildResultCode.Error })
            : BuildAsync(new[] { project }, options);

    public Task<BuildResults> BuildAsync(ISolution solution, BuildOptions options)
        => solution is null
            ? Task.FromResult(new BuildResults { Result = BuildResultCode.Error })
            : BuildAsync(solution.Projects.CreateSnapshot(), options);

    public Task<BuildResults> BuildInBackgroundAsync(IBuildable buildable, BuildOptions options, IBuildFeedbackSink buildFeedbackSink, IProgressMonitor progressMonitor)
    {
        ArgumentNullException.ThrowIfNull(buildable);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(buildFeedbackSink);
        ArgumentNullException.ThrowIfNull(progressMonitor);
        return BuildEngine.BuildAsync(buildable, options, buildFeedbackSink, progressMonitor);
    }

    private sealed class UnoBuildFeedbackSink : IBuildFeedbackSink
    {
        private readonly IOutputCategory? _buildCategory;
        private readonly UnoTaskService? _taskService;

        public UnoBuildFeedbackSink(IOutputPad? outputPad, UnoTaskService? taskService)
        {
            _taskService = taskService;
            _buildCategory = outputPad?.BuildCategory;
            _buildCategory?.Activate(true);
        }

        public void ReportError(BuildError error)
        {
            _taskService?.Add(UnoTask.FromBuildError(error));
            _buildCategory?.AppendLine(error.ToRichText());
        }

        public void ReportMessage(ICSharpCode.AvalonEdit.Highlighting.RichText message)
        {
            _buildCategory?.AppendLine(message);
        }
    }
}
