using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ICSharpCode.Core;

// ProcessRunner's own fake stub is gone - the real, functional ICSharpCode.SharpDevelop.ProcessRunner
// (Main/Base/Project/Util/ProcessRunner.cs, already linked into ICSharpCode.SharpDevelop.csproj) has
// a compatible API surface, since Git.cs/GitVersionProvider.cs (now linked verbatim from OpenDevelop,
// see GitAddIn.csproj) were always written against the real class. The fake no-op version silently
// meant every git command this AddIn ran (add/remove-on-file-change, blame-based document versioning,
// GUI commit/diff/log launch) was inert - `Start()` did nothing and `OpenStandardOutputReader()`
// returned an empty stream - so removing it is a real functional fix, not just cleanup.

namespace ICSharpCode.SharpDevelop.Editor
{
	public interface IDocumentVersionProvider { }
	public class RepoChangeWatcher
	{
		public static RepoChangeWatcher AddWatch(string path, Action handler) => new RepoChangeWatcher();
		public void ReleaseWatch(string path) { }
		public void ReleaseWatch(Action handler) { }
	}
}

namespace ICSharpCode.GitAddIn
{
	public static class OverlayIconManager
	{
		public static object? Provider => null;
		public static void Invalidate(string path) { }
	}
}

namespace ICSharpCode.SharpDevelop
{
	public interface IProjectBrowserOverlayService
	{
		void RegisterProvider(object provider);
	}
}

public static class TaskExtensions
{
	public static void FireAndForget(this Task task) { }
}

