using System;
using System.Collections.Generic;
using System.Linq;

namespace UnoDevelop.Debugger.Visualizers;

/// <summary>
/// Registry of all registered <see cref="IVisualizerDescriptor"/> instances.
/// </summary>
public static class VisualizerDescriptors
{
    private static List<IVisualizerDescriptor>? _descriptors;

    public static IReadOnlyList<IVisualizerDescriptor> GetAll()
    {
        if (_descriptors is null)
        {
            _descriptors = new List<IVisualizerDescriptor>
            {
                new TextVisualizerDescriptor(),
                new GridVisualizerDescriptor(),
                new ObjectGraphVisualizerDescriptor(),
            };
        }
        return _descriptors;
    }

    /// <summary>Register a custom visualizer at runtime.</summary>
    public static void Register(IVisualizerDescriptor descriptor)
    {
        GetAll();
        _descriptors!.Add(descriptor);
    }
}
