using NUnit.Framework;

namespace SampleNUnitMtpTests;

[TestFixture]
public sealed class CalculatorTests
{
    [Test]
    public void Add_ReturnsSum()
    {
        Assert.That(new Calculator().Add(2, 3), Is.EqualTo(5));
    }

    [Test]
    public void Divide_ReturnsQuotient()
    {
        Assert.That(new Calculator().Divide(8, 2), Is.EqualTo(4));
    }
}
