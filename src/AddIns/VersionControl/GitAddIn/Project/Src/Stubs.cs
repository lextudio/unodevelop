using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop
{
	public class ProcessRunner : IDisposable
	{
		public string WorkingDirectory { get; set; } = "";
		public string? Arguments { get; set; }
		public string? CommandFileName { get; set; }
		public string CommandLine { get; set; } = "";
		public bool RedirectStandardOutput { get; set; }
		public bool RedirectStandardError { get; set; }
		public Stream? StandardOutput { get; set; }
		public event EventHandler? OutputLineReceived;
		public event EventHandler? ErrorLineReceived;
		public int ExitCode { get; private set; }

		public void Start() { }
		public void Start(string cmd, string args) { }
		public void Start(string cmd, string args, string workDir) { }
		public void Start(string cmd, string args, string workDir, string input) { }
		public void Kill() { }
		public void WaitForExit() { }
		public void Dispose() { }
		public StreamReader OpenStandardOutputReader() => new StreamReader(Stream.Null);
		public StreamReader OpenStandardErrorReader() => new StreamReader(Stream.Null);

		public static int RunCommand(string command, string arguments, string workingDirectory) => 0;
		public static string RunCommandWithOutput(string command, string arguments, string workingDirectory) => "";
		public async Task<string> RunCommandWithOutputAsync(string command, string arguments, string workingDirectory) => await Task.FromResult("");

		public async Task<int> RunInOutputPadAsync(object category, string command, string[] args) => await Task.FromResult(0);
	}
}

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

