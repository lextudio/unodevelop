using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.Project
{
	sealed class ProjectChangeWatcher : IProjectChangeWatcher
	{
		public ProjectChangeWatcher(FileName fileName) { }
		public void Dispose() { }
		public void Disable() { }
		public void Enable() { }
		public void Rename(string newFileName) { }
	}
}
