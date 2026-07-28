using System.IO;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop.Commands;

// Real (not placeholder) "save every dirty open file" - matches OpenDevelop's own
// SaveAllFiles.SaveAll(), needed by the classic ICSharpCode.UnitTesting backend's
// UnitTestSaveAllFilesCommand (TestExecutionManager.RunTestsAsync calls it before running tests).
// In practice a no-op today because AbstractViewContent.uno.cs's IsDirty is itself still a
// hardcoded stub (always false) - not something to fix in this pass; this is written the same way
// it'll behave once that's wired up, not as a fake/lying implementation.
public static class SaveAllFiles
{
    public static void SaveAll()
    {
        foreach (var content in SD.Workbench.ViewContentCollection)
        {
            foreach (var file in content.Files)
            {
                if (!file.IsDirty || file.FileName is null)
                    continue;

                using var stream = new FileStream(file.FileName.ToString(), FileMode.Create, FileAccess.Write);
                content.Save(file, stream);
            }
        }
    }
}
