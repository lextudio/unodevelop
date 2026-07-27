using System.Threading.Tasks;
using NUnit.Framework;
using ICSharpCode.UnitTesting.Simple;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class TestServiceBehaviorTests
{
    [Test]
    public void NewService_GetLastResults_ReturnsEmpty()
    {
        var service = new TestService();
        var results = service.GetLastResults();
        Assert.That(results, Is.Empty);
    }

    [Test]
    public void NewService_GetTests_ReturnsEmpty()
    {
        var service = new TestService();
        var tests = service.GetTests();
        Assert.That(tests, Is.Empty);
    }

    [Test]
    public void GetTests_WithProgressMonitor_ReturnsEmpty()
    {
        var service = new TestService();
        using var monitor = new ICSharpCode.SharpDevelop.DummyProgressMonitor();
        var tests = service.GetTests(monitor);
        Assert.That(tests, Is.Empty);
    }

    [Test]
    public void GetTests_ReturnsSameRef()
    {
        var service = new TestService();
        var first = service.GetTests();
        var second = service.GetTests();
        // Without a solution, both return Array.Empty<TestInfo>() singleton
        Assert.That(first, Is.SameAs(second));
    }

    [Test]
    public void GetTests_AfterRefresh_ReturnsEmpty()
    {
        var service = new TestService();
        _ = service.GetTests();
        service.RefreshTests();
        var tests = service.GetTests();
        Assert.That(tests, Is.Empty);
    }

    [Test]
    public void RefreshTests_DoesNotThrow()
    {
        var service = new TestService();
        Assert.DoesNotThrow(() => service.RefreshTests());
    }

    [Test]
    public void DoubleRefresh_DoesNotThrow()
    {
        var service = new TestService();
        Assert.DoesNotThrow(() =>
        {
            service.RefreshTests();
            service.RefreshTests();
        });
    }

    [Test]
    public async Task RunTestsAsync_EmptyList_NoOp()
    {
        var service = new TestService();
        Assert.That(service.IsRunning, Is.False);
        await service.RunTestsAsync([]);
        Assert.That(service.IsRunning, Is.False);
    }

    [Test]
    public void Stop_WhenNotRunning_DoesNotThrow()
    {
        var service = new TestService();
        Assert.DoesNotThrow(() => service.Stop());
    }

    [Test]
    public async Task DoubleStop_DoesNotThrow()
    {
        var service = new TestService();
        Assert.DoesNotThrow(() =>
        {
            service.Stop();
            service.Stop();
        });
        await Task.CompletedTask;
    }

    [Test]
    public void IsRunning_InitiallyFalse()
    {
        var service = new TestService();
        Assert.That(service.IsRunning, Is.False);
    }

    [Test]
    public void MultipleGetLastResultsCalls_ReturnIndependentCopies()
    {
        var service = new TestService();
        var first = service.GetLastResults();
        var second = service.GetLastResults();
        Assert.That(first, Is.Not.SameAs(second));
    }
}
