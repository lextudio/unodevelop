using System;
using System.IO;
using ICSharpCode.SharpDevelop.Project;

namespace FSharpBinding;

public sealed class FSharpProject : CompilableProject
{
    public FSharpProject(ProjectLoadInformation info) : base(info)
    {
    }

    public FSharpProject(ProjectCreateInformation info) : base(info)
    {
    }

    public override string Language => "F#";

    protected override ProjectBehavior CreateDefaultBehavior()
    {
        return new FSharpProjectBehavior(this, base.CreateDefaultBehavior());
    }
}

public sealed class FSharpProjectBehavior : ProjectBehavior
{
    public FSharpProjectBehavior(FSharpProject project, ProjectBehavior? next = null)
        : base(project, next)
    {
    }

    public override ItemType GetDefaultItemType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.Equals(extension, ".fs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".fsi", StringComparison.OrdinalIgnoreCase))
        {
            return ItemType.Compile;
        }

        return base.GetDefaultItemType(fileName);
    }
}
