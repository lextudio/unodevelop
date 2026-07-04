using System;
using System.Collections.Generic;
using System.Linq;

namespace DebugTestApp;

class Program
{
    static void Main(string[] args)
    {
        var greeting = "Hello, Debugger!";
        var answer = 42;
        var pi = 3.14159;
        var fruits = new List<string> { "apple", "banana", "cherry", "date" };
        var scores = new Dictionary<string, int>
        {
            ["Alice"] = 95,
            ["Bob"] = 87,
            ["Charlie"] = 92
        };
        var buffer = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
        int? maybeValue = 10;
        var message = ComputeGreeting("World");

        Console.WriteLine(greeting);
        Console.ReadLine();
    }

    static string ComputeGreeting(string name)
    {
        var prefix = "Hello";
        var result = $"{prefix}, {name}!";
        return result;
    }
}
