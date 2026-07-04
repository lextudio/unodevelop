namespace SampleXunitMtpTests;

public sealed class Calculator
{
    public int Add(int a, int b) => a + b;

    public int Divide(int a, int b)
    {
        if (b == 0)
        {
            // Deliberately left untested so a coverage run against this fixture would report a
            // partial percentage - mirrors SampleMtpTests' Calculator.
            return 0;
        }

        return a / b;
    }
}
