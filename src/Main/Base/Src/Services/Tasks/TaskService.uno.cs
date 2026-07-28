using System;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop.Gui
{
	public enum TaskType
	{
		Error,
		Warning,
		Message
	}

	public class SDTask
	{
		public SDTask(FileName fileName, string message, int column, int line, TaskType taskType)
		{
			FileName = fileName;
			Message = message;
			Column = column;
			Line = line;
			TaskType = taskType;
		}

		public FileName FileName { get; }
		public string Message { get; }
		public int Column { get; }
		public int Line { get; }
		public TaskType TaskType { get; }
	}

	// Base can't reference the App-layer UnoTaskService directly (UnoDevelop.Services lives in
	// SharpDevelop.csproj, which references THIS project, not the other way around) - this
	// interface is the seam, implemented by an adapter registered in ServiceBootstrapper.cs, same
	// pattern as IOutputPad/UnoOutputPadService.
	public interface ITaskListService
	{
		void Add(SDTask task);
		void ClearExceptCommentTasks();
		bool SomethingWentWrong { get; }
		bool HasCriticalErrors(bool treatWarningsAsErrors);
	}

	// Was a no-op placeholder (Add/ClearExceptCommentTasks did nothing, SomethingWentWrong
	// hardcoded false) - now forwards to whatever ITaskListService the host registered, so a
	// caller of this OpenDevelop-shaped static API (e.g. the classic ICSharpCode.UnitTesting
	// backend's UnitTestTaskService) actually shows up in the real Errors/Tasks pad.
	public static class TaskService
	{
		static ITaskListService? Service
			=> ServiceSingleton.ServiceProvider.GetService(typeof(ITaskListService)) as ITaskListService;

		public static void Add(SDTask task) => Service?.Add(task);

		public static void ClearExceptCommentTasks() => Service?.ClearExceptCommentTasks();

		public static bool SomethingWentWrong => Service?.SomethingWentWrong ?? false;

		public static bool HasCriticalErrors(bool treatWarningsAsErrors)
			=> Service?.HasCriticalErrors(treatWarningsAsErrors) ?? false;

		public static IOutputCategory? BuildMessageViewCategory
		{
			get
			{
				var outputPad = ServiceSingleton.ServiceProvider.GetService(typeof(IOutputPad)) as IOutputPad;
				return outputPad?.GetOrCreateCategory("Build");
			}
		}
	}
}
