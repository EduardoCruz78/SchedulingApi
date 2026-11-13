using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduling.Application.Abstractions;
using Scheduling.Domain.Common;
using Scheduling.Infrastructure.Outbox;

namespace Scheduling.Infrastructure.Persistence;

/// <summary>
/// Saves the DbContext and translates PostgreSQL constraint errors into <see cref="PersistenceConflictException"/>.
/// Before saving, it turns the domain events of tracked aggregates into outbox messages, so the
/// events are written by the same SaveChanges, in the same transaction, as the change itself (ADR 0005).
/// </summary>
internal sealed class UnitOfWork(SchedulingDbContext dbContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = dbContext.ChangeTracker.Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.GetDomainEvents().Count > 0)
            .ToList();

        var messages = aggregates
            .SelectMany(aggregate => aggregate.GetDomainEvents())
            .Select(OutboxMapper.ToMessage)
            .OfType<OutboxMessage>()
            .ToList();
        dbContext.OutboxMessages.AddRange(messages);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Discard(messages);
            throw new PersistenceConflictException(ConflictKind.Concurrency, null, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } pg)
        {
            Discard(messages);
            throw new PersistenceConflictException(ConflictKind.Overlap, pg.ConstraintName, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            Discard(messages);
            throw new PersistenceConflictException(ConflictKind.Duplicate, pg.ConstraintName, ex);
        }

        // Only after a successful commit: if saving failed, the events did not happen.
        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());
    }

    /// <summary>The transaction rolled back: stop tracking the messages so a later save cannot write them.</summary>
    private void Discard(List<OutboxMessage> messages) =>
        messages.ForEach(message => dbContext.Entry(message).State = EntityState.Detached);
}
