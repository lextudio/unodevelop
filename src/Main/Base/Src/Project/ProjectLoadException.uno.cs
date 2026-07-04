using System;

namespace ICSharpCode.SharpDevelop.Project;

public class ProjectLoadException : Exception
{
    public ProjectLoadException() { }
    public ProjectLoadException(string message) : base(message) { }
    public ProjectLoadException(string message, Exception innerException) : base(message, innerException) { }
    public virtual bool CanShowDialog => false;
    public virtual void ShowDialog() { }
}

public class ToolNotFoundProjectLoadException : ProjectLoadException
{
    public string Description { get; set; } = string.Empty;
    public string LinkTarget { get; set; } = string.Empty;
    public ToolNotFoundProjectLoadException() { }
    public ToolNotFoundProjectLoadException(string message) : base(message) { }
    public ToolNotFoundProjectLoadException(string message, Exception innerException) : base(message, innerException) { }
    public override bool CanShowDialog => true;
}
