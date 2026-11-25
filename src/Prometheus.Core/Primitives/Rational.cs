using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Primitives;

/// <summary>
/// Representación exacta e inmutable de tiempo y proporciones de cuadros en formato racional (n / d).
/// Previene pérdidas de precisión por punto flotante en cálculo de frames y muestras de audio.
/// </summary>
[JsonConverter(typeof(RationalJsonConverter))]
public readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    public long N { get; }
    public long D { get; }

    public double Seconds => D == 0 ? 0 : (double)N / D;

    public Rational(long n, long d)
    {
        if (d <= 0)
            throw new ArgumentException("El denominador de Rational debe ser estrictamente positivo (> 0).", nameof(d));

        if (n == 0)
        {
            N = 0;
            D = 1;
            return;
        }

        long g = (long)Gcd(Int128.Abs((Int128)n), d);
        N = n / g;
        D = d / g;
    }

    public static Rational Zero => new(0, 1);
    public static Rational One => new(1, 1);

    /// <summary>
    /// Crea un racional con resolución de audio a 48000 Hz.
    /// </summary>
    public static Rational Time(double seconds)
    {
        long num = (long)Math.Round(seconds * 48000.0);
        return new Rational(num, 48000);
    }

    /// <summary>
    /// Crea un racional con resolución de vídeo a 60 FPS exactos.
    /// </summary>
    public static Rational Frame(double seconds)
    {
        long num = (long)Math.Round(seconds * 60.0);
        return new Rational(num, 60);
    }

    public static Rational FromFrames(long frames, int fps = 60)
    {
        return new Rational(frames, fps);
    }

    private static Int128 Gcd(Int128 a, Int128 b)
    {
        while (b != 0)
        {
            var remainder = a % b;
            a = b;
            b = remainder;
        }
        return a == 0 ? 1 : a;
    }

    private static Rational Reduced(Int128 numerator, Int128 denominator)
    {
        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }
        var divisor = Gcd(Int128.Abs(numerator), denominator);
        return new Rational(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }

    public static Rational operator +(Rational a, Rational b) =>
        Reduced((Int128)a.N * b.D + (Int128)b.N * a.D, (Int128)a.D * b.D);

    public static Rational operator -(Rational a, Rational b) =>
        Reduced((Int128)a.N * b.D - (Int128)b.N * a.D, (Int128)a.D * b.D);

    public static Rational operator *(Rational a, Rational b) =>
        Reduced((Int128)a.N * b.N, (Int128)a.D * b.D);

    public static Rational operator /(Rational a, Rational b)
    {
        if (b.N == 0) throw new DivideByZeroException("División por cero en Rational.");
        return Reduced((Int128)a.N * b.D, (Int128)a.D * b.N);
    }

    public bool Equals(Rational other) => N == other.N && D == other.D;
    public override bool Equals(object? obj) => obj is Rational other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(N, D);

    public int CompareTo(Rational other) =>
        ((Int128)N * other.D).CompareTo((Int128)other.N * D);

    public static bool operator ==(Rational left, Rational right) => left.Equals(right);
    public static bool operator !=(Rational left, Rational right) => !left.Equals(right);
    public static bool operator <(Rational left, Rational right) => left.CompareTo(right) < 0;
    public static bool operator <=(Rational left, Rational right) => left.CompareTo(right) <= 0;
    public static bool operator >(Rational left, Rational right) => left.CompareTo(right) > 0;
    public static bool operator >=(Rational left, Rational right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{N}/{D} ({Seconds:F4}s)";
}

public class RationalJsonConverter : JsonConverter<Rational>
{
    public override Rational Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            long n = 0;
            long d = 1;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string prop = reader.GetString() ?? "";
                    reader.Read();
                    if (string.Equals(prop, "n", StringComparison.OrdinalIgnoreCase))
                        n = reader.GetInt64();
                    else if (string.Equals(prop, "d", StringComparison.OrdinalIgnoreCase))
                        d = reader.GetInt64();
                }
            }

            return new Rational(n, d);
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            double val = reader.GetDouble();
            return Rational.Time(val);
        }

        return Rational.Zero;
    }

    public override void Write(Utf8JsonWriter writer, Rational value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("n", value.N);
        writer.WriteNumber("d", value.D);
        writer.WriteEndObject();
    }
}
