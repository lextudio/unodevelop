using ICSharpCode.SharpDevelop.Gui;

namespace UnoDevelop.Services;

// Adapter satisfying Base's ITaskListService seam (TaskService.uno.cs) over the real UnoTaskService
// - see doc/technotes/unit-testing.md. Base can't reference UnoTaskService directly (layering:
// SharpDevelop.csproj references ICSharpCode.SharpDevelop.csproj, not the reverse), so this lives
// here instead, registered in ServiceBootstrapper.cs.
internal sealed class UnoTaskListService : ITaskListService
{
    private readonly UnoTaskService _tasks;

    public UnoTaskListService(UnoTaskService tasks) => _tasks = tasks;

    public void Add(SDTask task)
        => _tasks.Add(new UnoTask(task.FileName?.ToString(), task.Message, task.Column, task.Line, Map(task.TaskType)));

    public void ClearExceptCommentTasks() => _tasks.ClearExceptCommentTasks();

    public bool SomethingWentWrong => _tasks.GetCount(UnoTaskType.Error) > 0;

    public bool HasCriticalErrors(bool treatWarningsAsErrors)
        => _tasks.GetCount(UnoTaskType.Error) > 0
            || (treatWarningsAsErrors && _tasks.GetCount(UnoTaskType.Warning) > 0);

    private static UnoTaskType Map(TaskType taskType) => taskType switch
    {
        TaskType.Error => UnoTaskType.Error,
        TaskType.Warning => UnoTaskType.Warning,
        _ => UnoTaskType.Message,
    };
}
