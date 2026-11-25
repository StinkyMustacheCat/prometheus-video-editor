using System;
using System.Text.Json;
using Prometheus.Core.Primitives;
using Xunit;

namespace Prometheus.UnitTests;

public class RationalTests
{
    [Fact]
    public void LargeOperandsReduceBeforeNarrowingAndCompareWithoutOverflow()
    {
        var value = new Rational(4_000_000_007, 4_000_000_009);
        Assert.Equal(Rational.One, value * new Rational(value.D, value.N));
        Assert.Equal(Rational.Zero, value - value);
        Assert.True(new Rational(long.MaxValue, 2) > new Rational(long.MaxValue - 1, 2));
        Assert.Equal(new Rational(long.MinValue / 2, 1), new Rational(long.MinValue, 2));
        Assert.Throws<OverflowException>(() => new Rational(long.MaxValue, 1) * new Rational(2, 1));
    }

    [Fact]
    public void Constructor_SimplifiesCorrectly()
    {
        var r = new Rational(4, 8);
        Assert.Equal(1, r.N);
        Assert.Equal(2, r.D);
        Assert.Equal(0.5, r.Seconds);
    }

    [Fact]
    public void Constructor_RejectsZeroOrNegativeDenominator()
    {
        Assert.Throws<ArgumentException>(() => new Rational(1, 0));
        Assert.Throws<ArgumentException>(() => new Rational(1, -5));
    }

    [Fact]
    public void TimeAndFrame_Helpers_CalculateAccurateValues()
    {
        var t = Rational.Time(1.5); // 1.5s @ 48000 Hz = 72000 / 48000 = 3 / 2
        Assert.Equal(3, t.N);
        Assert.Equal(2, t.D);
        Assert.Equal(1.5, t.Seconds);

        var f = Rational.Frame(0.5); // 0.5s @ 60 FPS = 30 / 60 = 1 / 2
        Assert.Equal(1, f.N);
        Assert.Equal(2, f.D);
        Assert.Equal(0.5, f.Seconds);
    }

    [Fact]
    public void Arithmetic_OperatorsWorkCorrectly()
    {
        var a = new Rational(1, 3);
        var b = new Rational(1, 6);

        var sum = a + b;
        Assert.Equal(1, sum.N);
        Assert.Equal(2, sum.D); // 1/3 + 1/6 = 3/6 = 1/2

        var diff = a - b;
        Assert.Equal(1, diff.N);
        Assert.Equal(6, diff.D); // 1/3 - 1/6 = 1/6

        var prod = a * b;
        Assert.Equal(1, prod.N);
        Assert.Equal(18, prod.D); // 1/3 * 1/6 = 1/18

        var div = a / b;
        Assert.Equal(2, div.N);
        Assert.Equal(1, div.D); // (1/3) / (1/6) = 2
    }

    [Fact]
    public void JsonSerialization_SerializesAndDeserializesRoundtrip()
    {
        var r = new Rational(7, 60);
        string json = JsonSerializer.Serialize(r);
        var restored = JsonSerializer.Deserialize<Rational>(json);

        Assert.Equal(r.N, restored.N);
        Assert.Equal(r.D, restored.D);
    }
}
