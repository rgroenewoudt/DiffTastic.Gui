using System;

namespace Demo;

public class Calculator
{
    public int Subtract(int a, int b)
    {
        return a - b;
    }

    public int Multiply(int a, int b) => a * b;

    public int Add(int a, int b)
    {
        var sum = a + b;
        Console.WriteLine(sum);
        return sum;
    }

    public int Divide(int a, int b) => a / b;
}
