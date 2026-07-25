using System;
using System.Collections.Generic;
using ICSharpCode.SharpDevelop.Project;

namespace ICSharpCode.SharpDevelop
{
	public static class ProjectService
	{
		public static IProject? CurrentProject => null;
		public static ISolution? OpenSolution => null;
		public static IEnumerable<IProject> Projects => Array.Empty<IProject>();
		public static event EventHandler<ProjectItemEventArgs>? ProjectItemRemoved;
		public static event EventHandler<SolutionEventArgs>? SolutionClosed;
		public static event EventHandler<SolutionEventArgs>? SolutionOpened;
		public static event EventHandler<SolutionEventArgs>? SolutionLoaded;
	}
}
