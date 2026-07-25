// Stub for ErrorListPad used by AddIn projects (XmlEditor) that reference
// Base but not the full SharpDevelop app. When building SharpDevelop.csproj,
// the real UnoDevelop.Workbench.ErrorListPad is used instead.
namespace ICSharpCode.SharpDevelop.Gui
{
	public static class _ErrorListPadStub
	{
		public static bool ShowAfterBuild => false;
	}
}

// XmlView.cs references ErrorListPad directly. Make it resolve to our stub.
namespace ICSharpCode.SharpDevelop.Gui
{
	public static class ErrorListPad
	{
		public static bool ShowAfterBuild => false;
	}
}
