using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduling.Application.Abstractions;

namespace Scheduling.Infrastructure.Persistence;

/// <summary>Saves the DbContext and translates PostgreSQL constraint errors into <see cref="PersistenceConflictException"/>.</summary>
internal sealed class UnitOfWork(SchedulingDbContext dbContext) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new PersistenceConflictException(ConflictKind.Concurrency, null, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } pg)
        {
            throw new PersistenceConflictException(ConflictKind.Overlap, pg.ConstraintName, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new PersistenceConflictException(ConflictKind.Duplicate, pg.ConstraintName, ex);
        }
    }
}
