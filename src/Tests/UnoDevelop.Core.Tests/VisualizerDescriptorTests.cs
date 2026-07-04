using NUnit.Framework;
using UnoDevelop.Debugger.Visualizers;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class VisualizerDescriptorTests
{
    [Test]
    public void TextVisualizer_AcceptsString()
    {
        var d = new TextVisualizerDescriptor();
        Assert.That(d.IsVisualizerAvailable("string"), Is.True);
    }

    [Test]
    public void TextVisualizer_AcceptsSystemString()
    {
        var d = new TextVisualizerDescriptor();
        Assert.That(d.IsVisualizerAvailable("System.String"), Is.True);
    }

    [Test]
    public void TextVisualizer_RejectsOtherTypes()
    {
        var d = new TextVisualizerDescriptor();
        Assert.That(d.IsVisualizerAvailable("int"), Is.False);
        Assert.That(d.IsVisualizerAvailable("bool"), Is.False);
        Assert.That(d.IsVisualizerAvailable(typeof(System.Uri).FullName), Is.False);
    }

    [TestCase("int[]")]
    [TestCase("string[,]")]
    [TestCase("byte[,,]")]
    [TestCase("List<int>")]
    [TestCase("Dictionary<string,int>")]
    [TestCase("System.Collections.Generic.List`1")]
    [TestCase("System.Array")]
    [TestCase("ArrayList")]
    [TestCase("List")]
    [TestCase("Dictionary")]
    [TestCase("HashSet")]
    [TestCase("SortedList")]
    [TestCase("SortedDictionary")]
    [TestCase("Queue")]
    [TestCase("Stack")]
    [TestCase("LinkedList")]
    [TestCase("Collection")]
    [TestCase("ObservableCollection")]
    [TestCase("ReadOnlyCollection")]
    [TestCase("IEnumerable")]
    [TestCase("ICollection")]
    [TestCase("IList")]
    [TestCase("IDictionary")]
    [TestCase("System.Collections.ArrayList")]
    [TestCase("System.Collections.Generic.List`1[[System.Int32]]")]
    [TestCase("System.Linq.Enumerable")]
    public void GridVisualizer_AcceptsCollectionTypes(string typeName)
    {
        var d = new GridVisualizerDescriptor();
        Assert.That(d.IsVisualizerAvailable(typeName), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("int")]
    [TestCase("string")]
    [TestCase("System.Uri")]
    [TestCase("MyClass")]
    [TestCase("SomeRandomType")]
    public void GridVisualizer_RejectsNonCollectionTypes(string? typeName)
    {
        var d = new GridVisualizerDescriptor();
        Assert.That(d.IsVisualizerAvailable(typeName), Is.False);
    }

    [Test]
    public void ObjectGraphVisualizer_AcceptsAnyType()
    {
        var d = new ObjectGraphVisualizerDescriptor();
        Assert.That(d.IsVisualizerAvailable("int"), Is.True);
        Assert.That(d.IsVisualizerAvailable("string"), Is.True);
        Assert.That(d.IsVisualizerAvailable("System.Uri"), Is.True);
        Assert.That(d.IsVisualizerAvailable("MyClass"), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    public void ObjectGraphVisualizer_RejectsNullOrEmpty(string? typeName)
    {
        var d = new ObjectGraphVisualizerDescriptor();
        Assert.That(d.IsVisualizerAvailable(typeName), Is.False);
    }
}
