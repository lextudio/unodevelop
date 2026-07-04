using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Project;

namespace UnoDevelop.Conditions;

internal sealed class SolutionOpenConditionEvaluator : IConditionEvaluator
{
    public bool IsValid(object? caller, Condition condition)
    {
        var projectService = ServiceSingleton.GetRequiredService<IProjectService>();
        return projectService.CurrentSolution?.Projects?.Count > 0;
    }
}
