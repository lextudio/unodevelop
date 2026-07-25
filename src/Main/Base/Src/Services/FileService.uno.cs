using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop
{
	public static class FileService
	{
		public static void OpenFile(string fileName) { }
		public static void JumpToFilePosition(string fileName, int line, int column) { }
		public static IViewContent NewFile(string fileName, string content) => null!;
		public static IViewContent NewFile(string fileName) => NewFile(fileName, string.Empty);
	}
}
