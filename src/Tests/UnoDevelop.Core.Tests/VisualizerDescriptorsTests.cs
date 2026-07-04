using System.Linq;
using NUnit.Framework;
using UnoDevelop.Debugger;
using UnoDevelop.Debugger.Visualizers;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class VisualizerDescriptorsTests
{
    [Test]
    public void GetAll_ReturnsThreeBuiltInDescriptors()
    {
        var all = VisualizerDescriptors.GetAll();
        Assert.That(all.Count, Is.EqualTo(3));
    }

    [Test]
    public void GetAll_ReturnsSameInstanceOnRepeatedCalls()
    {
        var first = VisualizerDescriptors.GetAll();
        var second = VisualizerDescriptors.GetAll();
        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetAll_ContainsTextVisualizer()
    {
        var all = VisualizerDescriptors.GetAll();
        Assert.That(all.Any(d => d is TextVisualizerDescriptor), Is.True);
    }

    [Test]
    public void GetAll_ContainsGridVisualizer()
    {
        var all = VisualizerDescriptors.GetAll();
        Assert.That(all.Any(d => d is GridVisualizerDescriptor), Is.True);
    }

    [Test]
    public void GetAll_ContainsObjectGraphVisualizer()
    {
        var all = VisualizerDescriptors.GetAll();
        Assert.That(all.Any(d => d is ObjectGraphVisualizerDescriptor), Is.True);
    }

    [Test]
    public void Register_AddsToExistingList()
    {
        var countBefore = VisualizerDescriptors.GetAll().Count;

        var myDescriptor = new TestDescriptor();
        VisualizerDescriptors.Register(myDescriptor);

        try
        {
            var all = VisualizerDescriptors.GetAll();
            Assert.That(all.Count, Is.EqualTo(countBefore + 1));
            Assert.That(all.Contains(myDescriptor), Is.True);
        }
        finally
        {
            // Clean up: remove the test descriptor via reflection (no public Unregister API)
            var field = typeof(VisualizerDescriptors)
                .GetField("_descriptors", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(null) is System.Collections.Generic.List<IVisualizerDescriptor> list)
                list.Remove(myDescriptor);
        }
    }

    private sealed class TestDescriptor : IVisualizerDescriptor
    {
        public bool IsVisualizerAvailable(string typeName) => false;
        public ICSharpCode.SharpDevelop.Debugging.IVisualizerCommand CreateVisualizerCommand(
            VariableInfo variable,
            System.Func<VariableInfo?> reevaluate)
            => new TestCommand();

        private sealed class TestCommand : ICSharpCode.SharpDevelop.Debugging.IVisualizerCommand
        {
            public void Execute() { }
        }
    }
}
