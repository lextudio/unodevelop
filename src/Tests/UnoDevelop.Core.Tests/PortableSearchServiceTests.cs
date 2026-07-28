#nullable enable

using System.IO;
using System.Linq;
using System;
using ICSharpCode.SearchAndReplace.Portable;
using NUnit.Framework;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class PortableSearchServiceTests
{
    [Test]
    public void CreateReplacePlan_DoesNotWriteFiles()
    {
        var directory = CreateTempDirectory();
        var file = Path.Combine(directory, "sample.cs");
        File.WriteAllText(file, "alpha beta alpha");

        var service = new PortableSearchService();
        var options = new PortableSearchOptions("alpha", "gamma", directory, "*.cs", MatchCase: true, UseRegex: false, IncludeSubdirectories: true);
        var search = service.FindAll(options);

        var plan = service.CreateReplacePlan(search.Results, options);

        Assert.That(plan.MatchCount, Is.EqualTo(2));
        Assert.That(plan.ChangedFileCount, Is.EqualTo(1));
        Assert.That(File.ReadAllText(file), Is.EqualTo("alpha beta alpha"));
    }

    [Test]
    public void ApplyReplacePlan_WritesPlannedChanges()
    {
        var directory = CreateTempDirectory();
        var file = Path.Combine(directory, "sample.cs");
        File.WriteAllText(file, "alpha beta alpha");

        var service = new PortableSearchService();
        var options = new PortableSearchOptions("alpha", "gamma", directory, "*.cs", MatchCase: true, UseRegex: false, IncludeSubdirectories: true);
        var search = service.FindAll(options);
        var plan = service.CreateReplacePlan(search.Results, options);

        var result = service.ApplyReplacePlan(plan);

        Assert.That(result.ChangedFileCount, Is.EqualTo(1));
        Assert.That(result.ChangedFilePaths, Is.EqualTo(new[] { file }));
        Assert.That(File.ReadAllText(file), Is.EqualTo("gamma beta gamma"));
    }

    [Test]
    public void Group_PerFile_ReturnsOneGroupPerFile()
    {
        var results = new[]
        {
            new PortableSearchResult("/repo/a/file1.cs", 1, 1, 0, 5, "alpha"),
            new PortableSearchResult("/repo/a/file1.cs", 2, 1, 6, 5, "alpha"),
            new PortableSearchResult("/repo/b/file2.cs", 1, 1, 0, 5, "alpha")
        };

        var groups = new PortableSearchResultGrouper().Group(results, PortableSearchResultGroupingKind.PerFile);

        Assert.That(groups.Select(group => group.Title).ToArray(), Is.EqualTo(new[] { "/repo/a/file1.cs", "/repo/b/file2.cs" }));
        Assert.That(groups.Select(group => group.OccurrenceCount).ToArray(), Is.EqualTo(new[] { 2, 1 }));
    }

    [Test]
    public void Group_PerProjectAndFile_UsesProvidedProjectNames()
    {
        var results = new[]
        {
            new PortableSearchResult("/repo/a/file1.cs", 1, 1, 0, 5, "alpha"),
            new PortableSearchResult("/repo/a/file2.cs", 1, 1, 0, 5, "alpha"),
            new PortableSearchResult("/repo/b/file3.cs", 1, 1, 0, 5, "alpha")
        };

        var groups = new PortableSearchResultGrouper().Group(
            results,
            PortableSearchResultGroupingKind.PerProjectAndFile,
            file => file.Contains("/repo/a/") ? "ProjectA" : "ProjectB");

        Assert.That(groups.Select(group => group.Title).ToArray(), Is.EqualTo(new[] { "ProjectA", "ProjectB" }));
        Assert.That(groups[0].Children.Select(group => group.Title).ToArray(), Is.EqualTo(new[] { "/repo/a/file1.cs", "/repo/a/file2.cs" }));
        Assert.That(groups.Select(group => group.OccurrenceCount).ToArray(), Is.EqualTo(new[] { 2, 1 }));
    }

    [Test]
    public void FindAll_ReportsSearchedFileProgress()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "file1.cs"), "alpha");
        File.WriteAllText(Path.Combine(directory, "file2.cs"), "alpha");
        var reported = 0;

        var service = new PortableSearchService();
        var options = new PortableSearchOptions("alpha", "gamma", directory, "*.cs", MatchCase: true, UseRegex: false, IncludeSubdirectories: true);
        var result = service.FindAll(options, searchedFileProgress: new Progress<int>(count => reported = count));

        Assert.That(result.SearchedFileCount, Is.EqualTo(2));
        Assert.That(reported, Is.EqualTo(2));
    }

    static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "UnoDevelop.SearchAndReplace.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        return directory;
    }
}
