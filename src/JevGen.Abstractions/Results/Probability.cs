namespace JevGen;

/// <summary>
/// A validated probability in the closed interval <c>[0, 1]</c>.
/// </summary>
/// <remarks>
/// Core result types expose confidence as <see cref="double"/> for API simplicity
/// (see ADR notes in the technical design). <see cref="Probability"/> is available for
/// applications that prefer a validated value type on their own boundaries.
/// </remarks>
public readonly record struct Probability : IComparable<Probability>
{
    /// <summary>The underlying value, between 0 and 1 inclusive.</summary>
    public double Value { get; }

    /// <summary>Creates a probability, validating the supplied value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or outside <c>[0, 1]</c>.</exception>
    public Probability(double value)
    {
        if (double.IsNaN(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A probability must be a number between 0 and 1 inclusive.");
        }

        Value = value;
    }

    /// <summary>Zero probability.</summary>
    public static Probability Zero => new(0d);

    /// <summary>Certainty.</summary>
    public static Probability One => new(1d);

    /// <summary>Clamps an arbitrary double into the valid probability range.</summary>
    public static Probability Clamp(double value)
        => double.IsNaN(value) ? Zero : new Probability(Math.Clamp(value, 0d, 1d));

    /// <summary>Attempts to create a probability without throwing.</summary>
    public static bool TryCreate(double value, out Probability probability)
    {
        if (double.IsNaN(value) || value is < 0 or > 1)
        {
            probability = default;
            return false;
        }

        probability = new Probability(value);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(Probability other) => Value.CompareTo(other.Value);

    /// <summary>Implicitly converts to the underlying <see cref="double"/>.</summary>
    public static implicit operator double(Probability value) => value.Value;

    /// <summary>Explicitly converts a <see cref="double"/> into a validated probability.</summary>
    public static explicit operator Probability(double value) => new(value);

    /// <summary>Converts to the underlying <see cref="double"/>.</summary>
    public double ToDouble() => Value;

    /// <summary>Creates a validated probability from a <see cref="double"/>.</summary>
    public static Probability FromDouble(double value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator <(Probability left, Probability right) => left.CompareTo(right) < 0;

    public static bool operator <=(Probability left, Probability right) => left.CompareTo(right) <= 0;

    public static bool operator >(Probability left, Probability right) => left.CompareTo(right) > 0;

    public static bool operator >=(Probability left, Probability right) => left.CompareTo(right) >= 0;
}
