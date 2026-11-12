using Microsoft.EntityFrameworkCore;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence.Repositories;

internal static class QueryableExtensions
{
    public static async Task<PagedList<T>> ToPagedListAsync<T>(this IQueryable<T> query, PageRequest page, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedList<T>(items, page.Page, page.PageSize, total);
    }
}

internal sealed class UserRepository(SchedulingDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        db.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class ServiceRepository(SchedulingDbContext db) : IServiceRepository
{
    public Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<PagedList<Service>> ListAsync(bool includeInactive, PageRequest page, CancellationToken cancellationToken = default) =>
        db.Services
            .AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToPagedListAsync(page, cancellationToken);

    public async Task<IReadOnlyList<Guid>> FindMissingAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var existing = await db.Services.Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToListAsync(cancellationToken);
        return ids.Except(existing).ToList();
    }

    public void Add(Service service) => db.Services.Add(service);
}

internal sealed class ProfessionalRepository(SchedulingDbContext db) : IProfessionalRepository
{
    public Task<Professional?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Professionals
            .Include(p => p.TimeOffs)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PagedList<Professional>> ListAsync(
        Guid? serviceId, bool includeInactive, PageRequest page, CancellationToken cancellationToken = default) =>
        db.Professionals
            .AsNoTracking()
            .AsSplitQuery()
            .Where(p => includeInactive || p.IsActive)
            .Where(p => serviceId == null ||
                        EF.Property<List<ProfessionalService>>(p, "_services").Any(s => s.ServiceId == serviceId))
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToPagedListAsync(page, cancellationToken);

    public void Add(Professional professional) => db.Professionals.Add(professional);
}

internal sealed class AppointmentRepository(SchedulingDbContext db) : IAppointmentRepository
{
    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Appointments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> ListActiveForProfessionalAsync(
        Guid professionalId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        await db.Appointments
            .AsNoTracking()
            .Where(a => a.ProfessionalId == professionalId &&
                        a.Status != AppointmentStatus.Cancelled &&
                        a.StartsAt < to && from < a.EndsAt)
            .OrderBy(a => a.StartsAt)
            .ToListAsync(cancellationToken);

    public Task<bool> ProfessionalHasOverlapAsync(
        Guid professionalId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) =>
        db.Appointments.AnyAsync(
            a => a.ProfessionalId == professionalId &&
                 a.Status != AppointmentStatus.Cancelled &&
                 a.StartsAt < end && start < a.EndsAt,
            cancellationToken);

    public Task<bool> CustomerHasOverlapAsync(
        Guid customerId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) =>
        db.Appointments.AnyAsync(
            a => a.CustomerId == customerId &&
                 a.Status != AppointmentStatus.Cancelled &&
                 a.StartsAt < end && start < a.EndsAt,
            cancellationToken);

    public Task<PagedList<Appointment>> SearchAsync(AppointmentFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        var query = db.Appointments.AsNoTracking();

        if (filter.CustomerId is { } customerId)
        {
            query = query.Where(a => a.CustomerId == customerId);
        }

        if (filter.ProfessionalId is { } professionalId)
        {
            query = query.Where(a => a.ProfessionalId == professionalId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        if (filter.From is { } from)
        {
            query = query.Where(a => a.EndsAt > from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(a => a.StartsAt < to);
        }

        return query.OrderBy(a => a.StartsAt).ThenBy(a => a.Id).ToPagedListAsync(page, cancellationToken);
    }

    public void Add(Appointment appointment) => db.Appointments.Add(appointment);
}
