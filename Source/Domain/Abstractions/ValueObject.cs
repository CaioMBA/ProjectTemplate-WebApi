using System.Diagnostics.CodeAnalysis;

namespace Domain.Abstractions;

[SuppressMessage(
    "Minor Code Smell",
    "S4035:Classes implementing IEquatable<T> should be sealed",
    Justification = "This is an inheritance root by design; Equals compares GetType() so " +
                    "two different value-object types are never equal to each other.")]
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) =>
        other is not null
        && other.GetType() == GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(GetType());

        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
