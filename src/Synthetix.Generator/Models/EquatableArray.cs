namespace Synthetix.Models;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A small wrapper around an array that compares by VALUE instead of by reference.
/// </summary>
/// <remarks>
/// Why this type exists:
///
/// The source generator caches its work between builds so that typing in one
/// file does not re-run mapping generation for the whole project. That cache
/// only works if every model object can be compared by value - two models with
/// the same content must count as "equal".
///
/// A plain C# array breaks this. Two arrays that hold the same items are still
/// "not equal" because arrays compare by reference. If a model held a plain
/// array, the cache would always miss and the generator would get slow.
///
/// So every list inside a model is wrapped in this struct, which compares its
/// items one by one.
/// </remarks>
/// <typeparam name="T">The item type. It must support value equality itself.</typeparam>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    /// <summary>Creates a value-comparable array from a plain array.</summary>
    public EquatableArray(T[]? items) => _items = items;

    /// <summary>An empty array. Reused so we do not allocate a new one each time.</summary>
    public static EquatableArray<T> Empty => new(Array.Empty<T>());

    // Treat a null backing array exactly like an empty one. This keeps every
    // other method simple - it never has to check for null.
    private T[] Items => _items ?? Array.Empty<T>();

    /// <summary>How many items the array holds.</summary>
    public int Count => Items.Length;

    /// <summary>Gets the item at the given position.</summary>
    public T this[int index] => Items[index];

    /// <summary>True when the array has no items.</summary>
    public bool IsEmpty => Items.Length == 0;

    /// <summary>
    /// Two EquatableArrays are equal when they have the same length and every
    /// item at the same position is equal.
    /// </summary>
    public bool Equals(EquatableArray<T> other)
    {
        T[] mine = Items;
        T[] theirs = other.Items;

        if (mine.Length != theirs.Length)
        {
            return false;
        }

        for (int i = 0; i < mine.Length; i++)
        {
            if (!mine[i].Equals(theirs[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <summary>
    /// Builds one hash code out of every item, so that two equal arrays always
    /// produce the same hash code.
    /// </summary>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (T item in Items)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    /// <summary>Returns the items as a plain array. Used when handing data to Roslyn.</summary>
    public T[] ToArray() => Items;

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

/// <summary>Helper methods for building <see cref="EquatableArray{T}"/> values.</summary>
public static class EquatableArray
{
    /// <summary>Wraps a sequence in a value-comparable array.</summary>
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> source)
        where T : IEquatable<T>
        => new(source as T[] ?? source.ToArray());
}
