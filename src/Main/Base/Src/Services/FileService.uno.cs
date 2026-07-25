using System;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop
{
	public static class FileService
	{
		public static void OpenFile(string fileName) { }
		public static void JumpToFilePosition(string fileName, int line, int column) { }
		public static IViewContent NewFile(string fileName, string content) => null!;
		public static IViewContent NewFile(string fileName) => NewFile(fileName, string.Empty);

		public static event EventHandler<FileEventArgs>? FileCreated;
		public static event EventHandler<FileEventArgs>? FileCopied;
		public static event EventHandler<FileEventArgs>? FileRemoved;
		public static event EventHandler<FileRenameEventArgs>? FileRenamed;
	}
}

namespace ICSharpCode.SharpDevelop
{
	public class FileEventArgs : EventArgs
	{
		public FileName FileName => null!;
	}
}
