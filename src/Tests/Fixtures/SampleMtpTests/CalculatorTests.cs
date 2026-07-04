using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SampleMtpTests;

[TestClass]
public sealed class CalculatorTests
{
    [TestMethod]
    public void Add_ReturnsSum()
    {
        Assert.AreEqual(5, new Calculator().Add(2, 3));
    }

    [TestMethod]
    public void Divide_ReturnsQuotient()
    {
        Assert.AreEqual(4, new Calculator().Divide(8, 2));
    }
}
