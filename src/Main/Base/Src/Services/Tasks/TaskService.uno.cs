using System;
using ICSharpCode.Core;

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
		}
	}

	public static class TaskService
	{
		public static void Add(SDTask task)
		{
		}

		public static void ClearExceptCommentTasks()
		{
		}

		public static bool SomethingWentWrong => false;
	}
}
