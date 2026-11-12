using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Api.IntegrationTests.Infrastructure;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Availability;
using Scheduling.Application.Common;
using Scheduling.Application.Professionals;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Tests.Common;

namespace Scheduling.Api.IntegrationTests.Catalog;

public class ProfessionalsTests(DatabaseFixture database) : ApiTest(database)
{
    /// <summary>Tuesday after <see cref="TestTime.Now"/>; 09:00 in São Paulo is 12:00 UTC.</summary>
    private static readonly DateOnly Tuesday = new(2026, 12, 8);

    [Fact]
    public async Task SetupFlow_ExposesServicesAndWorkingHours()
    {
        using var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync();

        var professional = await admin.CreateProfessionalAsync("Ana", [service]);

        professional.ServiceIds.Should().Equal(service.Id);
        professional.WorkingHours.Should().HaveCount(5);
        professional.WorkingHours[0].Should().Be(new WorkingHoursDto(DayOfWeek.Monday, "09:00", "18:00"));
    }

    [Fact]
    public async Task SetServices_WithUnknownService_Returns422()
    {
        using var admin = await CreateAdminClientAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", []);

        var response = await admin.PutAsJsonAsync(
            $"/api/professionals/{professional.Id}/services", new { serviceIds = new[] { Guid.NewGuid() } }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).Code.Should().Be("Professional.UnknownServices");
    }

    // R11
    [Fact]
    public async Task SetWorkingHours_WithOverlappingIntervals_Returns400()
    {
        using var admin = await CreateAdminClientAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", []);

        var response = await admin.PutAsJsonAsync(
            $"/api/professionals/{professional.Id}/working-hours",
            new { intervals = new[] { new { day = "Monday", start = "09:00", end = "13:00" }, new { day = "Monday", start = "12:00", end = "18:00" } } },
            Json,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).Code.Should().Be("Professional.OverlappingWorkingHours");
    }

    [Fact]
    public async Task SetWorkingHours_WithBadTimeFormat_Returns400PointingAtTheField()
    {
        using var admin = await CreateAdminClientAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", []);

        var response = await admin.PutAsJsonAsync(
            $"/api/professionals/{professional.Id}/working-hours",
            new { intervals = new[] { new { day = "Monday", start = "9h", end = "18:00" } } },
            Json,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).Errors.Should().ContainKey("intervals[0].start");
    }

    [Fact]
    public async Task InactiveProfessional_IsHiddenFromPublicButVisibleToAdmin()
    {
        using var admin = await CreateAdminClientAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", []);
        await admin.PutAsJsonAsync($"/api/professionals/{professional.Id}", new { name = "Ana", isActive = false }, Json, Ct);
        using var anonymous = CreateClient();

        var publicResponse = await anonymous.GetAsync(new Uri($"/api/professionals/{professional.Id}", UriKind.Relative), Ct);
        var adminResponse = await admin.GetAsync(new Uri($"/api/professionals/{professional.Id}", UriKind.Relative), Ct);
        var publicList = await anonymous.GetFromJsonAsync<PagedList<ProfessionalDto>>("/api/professionals", Json, Ct);

        publicResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        publicList!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task List_FiltersByService()
    {
        using var admin = await CreateAdminClientAsync();
        var haircut = await admin.CreateServiceAsync("Haircut");
        var coloring = await admin.CreateServiceAsync("Coloring", 90);
        await admin.CreateProfessionalAsync("Ana", [haircut, coloring]);
        await admin.CreateProfessionalAsync("Bruno", [haircut]);
        using var anonymous = CreateClient();

        var list = await anonymous.GetFromJsonAsync<PagedList<ProfessionalDto>>($"/api/professionals?serviceId={coloring.Id}", Json, Ct);

        list!.Items.Select(p => p.Name).Should().Equal("Ana");
    }

    [Fact]
    public async Task TimeOff_IsVisibleOnlyToAdminAndCanBeRemoved()
    {
        using var admin = await CreateAdminClientAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", []);
        using var anonymous = CreateClient();

        var add = await admin.PostAsJsonAsync(
            $"/api/professionals/{professional.Id}/time-off",
            new { startsAt = Iso(TestTime.Now.AddDays(3)), endsAt = Iso(TestTime.Now.AddDays(5)), reason = "Vacation" },
            Json,
            Ct);
        var timeOff = await ReadAsync<TimeOffDto>(add);
        var asAdmin = await admin.GetFromJsonAsync<ProfessionalDto>($"/api/professionals/{professional.Id}", Json, Ct);
        var asPublic = await anonymous.GetFromJsonAsync<ProfessionalDto>($"/api/professionals/{professional.Id}", Json, Ct);
        var remove = await admin.DeleteAsync(new Uri($"/api/professionals/{professional.Id}/time-off/{timeOff.Id}", UriKind.Relative), Ct);
        var removeAgain = await admin.DeleteAsync(new Uri($"/api/professionals/{professional.Id}/time-off/{timeOff.Id}", UriKind.Relative), Ct);

        add.StatusCode.Should().Be(HttpStatusCode.Created);
        asAdmin!.TimeOffs.Should().ContainSingle().Which.Reason.Should().Be("Vacation");
        asPublic!.TimeOffs.Should().BeNull();
        remove.StatusCode.Should().Be(HttpStatusCode.NoContent);
        removeAgain.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // R13
    [Fact]
    public async Task TimeOff_OverABookedAppointment_Returns409()
    {
        using var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", [service]);
        using var customer = await CreateCustomerClientAsync();
        var me = await customer.GetFromJsonAsync<Application.Auth.UserDto>("/api/auth/me", Json, Ct);
        var startsAt = TestTime.At(2026, 12, 8, 13);
        await InsertAppointmentAsync(me!.Id, professional.Id, service.Id, startsAt);

        var response = await admin.PostAsJsonAsync(
            $"/api/professionals/{professional.Id}/time-off",
            new { startsAt = Iso(startsAt.AddHours(-1)), endsAt = Iso(startsAt.AddHours(3)) },
            Json,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(response)).Code.Should().Be("Professional.TimeOffConflictsWithAppointments");
    }

    [Fact]
    public async Task Availability_ReturnsUtcSlotsWithLocalTime()
    {
        using var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync(durationMinutes: 60);
        var professional = await admin.CreateProfessionalAsync("Ana", [service], "09:00", "11:00");
        using var anonymous = CreateClient();

        var availability = await anonymous.GetFromJsonAsync<AvailabilityDto>(
            $"/api/professionals/{professional.Id}/availability?serviceId={service.Id}&date=2026-12-08", Json, Ct);

        availability!.TimeZone.Should().Be("America/Sao_Paulo");
        availability.Date.Should().Be(Tuesday);
        availability.Slots.Should().HaveCount(5); // 09:00, 09:15, 09:30, 09:45, 10:00
        availability.Slots[0].StartsAt.Should().Be(TestTime.At(2026, 12, 8, 12));
        availability.Slots[0].LocalStartsAt.Offset.Should().Be(TimeSpan.FromHours(-3));
        availability.Slots[0].LocalStartsAt.Hour.Should().Be(9);
    }

    [Fact]
    public async Task Availability_ExcludesBookedSlots()
    {
        using var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync(durationMinutes: 60);
        var professional = await admin.CreateProfessionalAsync("Ana", [service], "09:00", "11:00");
        using var customer = await CreateCustomerClientAsync();
        var me = await customer.GetFromJsonAsync<Application.Auth.UserDto>("/api/auth/me", Json, Ct);
        await InsertAppointmentAsync(me!.Id, professional.Id, service.Id, TestTime.At(2026, 12, 8, 12)); // 09:00-10:00 local

        var availability = await customer.GetFromJsonAsync<AvailabilityDto>(
            $"/api/professionals/{professional.Id}/availability?serviceId={service.Id}&date=2026-12-08", Json, Ct);

        availability!.Slots.Select(s => s.LocalStartsAt.Hour * 100 + s.LocalStartsAt.Minute).Should().Equal(1000);
    }

    [Fact]
    public async Task Availability_WithoutServiceId_Returns400()
    {
        using var admin = await CreateAdminClientAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", []);
        using var anonymous = CreateClient();

        var response = await anonymous.GetAsync(new Uri($"/api/professionals/{professional.Id}/availability?date=2026-12-08", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // R7
    [Fact]
    public async Task Availability_ForServiceTheProfessionalDoesNotPerform_Returns422()
    {
        using var admin = await CreateAdminClientAsync();
        var haircut = await admin.CreateServiceAsync("Haircut");
        var coloring = await admin.CreateServiceAsync("Coloring", 90);
        var professional = await admin.CreateProfessionalAsync("Ana", [haircut]);
        using var anonymous = CreateClient();

        var response = await anonymous.GetAsync(
            new Uri($"/api/professionals/{professional.Id}/availability?serviceId={coloring.Id}&date=2026-12-08", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).Code.Should().Be("Professional.DoesNotPerformService");
    }

    // R7 + R12
    [Fact]
    public async Task Availability_ForInactiveService_Returns422()
    {
        using var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", [service]);
        await admin.DeleteAsync(new Uri($"/api/services/{service.Id}", UriKind.Relative), Ct);
        using var anonymous = CreateClient();

        var response = await anonymous.GetAsync(
            new Uri($"/api/professionals/{professional.Id}/availability?serviceId={service.Id}&date=2026-12-08", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).Code.Should().Be("Service.Inactive");
    }

    /// <summary>Inserts an appointment below the HTTP layer (booking endpoints are tested separately).</summary>
    private async Task InsertAppointmentAsync(Guid customerId, Guid professionalId, Guid serviceId, DateTimeOffset startsAt)
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider.GetRequiredService<IServiceRepository>();
        var service = (await services.GetByIdAsync(serviceId, Ct))!;
        var appointment = Appointment.Book(customerId, professionalId, service, startsAt, Clock.GetUtcNow(), SchedulingPolicy.Default).Value;
        scope.ServiceProvider.GetRequiredService<IAppointmentRepository>().Add(appointment);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
    }
}
