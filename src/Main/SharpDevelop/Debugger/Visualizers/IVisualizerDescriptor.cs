using System;
using ICSharpCode.SharpDevelop.Debugging;

namespace UnoDevelop.Debugger.Visualizers;

/// <summary>
/// Creates visualizer commands and decides availability based on type name.
/// UnoDevelop port of SharpDevelop's IVisualizerDescriptor, using string
/// type names instead of IType to avoid binding debugger visualizers to a specific type-system implementation.
/// </summary>
public interface IVisualizerDescriptor
{
    bool IsVisualizerAvailable(string typeName);
    IVisualizerCommand CreateVisualizerCommand(VariableInfo variable, Func<VariableInfo?> reevaluate);
}
