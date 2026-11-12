namespace Scheduling.Domain.Common;

/// <summary>Base for objects with identity: two entities are equal when their ids are equal.</summary>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity(Guid id) => Id = id;

    /// <summary>Required by EF Core for materialization.</summary>
    protected Entity()
    {
    }

    public Guid Id { get; private init; }

    public bool Equals(Entity? other) =>
        other is not null && other.GetType() == GetType() && other.Id == Id;

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}

/// <summary>
/// Entry point of a consistency boundary. Other aggregates reference it only by id,
/// and it is loaded and saved as a whole.
/// </summary>
public abstract class AggregateRoot : Entity
{
    protected AggregateRoot(Guid id) : base(id)
    {
    }

    protected AggregateRoot()
    {
    }
}
