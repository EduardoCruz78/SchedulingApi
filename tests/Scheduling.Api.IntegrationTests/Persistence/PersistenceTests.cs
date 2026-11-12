using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Scheduling.Api.IntegrationTests.Infrastructure;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;
using Scheduling.Infrastructure.Auth;
using Scheduling.Infrastructure.Persistence;
using Scheduling.Infrastructure.Persistence.Repositories;
using Scheduling.Infrastructure.Persistence.Seed;
using Scheduling.Tests.Common;

namespace Scheduling.Api.IntegrationTests.Persistence;

/// <summary>Talks to the real database through the Infrastructure classes, below the HTTP layer.</summary>
public class PersistenceTests(DatabaseFixture database) : DatabaseTest(database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Catalog(User Customer, User OtherCustomer, Service Service, Professional Professional, Professional OtherProfessional);

    private async Task<Catalog> SeedCatalogAsync()
    {
        await using var db = Database.CreateDbContext();
        var customer = User.Create("Maria", "maria@test.local", "hash", Role.Customer, TestTime.Now).Value;
        var other = User.Create("João", "joao@test.local", "hash", Role.Customer, TestTime.Now).Value;
        var service = new ServiceBuilder().WithDuration(30).Build();
        var professional = new ProfessionalBuilder().Performing(service).WorkingWeekdays().Build();
        var otherProfessional = new ProfessionalBuilder().WithName("Bruno").Performing(service).WorkingWeekdays().Build();

        db.AddRange(customer, other, service, professional, otherProfessional);
        await db.SaveChangesAsync(Ct);
        return new Catalog(customer, other, service, professional, otherProfessional);
    }

    private async Task SaveAsync(Appointment appointment)
    {
        await using var db = Database.CreateDbContext();
        new AppointmentRepository(db).Add(appointment);
        await new UnitOfWork(db).SaveChangesAsync(Ct);
    }

    private static Appointment Booking(Catalog c, DateTimeOffset start, Guid? professionalId = null, Guid? customerId = null) =>
        new AppointmentBuilder()
            .ForCustomer(customerId ?? c.Customer.Id)
            .WithProfessional(professionalId ?? c.Professional.Id)
            .WithService(c.Service)
            .StartingAt(start)
            .Build();

    // ADR 0003: the exclusion constraint is the last line of defense for R2.
    [Fact]
    public async Task ExclusionConstraint_RejectsOverlappingAppointmentsOfSameProfessional()
    {
        var c = await SeedCatalogAsync();
        var start = TestTime.Now.AddDays(1);
        await SaveAsync(Booking(c, start));

        var overlapping = Booking(c, start.AddMinutes(15), customerId: c.OtherCustomer.Id);
        var save = () => SaveAsync(overlapping);

        var error = await save.Should().ThrowAsync<PersistenceConflictException>();
        error.Which.Kind.Should().Be(ConflictKind.Overlap);
        error.Which.ConstraintName.Should().Be("ex_appointments_professional_overlap");
    }

    [Fact]
    public async Task ExclusionConstraint_AllowsBackToBackAppointments()
    {
        var c = await SeedCatalogAsync();
        var start = TestTime.Now.AddDays(1);
        await SaveAsync(Booking(c, start));

        var save = () => SaveAsync(Booking(c, start.AddMinutes(30), customerId: c.OtherCustomer.Id));

        await save.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExclusionConstraint_IgnoresCancelledAppointments()
    {
        var c = await SeedCatalogAsync();
        var start = TestTime.Now.AddDays(1);
        var cancelled = new AppointmentBuilder()
            .ForCustomer(c.Customer.Id).WithProfessional(c.Professional.Id).WithService(c.Service)
            .StartingAt(start).Cancelled().Build();
        await SaveAsync(cancelled);

        var save = () => SaveAsync(Booking(c, start, customerId: c.OtherCustomer.Id));

        await save.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExclusionConstraint_AllowsSameTimeForDifferentProfessionals()
    {
        var c = await SeedCatalogAsync();
        var start = TestTime.Now.AddDays(1);
        await SaveAsync(Booking(c, start));

        var save = () => SaveAsync(Booking(c, start, professionalId: c.OtherProfessional.Id, customerId: c.OtherCustomer.Id));

        await save.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UniqueEmail_IsEnforcedByTheDatabase()
    {
        await SeedCatalogAsync();
        await using var db = Database.CreateDbContext();
        new UserRepository(db).Add(User.Create("Other Maria", "MARIA@test.local", "hash", Role.Customer, TestTime.Now).Value);

        var save = () => new UnitOfWork(db).SaveChangesAsync(Ct);

        (await save.Should().ThrowAsync<PersistenceConflictException>()).Which.Kind.Should().Be(ConflictKind.Duplicate);
    }

    [Fact]
    public async Task ConcurrentUpdates_OfTheSameAppointment_SecondOneIsRejected()
    {
        var c = await SeedCatalogAsync();
        var appointment = Booking(c, TestTime.Now.AddDays(1));
        await SaveAsync(appointment);

        await using var first = Database.CreateDbContext();
        await using var second = Database.CreateDbContext();
        var asCustomer = (await new AppointmentRepository(first).GetByIdAsync(appointment.Id, Ct))!;
        var asAdmin = (await new AppointmentRepository(second).GetByIdAsync(appointment.Id, Ct))!;

        asCustomer.Cancel(byAdmin: false, null, TestTime.Now, SchedulingPolicy.Default);
        await new UnitOfWork(first).SaveChangesAsync(Ct);

        asAdmin.Complete(appointment.StartsAt.AddMinutes(5));
        var save = () => new UnitOfWork(second).SaveChangesAsync(Ct);

        (await save.Should().ThrowAsync<PersistenceConflictException>()).Which.Kind.Should().Be(ConflictKind.Concurrency);
    }

    [Fact]
    public async Task Professional_RoundTripsTheWholeAggregate()
    {
        var c = await SeedCatalogAsync();
        await using (var db = Database.CreateDbContext())
        {
            var professional = (await new ProfessionalRepository(db).GetByIdAsync(c.Professional.Id, Ct))!;
            professional.SetWorkingHours([new WorkingHours(DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(12, 30))]);
            professional.AddTimeOff(TestTime.Now.AddDays(3), TestTime.Now.AddDays(4), "Vacation", TestTime.Now);
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using var fresh = Database.CreateDbContext();
        var reloaded = (await new ProfessionalRepository(fresh).GetByIdAsync(c.Professional.Id, Ct))!;

        reloaded.ServiceIds.Should().Equal(c.Service.Id);
        reloaded.WorkingHours.Should().ContainSingle()
            .Which.Should().Be(new WorkingHours(DayOfWeek.Saturday, new TimeOnly(8, 0), new TimeOnly(12, 30)));
        reloaded.TimeOffs.Should().ContainSingle().Which.Reason.Should().Be("Vacation");
        reloaded.TimeOffs.Single().StartsAt.Should().Be(TestTime.Now.AddDays(3));
    }

    [Fact]
    public async Task ProfessionalList_FiltersByService()
    {
        var c = await SeedCatalogAsync();
        await using var db = Database.CreateDbContext();
        var unrelated = new ServiceBuilder().WithName("Massage").Build();
        db.Add(unrelated);
        await db.SaveChangesAsync(Ct);
        var repository = new ProfessionalRepository(db);

        var withService = await repository.ListAsync(c.Service.Id, includeInactive: false, new PageRequest(), Ct);
        var withUnrelated = await repository.ListAsync(unrelated.Id, includeInactive: false, new PageRequest(), Ct);

        withService.TotalCount.Should().Be(2);
        withUnrelated.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AppointmentOverlapQueries_MatchHalfOpenIntervals()
    {
        var c = await SeedCatalogAsync();
        var start = TestTime.Now.AddDays(1);
        await SaveAsync(Booking(c, start));
        await using var db = Database.CreateDbContext();
        var repository = new AppointmentRepository(db);

        (await repository.ProfessionalHasOverlapAsync(c.Professional.Id, start.AddMinutes(15), start.AddMinutes(45), Ct)).Should().BeTrue();
        (await repository.ProfessionalHasOverlapAsync(c.Professional.Id, start.AddMinutes(30), start.AddMinutes(60), Ct)).Should().BeFalse();
        (await repository.CustomerHasOverlapAsync(c.Customer.Id, start.AddMinutes(-15), start.AddMinutes(15), Ct)).Should().BeTrue();
        (await repository.CustomerHasOverlapAsync(c.OtherCustomer.Id, start, start.AddMinutes(30), Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Seeder_CreatesDemoDataOnceAndIsIdempotent()
    {
        await using var db = Database.CreateDbContext();
        var seeder = new DataSeeder(
            db,
            new PasswordHasher(),
            new FakeTimeProvider(TestTime.Now),
            Options.Create(new BusinessOptions()),
            Options.Create(new SchedulingOptions()),
            NullLogger<DataSeeder>.Instance);

        await seeder.SeedAsync(Ct);
        await seeder.SeedAsync(Ct);

        db.Users.Count().Should().Be(3);
        db.Services.Count().Should().Be(5);
        db.Professionals.Count().Should().Be(3);
        db.Appointments.Count().Should().Be(4);
    }
}
