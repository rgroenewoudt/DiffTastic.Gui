using System;
using System.Linq;

namespace Demo;

public class Calculator
{
    public int Add(int a, int b, int c = 0)
    {
        return a + b + c;
    }

    public int Multiply(int a, int b) => a * b;

    public int Sum(params int[] values)
    {
        return values.Sum();
    }
}
