namespace Scheduling.Domain.Common;

/// <summary>
/// Something that happened to an aggregate and that other systems may care about.
/// Raised by the aggregate itself, so it is recorded only when the state change really happened.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
