namespace SampleMtpTests;

public sealed class Calculator
{
    public int Add(int a, int b) => a + b;

    public int Divide(int a, int b)
    {
        if (b == 0)
        {
            // Deliberately left untested by CalculatorTests so the fixture's
            // coverage run reports a partial (not 0% or 100%) percentage.
            return 0;
        }

        return a / b;
    }
}
