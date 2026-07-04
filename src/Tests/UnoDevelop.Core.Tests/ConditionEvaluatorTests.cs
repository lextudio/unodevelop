using ICSharpCode.Core;
using NUnit.Framework;
using UnoDevelop.Services;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class ConditionEvaluatorTests
{
    [SetUp]
    public void SetUp()
    {
        ExecutionState.IsRunning = null;
        ExecutionState.IsDebugging = null;
        ExecutionState.IsPaused = null;
    }

    [Test]
    public void ExecutionActive_NoProperty_DefaultsToTrue_RequiresActive()
    {
        var e = new ExecutionActiveConditionEvaluator();
        var cond = new Condition("none", new Properties(), null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void ExecutionActive_ActiveTrue_WhenRunning()
    {
        ExecutionState.IsRunning = () => true;
        var e = new ExecutionActiveConditionEvaluator();
        var cond = new Condition("activeTrue", new Properties { ["active"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.True);
    }

    [Test]
    public void ExecutionActive_ActiveTrue_WhenDebugging()
    {
        ExecutionState.IsDebugging = () => true;
        var e = new ExecutionActiveConditionEvaluator();
        var cond = new Condition("activeTrue", new Properties { ["active"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.True);
    }

    [Test]
    public void ExecutionActive_ActiveTrue_WhenIdle()
    {
        ExecutionState.IsRunning = () => false;
        ExecutionState.IsDebugging = () => false;
        var e = new ExecutionActiveConditionEvaluator();
        var cond = new Condition("activeTrue", new Properties { ["active"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void ExecutionActive_ActiveFalse_WhenIdle()
    {
        var e = new ExecutionActiveConditionEvaluator();
        var cond = new Condition("activeFalse", new Properties { ["active"] = "False" }, null);

        Assert.That(e.IsValid(null, cond), Is.True);
    }

    [Test]
    public void ExecutionActive_ActiveFalse_WhenRunning()
    {
        ExecutionState.IsRunning = () => true;
        var e = new ExecutionActiveConditionEvaluator();
        var cond = new Condition("activeFalse", new Properties { ["active"] = "False" }, null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void Debugging_DefaultsToFalse()
    {
        var e = new DebuggingConditionEvaluator();
        var cond = new Condition("debuggingTrue", new Properties { ["debugging"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void Debugging_TrueWhenDebugging()
    {
        ExecutionState.IsDebugging = () => true;
        var e = new DebuggingConditionEvaluator();
        var cond = new Condition("debuggingTrue", new Properties { ["debugging"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.True);
    }

    [Test]
    public void Debugging_FalseWhenNotDebugging()
    {
        ExecutionState.IsDebugging = () => false;
        var e = new DebuggingConditionEvaluator();
        var cond = new Condition("debuggingTrue", new Properties { ["debugging"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void Debugging_DebuggingFalse_EvaluatesCorrectly()
    {
        ExecutionState.IsDebugging = () => true;
        var e = new DebuggingConditionEvaluator();
        var cond = new Condition("debuggingFalse", new Properties { ["debugging"] = "False" }, null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void Paused_DefaultsToFalse()
    {
        var e = new PausedConditionEvaluator();
        var cond = new Condition("pausedTrue", new Properties { ["paused"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void Paused_TrueWhenPaused()
    {
        ExecutionState.IsPaused = () => true;
        var e = new PausedConditionEvaluator();
        var cond = new Condition("pausedTrue", new Properties { ["paused"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.True);
    }

    [Test]
    public void Paused_FalseWhenNotPaused()
    {
        ExecutionState.IsPaused = () => false;
        var e = new PausedConditionEvaluator();
        var cond = new Condition("pausedTrue", new Properties { ["paused"] = "True" }, null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void Paused_PausedFalse_EvaluatesCorrectly()
    {
        ExecutionState.IsPaused = () => true;
        var e = new PausedConditionEvaluator();
        var cond = new Condition("pausedFalse", new Properties { ["paused"] = "False" }, null);

        Assert.That(e.IsValid(null, cond), Is.False);
    }

    [Test]
    public void AnyActive_FalseByDefault()
    {
        Assert.That(ExecutionState.AnyActive, Is.False);
    }

    [Test]
    public void AnyActive_TrueWhenRunning()
    {
        ExecutionState.IsRunning = () => true;
        Assert.That(ExecutionState.AnyActive, Is.True);
    }

    [Test]
    public void AnyActive_TrueWhenDebugging()
    {
        ExecutionState.IsDebugging = () => true;
        Assert.That(ExecutionState.AnyActive, Is.True);
    }

    [Test]
    public void AnyActive_FalseWhenBothNull()
    {
        ExecutionState.IsRunning = null;
        ExecutionState.IsDebugging = null;
        Assert.That(ExecutionState.AnyActive, Is.False);
    }
}
