using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace JevGen.Generator;

/// <summary>
/// An immutable array with value equality.
/// </summary>
/// <remarks>
/// <see cref="ImmutableArray{T}"/> compares by reference, which would defeat the incremental
/// generator's caching: every recompilation would produce a new array and invalidate every
/// downstream step. Wrapping it in a value-equal type is what keeps generation incremental.
/// </remarks>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _values;

    public EquatableArray(ImmutableArray<T> values) => _values = values;

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    public ImmutableArray<T> Values => _values.IsDefault ? ImmutableArray<T>.Empty : _values;

    public int Length => Values.Length;

    public bool IsEmpty => Values.Length == 0;

    public T this[int index] => Values[index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = Values;
        var right = other.Values;

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;

            foreach (var value in Values)
            {
                hash = (hash * 31) + (value?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

internal static class EquatableArray
{
    public static EquatableArray<T> Create<T>(IEnumerable<T> values)
        where T : IEquatable<T>
        => new(values.ToImmutableArray());

    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> values)
        where T : IEquatable<T>
        => new(values.ToImmutableArray());
}
