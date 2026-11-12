using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Application.UnitTests.Fakes;

/// <summary>Hand-written fakes: simpler to read than a mocking library and they behave like the real thing.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    /// <summary>Simulates a database constraint rejecting the next save.</summary>
    public ConflictKind? FailNextSaveWith { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (FailNextSaveWith is { } kind)
        {
            FailNextSaveWith = null;
            throw new PersistenceConflictException(kind, "simulated", new InvalidOperationException("simulated"));
        }

        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryUsers : IUserRepository
{
    public List<User> Items { get; } = [];

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Find(u => u.Id == id));

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Find(u => u.Email == normalizedEmail));

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Exists(u => u.Email == normalizedEmail));

    public void Add(User user) => Items.Add(user);
}

internal sealed class InMemoryServices : IServiceRepository
{
    public List<Service> Items { get; } = [];

    public Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Find(s => s.Id == id));

    public Task<PagedList<Service>> ListAsync(bool includeInactive, PageRequest page, CancellationToken cancellationToken = default)
    {
        var all = Items.Where(s => includeInactive || s.IsActive).OrderBy(s => s.Name).ToList();
        return Task.FromResult(new PagedList<Service>(all.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, all.Count));
    }

    public Task<IReadOnlyList<Guid>> FindMissingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(ids.Where(id => !Items.Exists(s => s.Id == id)).ToList());

    public void Add(Service service) => Items.Add(service);
}

internal sealed class InMemoryProfessionals : IProfessionalRepository
{
    public List<Professional> Items { get; } = [];

    public Task<Professional?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Find(p => p.Id == id));

    public Task<PagedList<Professional>> ListAsync(
        Guid? serviceId, bool includeInactive, PageRequest page, CancellationToken cancellationToken = default)
    {
        var all = Items
            .Where(p => includeInactive || p.IsActive)
            .Where(p => serviceId is null || p.Performs(serviceId.Value))
            .ToList();
        return Task.FromResult(new PagedList<Professional>(all.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, all.Count));
    }

    public void Add(Professional professional) => Items.Add(professional);
}

internal sealed class InMemoryAppointments : IAppointmentRepository
{
    public List<Appointment> Items { get; } = [];

    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Find(a => a.Id == id));

    public Task<IReadOnlyList<Appointment>> ListActiveForProfessionalAsync(
        Guid professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Appointment>>(
            Items.Where(a => a.ProfessionalId == professionalId && a.IsActive && a.Overlaps(from, to)).ToList());

    public Task<bool> ProfessionalHasOverlapAsync(
        Guid professionalId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Exists(a => a.ProfessionalId == professionalId && a.IsActive && a.Overlaps(start, end)));

    public Task<bool> CustomerHasOverlapAsync(
        Guid customerId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Exists(a => a.CustomerId == customerId && a.IsActive && a.Overlaps(start, end)));

    public Task<PagedList<Appointment>> SearchAsync(AppointmentFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        var all = Items
            .Where(a => filter.CustomerId is null || a.CustomerId == filter.CustomerId)
            .Where(a => filter.ProfessionalId is null || a.ProfessionalId == filter.ProfessionalId)
            .Where(a => filter.Status is null || a.Status == filter.Status)
            .OrderBy(a => a.StartsAt)
            .ToList();
        return Task.FromResult(new PagedList<Appointment>(all.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, all.Count));
    }

    public void Add(Appointment appointment) => Items.Add(appointment);
}

/// <summary>Deterministic, fast hasher: "hash:" + password. Counts verifications to test timing-safe login.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int VerifyCount { get; private set; }

    public string Hash(string password) => "hash:" + password;

    public bool Verify(string hashedPassword, string providedPassword)
    {
        VerifyCount++;
        return hashedPassword == "hash:" + providedPassword;
    }
}

internal sealed class FakeTokenService : ITokenService
{
    public AuthToken CreateAccessToken(User user) => new($"token-for-{user.Id}", DateTimeOffset.UnixEpoch);
}
