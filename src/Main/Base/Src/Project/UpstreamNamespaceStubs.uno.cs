using System;

namespace ICSharpCode.SharpDevelop.Project.PortableLibrary
{
	internal static class UnoProjectPortableLibraryNamespaceMarker
	{
	}
}

namespace ICSharpCode.SharpDevelop.Debugging
{
	internal static class UnoDebuggingNamespaceMarker
	{
	}
}

namespace ICSharpCode.SharpDevelop.Gui
{
	public static class NewFileDialog
	{
		public static string GenerateValidClassOrNamespaceName(string name, bool allowDot)
		{
			if (string.IsNullOrWhiteSpace(name))
				return "Generated";
			var chars = name.ToCharArray();
			for (var i = 0; i < chars.Length; i++)
			{
				var ch = chars[i];
				if (!(char.IsLetterOrDigit(ch) || ch == '_' || (allowDot && ch == '.')))
					chars[i] = '_';
			}
			if (!char.IsLetter(chars[0]) && chars[0] != '_')
				return "_" + new string(chars);
			return new string(chars);
		}
	}
}

namespace ICSharpCode.SharpDevelop.Gui.OptionPanels
{
	internal static class AmbienceService
	{
		public static ICSharpCode.Core.Properties CodeGenerationProperties { get; } = new();
	}
}

namespace ICSharpCode.SharpDevelop.Parser
{
	public sealed class ProjectContentContainer : IDisposable
	{
		object projectContent;

		public ProjectContentContainer(object project, object initialProjectContent)
		{
			projectContent = initialProjectContent;
		}

		public ICSharpCode.TypeSystem.IProjectContent ProjectContent => projectContent as ICSharpCode.TypeSystem.IProjectContent;

		public void SetCompilerSettings(object settings) { }
		public void SetAssemblyName(object name) { }
		public void SetLocation(string location) { }
		public void ReparseReferences() { }
		public void ReparseCode() { }
		public void ParseInformationUpdated(object oldFile, object newFile) { }
		public void Dispose() { }
	}
}

namespace ICSharpCode.SharpDevelop.Refactoring
{
	public interface ISymbolSearch
	{
	}
}


