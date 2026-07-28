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
			return ICSharpCode.SharpDevelop.Project.NamespaceNameGenerator.GenerateValidClassOrNamespaceName(name, allowDot);
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
