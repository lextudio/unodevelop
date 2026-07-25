using System;
using System.IO;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.IconEditor
{
	public class EditorPanel
	{
		public event EventHandler? IconWasEdited;
		public void ShowFile(IconFile file) { }
		public void SaveIcon(Stream stream) { }
	}
}
