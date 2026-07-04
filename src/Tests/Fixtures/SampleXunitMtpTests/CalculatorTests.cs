using Xunit;

namespace SampleXunitMtpTests;

public sealed class CalculatorTests
{
    [Fact]
    public void Add_ReturnsSum()
    {
        Assert.Equal(5, new Calculator().Add(2, 3));
    }

    [Fact]
    public void Divide_ReturnsQuotient()
    {
        Assert.Equal(4, new Calculator().Divide(8, 2));
    }
}
