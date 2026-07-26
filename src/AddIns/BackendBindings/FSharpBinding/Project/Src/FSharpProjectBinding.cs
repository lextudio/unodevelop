using ICSharpCode.SharpDevelop.Project;

namespace FSharpBinding;

public sealed class FSharpProjectBinding : IProjectBinding
{
    public IProject LoadProject(ProjectLoadInformation info)
    {
        return new FSharpProject(info);
    }

    public IProject CreateProject(ProjectCreateInformation info)
    {
        return new FSharpProject(info);
    }

    public bool HandlingMissingProject => false;
}
