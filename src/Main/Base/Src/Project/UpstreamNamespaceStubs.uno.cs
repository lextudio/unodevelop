using System;

namespace ICSharpCode.SharpDevelop.Project.Converter
{
	public class CompilerVersion
	{
		public CompilerVersion(Version msbuildVersion, string displayName)
		{
			MSBuildVersion = msbuildVersion;
			DisplayName = displayName;
		}

		public Version MSBuildVersion { get; }

		public string DisplayName { get; }
	}

	internal static class UnoProjectConverterNamespaceMarker
	{
	}
}

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

namespace ICSharpCode.SharpDevelop.Gui.OptionPanels
{
	public static class NewFileDialog
	{
		public static string GenerateValidClassOrNamespaceName(string name, bool allowDot)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return "Generated";
			}

			var chars = name.ToCharArray();
			for (var i = 0; i < chars.Length; i++)
			{
				var ch = chars[i];
				if (!(char.IsLetterOrDigit(ch) || ch == '_' || (allowDot && ch == '.')))
				{
					chars[i] = '_';
				}
			}

			if (!char.IsLetter(chars[0]) && chars[0] != '_')
			{
				return "_" + new string(chars);
			}

			return new string(chars);
		}
	}

	internal static class AmbienceService
	{
		public static ICSharpCode.Core.Properties CodeGenerationProperties { get; } = new();
	}
}

namespace ICSharpCode.SharpDevelop.Dom
{
	public interface IAssemblyModel
	{
	}

	public sealed class EmptyAssemblyModel : IAssemblyModel
	{
		public static readonly EmptyAssemblyModel Instance = new EmptyAssemblyModel();
		private EmptyAssemblyModel() { }
	}
}

namespace ICSharpCode.SharpDevelop.Refactoring
{
	public interface ISymbolSearch
	{
	}
}

namespace ICSharpCode.SharpDevelop.Parser
{
	public class ParseInformationEventArgs : EventArgs
	{
	}
}

namespace ICSharpCode.SharpDevelop.Project
{
	public class ReferenceProjectItem : ProjectItem
	{
		public ReferenceProjectItem(IProject project)
			: base(project, ItemType.Reference)
		{
		}

		public ReferenceProjectItem(IProject project, string include)
			: base(project, ItemType.Reference, include)
		{
		}
	}
}
