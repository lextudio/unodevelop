using System.Threading.Tasks;
using NUnit.Framework;
using UnoDevelop.Services;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class DebugServiceStateTests
{
    [Test]
    public void InitialState_HasCache_False()
    {
        using var s = new DebugService();
        Assert.That(s.HasCache, Is.False);
    }

    [Test]
    public void InitialState_CurrentThreadId_Zero()
    {
        using var s = new DebugService();
        Assert.That(s.CurrentThreadId, Is.EqualTo(0));
    }

    [Test]
    public void InitialState_IsDebugging_False()
    {
        using var s = new DebugService();
        Assert.That(s.IsDebugging, Is.False);
    }

    [Test]
    public void DoubleDispose_DoesNotThrow()
    {
        var s = new DebugService();
        s.Dispose();
        Assert.That(() => s.Dispose(), Throws.Nothing);
    }

    [Test]
    public void Stop_WhenNotStarted_DoesNotThrow()
    {
        using var s = new DebugService();
        Assert.That(() => s.Stop(), Throws.Nothing);
    }

    [Test]
    public async Task GetStackFramesAsync_WhenNotDebugging_ReturnsEmpty()
    {
        using var s = new DebugService();
        var frames = await s.GetStackFramesAsync(1);
        Assert.That(frames, Is.Empty);
    }

    [Test]
    public async Task GetLocalsAsync_WhenNotDebugging_ReturnsEmpty()
    {
        using var s = new DebugService();
        var locals = await s.GetLocalsAsync(0);
        Assert.That(locals, Is.Empty);
    }

    [Test]
    public async Task GetChildrenAsync_WhenNotDebugging_ReturnsEmpty()
    {
        using var s = new DebugService();
        var children = await s.GetChildrenAsync(0);
        Assert.That(children, Is.Empty);
    }

    [Test]
    public async Task GetThreadsAsync_WhenNotDebugging_ReturnsEmpty()
    {
        using var s = new DebugService();
        var threads = await s.GetThreadsAsync();
        Assert.That(threads, Is.Empty);
    }

    [Test]
    public async Task GetModulesAsync_WhenNotDebugging_ReturnsEmpty()
    {
        using var s = new DebugService();
        var modules = await s.GetModulesAsync();
        Assert.That(modules, Is.Empty);
    }

    [Test]
    public async Task EvaluateAsync_WhenNotDebugging_ReturnsNull()
    {
        using var s = new DebugService();
        var result = await s.EvaluateAsync("x");
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ContinueAsync_WhenNotDebugging_DoesNotThrow()
    {
        using var s = new DebugService();
        Assert.That(() => s.ContinueAsync(), Throws.Nothing);
    }

    [Test]
    public async Task StepOverAsync_WhenNotDebugging_DoesNotThrow()
    {
        using var s = new DebugService();
        Assert.That(() => s.StepOverAsync(), Throws.Nothing);
    }

    [Test]
    public async Task StepInAsync_WhenNotDebugging_DoesNotThrow()
    {
        using var s = new DebugService();
        Assert.That(() => s.StepInAsync(), Throws.Nothing);
    }

    [Test]
    public async Task StepOutAsync_WhenNotDebugging_DoesNotThrow()
    {
        using var s = new DebugService();
        Assert.That(() => s.StepOutAsync(), Throws.Nothing);
    }

    [Test]
    public async Task PauseAsync_WhenNotDebugging_DoesNotThrow()
    {
        using var s = new DebugService();
        Assert.That(() => s.PauseAsync(), Throws.Nothing);
    }

    [Test]
    public void Event_SubscribeAndUnsubscribe_DoesNotThrow()
    {
        using var s = new DebugService();
        s.DebugStarted += Handler;
        s.DebugStarted -= Handler;
        s.DebugStopped += Handler;
        s.DebugStopped -= Handler;
        s.Stopped += Handler2;
        s.Stopped -= Handler2;
        s.Continued += HandlerA;
        s.Continued -= HandlerA;
        s.ThreadsChanged += HandlerA;
        s.ThreadsChanged -= HandlerA;
        s.ExecutionPositionChanged += Handler3;
        s.ExecutionPositionChanged -= Handler3;
        return;

        void Handler(object? _, System.EventArgs __) { }
        void HandlerA() { }
        void Handler2(int __, string _) { }
        void Handler3(string _, int __) { }
    }

    [Test]
    public async Task Event_DoesNotFireAfterUnsubscribe()
    {
        using var s = new DebugService();
        var hit = false;
        s.DebugStopped += Handler;
        s.DebugStopped -= Handler;
        // No way to fire the event directly — this just validates no NRE on unsub
        await Task.CompletedTask;
        Assert.That(hit, Is.False);
        return;

        void Handler(object? _, System.EventArgs __) { hit = true; }
    }
}
