using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.Project;

public class ProjectLoadInformation
{
    public ProjectLoadInformation(ISolution solution, FileName fileName)
    {
        Solution = solution;
        FileName = fileName;
    }

    public ISolution Solution { get; }

    public FileName FileName { get; }
}
