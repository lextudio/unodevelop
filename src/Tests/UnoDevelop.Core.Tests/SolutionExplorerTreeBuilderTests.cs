using System;
using System.IO;
using NUnit.Framework;
using UnoDevelop.Services;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class SolutionExplorerTreeBuilderTests
{
    [Test]
    public void ResolveBestSolutionPathPrefersRootSlnx()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnoDevelopTreeBuilderTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "src"));

            var rootSlnx = Path.Combine(root, "UnoDevelop.slnx");
            var srcSln = Path.Combine(root, "src", "UnoDevelop.sln");
            File.WriteAllText(rootSlnx, string.Empty);
            File.WriteAllText(srcSln, string.Empty);

            Assert.That(SolutionExplorerTreeBuilder.ResolveBestSolutionPath(root), Is.EqualTo(rootSlnx));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
