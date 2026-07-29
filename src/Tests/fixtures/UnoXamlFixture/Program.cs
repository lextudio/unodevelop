using Microsoft.UI.Xaml;

namespace UnoXamlFixture;

public static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        Application.Start(_ => new App());
    }
}
