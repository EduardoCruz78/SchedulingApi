using Scheduling.Application.Common;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Abstractions;

/// <summary>Commits all changes tracked in the current request as one transaction.</summary>
public interface IUnitOfWork
{
    /// <exception cref="PersistenceConflictException">A database constraint rejected the changes.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    void Add(User user);
}

public interface IServiceRepository
{
    Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedList<Service>> ListAsync(bool includeInactive, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>Ids from <paramref name="ids"/> that do not exist.</summary>
    Task<IReadOnlyList<Guid>> FindMissingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    void Add(Service service);
}

public interface IProfessionalRepository
{
    /// <summary>Loads the whole aggregate: services, working hours and time off.</summary>
    Task<Professional?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedList<Professional>> ListAsync(Guid? serviceId, bool includeInactive, PageRequest page, CancellationToken cancellationToken = default);

    void Add(Professional professional);
}

public sealed record AppointmentFilter(
    Guid? CustomerId = null,
    Guid? ProfessionalId = null,
    AppointmentStatus? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Non-cancelled appointments of a professional that intersect [from, to).</summary>
    Task<IReadOnlyList<Appointment>> ListActiveForProfessionalAsync(
        Guid professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>R2: true when the professional has a non-cancelled appointment overlapping [start, end).</summary>
    Task<bool> ProfessionalHasOverlapAsync(Guid professionalId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);

    /// <summary>R3: true when the customer has a non-cancelled appointment overlapping [start, end).</summary>
    Task<bool> CustomerHasOverlapAsync(Guid customerId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);

    Task<PagedList<Appointment>> SearchAsync(AppointmentFilter filter, PageRequest page, CancellationToken cancellationToken = default);

    void Add(Appointment appointment);
}

public enum ConflictKind
{
    /// <summary>Exclusion constraint: overlapping time ranges (PostgreSQL 23P01).</summary>
    Overlap,

    /// <summary>Unique constraint (PostgreSQL 23505).</summary>
    Duplicate,

    /// <summary>Optimistic concurrency: the row changed since it was read.</summary>
    Concurrency,
}

/// <summary>
/// Raised by <see cref="IUnitOfWork"/> when the database rejects a write because of a constraint.
/// Lets use cases react (e.g. return 409) without knowing about Npgsql.
/// </summary>
public sealed class PersistenceConflictException : Exception
{
    public PersistenceConflictException()
    {
    }

    public PersistenceConflictException(string message) : base(message)
    {
    }

    public PersistenceConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public PersistenceConflictException(ConflictKind kind, string? constraintName, Exception innerException)
        : base($"Database rejected the change ({kind}, constraint '{constraintName}').", innerException)
    {
        Kind = kind;
        ConstraintName = constraintName;
    }

    public ConflictKind Kind { get; }

    public string? ConstraintName { get; }
}
