using System.Diagnostics.CodeAnalysis;

namespace Domain.Abstractions;

public interface IEntity
{
    object IdValue { get; }
}

[SuppressMessage(
    "Minor Code Smell",
    "S4035:Classes implementing IEquatable<T> should be sealed",
    Justification = "Entity equality is inherited by design; the Equals override compares " +
                    "GetType() so a derived type is never equal to a base or sibling type, " +
                    "which is the specific hazard the rule guards against.")]
public abstract class Entity<TId> : IEntity, IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    protected Entity() => Id = default!;

    public TId Id { get; protected init; }

    object IEntity.IdValue => Id;

    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other.GetType() == GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
