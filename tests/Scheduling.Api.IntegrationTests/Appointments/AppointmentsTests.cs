using System.Net;
using System.Net.Http.Json;
using Scheduling.Api.IntegrationTests.Infrastructure;
using Scheduling.Application.Appointments;
using Scheduling.Application.Availability;
using Scheduling.Application.Common;
using Scheduling.Application.Professionals;
using Scheduling.Application.Services;
using Scheduling.Domain.Appointments;
using Scheduling.Tests.Common;

namespace Scheduling.Api.IntegrationTests.Appointments;

public class AppointmentsTests(DatabaseFixture database) : ApiTest(database)
{
    // Clock: Monday 2026-12-07 12:00 UTC (09:00 in São Paulo). Professional works Mon-Fri 09:00-18:00 local.

    /// <summary>Tuesday 10:00 in São Paulo.</summary>
    private static readonly DateTimeOffset TuesdayTenLocal = TestTime.At(2026, 12, 8, 13);

    private sealed record Setup(HttpClient Admin, HttpClient Customer, ServiceDto Service, ProfessionalDto Professional);

    private async Task<Setup> ArrangeAsync(int durationMinutes = 30)
    {
        var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync(durationMinutes: durationMinutes, price: 50m);
        var professional = await admin.CreateProfessionalAsync("Ana", [service]);
        var customer = await CreateCustomerClientAsync();
        return new Setup(admin, customer, service, professional);
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Setup s, DateTimeOffset startsAt, Guid? professionalId = null) =>
        client.PostAsJsonAsync(
            "/api/appointments",
            new { professionalId = professionalId ?? s.Professional.Id, serviceId = s.Service.Id, startsAt = Iso(startsAt) },
            Json,
            Ct);

    private static async Task<AppointmentDto> BookOkAsync(HttpClient client, Setup s, DateTimeOffset startsAt)
    {
        var response = await BookAsync(client, s, startsAt);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadAsync<AppointmentDto>(response);
    }

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        (await ReadProblemAsync(response)).Code.Should().Be(code);
    }

    // ---------- Booking ----------

    // R8
    [Fact]
    public async Task Book_ValidSlot_Returns201WithComputedEndAndCopiedPrice()
    {
        var s = await ArrangeAsync(durationMinutes: 45);

        var response = await BookAsync(s.Customer, s, TuesdayTenLocal);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var appointment = await ReadAsync<AppointmentDto>(response);
        response.Headers.Location!.ToString().Should().EndWith($"/api/appointments/{appointment.Id}");
        appointment.StartsAt.Should().Be(TuesdayTenLocal);
        appointment.EndsAt.Should().Be(TuesdayTenLocal.AddMinutes(45));
        appointment.LocalStartsAt.Hour.Should().Be(10);
        appointment.PriceAtBooking.Should().Be(50m);
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task Book_AcceptsStartTimeWithLocalOffset()
    {
        var s = await ArrangeAsync();
        var local = new DateTimeOffset(2026, 12, 8, 10, 0, 0, TimeSpan.FromHours(-3));

        var appointment = await BookOkAsync(s.Customer, s, local);

        appointment.StartsAt.Should().Be(TuesdayTenLocal);
        appointment.StartsAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Book_PriceChangeLater_DoesNotChangeTheBooking()
    {
        var s = await ArrangeAsync();
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);

        await s.Admin.PutAsJsonAsync($"/api/services/{s.Service.Id}", new { name = "Haircut", durationMinutes = 30, price = 99, isActive = true }, Json, Ct);
        var reloaded = await s.Customer.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{booked.Id}", Json, Ct);

        reloaded!.PriceAtBooking.Should().Be(50m);
    }

    [Fact]
    public async Task Book_Anonymous_Returns401()
    {
        var s = await ArrangeAsync();
        using var anonymous = CreateClient();

        (await BookAsync(anonymous, s, TuesdayTenLocal)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Book_AsAdmin_Returns403()
    {
        var s = await ArrangeAsync();

        (await BookAsync(s.Admin, s, TuesdayTenLocal)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Book_WithMissingFields_Returns400()
    {
        var s = await ArrangeAsync();

        var response = await s.Customer.PostAsJsonAsync("/api/appointments", new { startsAt = Iso(TuesdayTenLocal) }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).Errors.Should().ContainKeys("professionalId", "serviceId");
    }

    [Fact]
    public async Task Book_UnknownProfessional_Returns404()
    {
        var s = await ArrangeAsync();

        await ShouldFailWith(await BookAsync(s.Customer, s, TuesdayTenLocal, Guid.NewGuid()), HttpStatusCode.NotFound, "Professional.NotFound");
    }

    // R1
    [Fact]
    public async Task Book_OutsideWorkingHours_Returns422()
    {
        var s = await ArrangeAsync();

        // 17:45 local + 30 min crosses the 18:00 closing time.
        await ShouldFailWith(await BookAsync(s.Customer, s, TestTime.At(2026, 12, 8, 20, 45)), HttpStatusCode.UnprocessableEntity, "Appointment.OutsideWorkingHours");
    }

    [Fact]
    public async Task Book_OnADayOff_Returns422()
    {
        var s = await ArrangeAsync();
        var saturday = TestTime.At(2026, 12, 12, 13);

        await ShouldFailWith(await BookAsync(s.Customer, s, saturday), HttpStatusCode.UnprocessableEntity, "Appointment.OutsideWorkingHours");
    }

    // R5
    [Fact]
    public async Task Book_LessThanOneHourAhead_Returns422()
    {
        var s = await ArrangeAsync();

        await ShouldFailWith(await BookAsync(s.Customer, s, TestTime.Now.AddMinutes(30)), HttpStatusCode.UnprocessableEntity, "Appointment.TooSoon");
    }

    [Fact]
    public async Task Book_MoreThanSixtyDaysAhead_Returns422()
    {
        var s = await ArrangeAsync();

        await ShouldFailWith(await BookAsync(s.Customer, s, TuesdayTenLocal.AddDays(63)), HttpStatusCode.UnprocessableEntity, "Appointment.TooFarAhead");
    }

    // R6
    [Fact]
    public async Task Book_OffTheGrid_Returns422()
    {
        var s = await ArrangeAsync();

        await ShouldFailWith(await BookAsync(s.Customer, s, TuesdayTenLocal.AddMinutes(10)), HttpStatusCode.UnprocessableEntity, "Appointment.NotOnGrid");
    }

    // R4
    [Fact]
    public async Task Book_DuringTimeOff_Returns422()
    {
        var s = await ArrangeAsync();
        await s.Admin.PostAsJsonAsync(
            $"/api/professionals/{s.Professional.Id}/time-off",
            new { startsAt = Iso(TuesdayTenLocal.AddHours(-1)), endsAt = Iso(TuesdayTenLocal.AddHours(2)) },
            Json,
            Ct);

        await ShouldFailWith(await BookAsync(s.Customer, s, TuesdayTenLocal), HttpStatusCode.UnprocessableEntity, "Appointment.ProfessionalOnTimeOff");
    }

    // R7
    [Fact]
    public async Task Book_InactiveService_Returns422()
    {
        var s = await ArrangeAsync();
        await s.Admin.DeleteAsync(new Uri($"/api/services/{s.Service.Id}", UriKind.Relative), Ct);

        await ShouldFailWith(await BookAsync(s.Customer, s, TuesdayTenLocal), HttpStatusCode.UnprocessableEntity, "Service.Inactive");
    }

    [Fact]
    public async Task Book_InactiveProfessional_Returns422()
    {
        var s = await ArrangeAsync();
        await s.Admin.PutAsJsonAsync($"/api/professionals/{s.Professional.Id}", new { name = "Ana", isActive = false }, Json, Ct);

        await ShouldFailWith(await BookAsync(s.Customer, s, TuesdayTenLocal), HttpStatusCode.UnprocessableEntity, "Professional.Inactive");
    }

    // R2
    [Fact]
    public async Task Book_OverlappingTheProfessionalsAppointment_Returns409()
    {
        var s = await ArrangeAsync();
        await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        using var other = await CreateCustomerClientAsync("joao@test.local", "João");

        await ShouldFailWith(await BookAsync(other, s, TuesdayTenLocal.AddMinutes(15)), HttpStatusCode.Conflict, "Appointment.SlotTaken");
    }

    [Fact]
    public async Task Book_BackToBack_Succeeds()
    {
        var s = await ArrangeAsync();
        await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        using var other = await CreateCustomerClientAsync("joao@test.local", "João");

        (await BookAsync(other, s, TuesdayTenLocal.AddMinutes(30))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // R3
    [Fact]
    public async Task Book_CustomerAlreadyBusyWithAnotherProfessional_Returns409()
    {
        var s = await ArrangeAsync();
        var bruno = await s.Admin.CreateProfessionalAsync("Bruno", [s.Service]);
        await BookOkAsync(s.Customer, s, TuesdayTenLocal);

        await ShouldFailWith(await BookAsync(s.Customer, s, TuesdayTenLocal, bruno.Id), HttpStatusCode.Conflict, "Appointment.CustomerOverlap");
    }

    // ADR 0003: the headline test. Many customers race for one slot; exactly one wins.
    [Fact]
    public async Task Book_ConcurrentRequestsForTheSameSlot_ExactlyOneSucceeds()
    {
        var s = await ArrangeAsync();
        var customers = new List<HttpClient>();
        for (var i = 0; i < 10; i++)
        {
            customers.Add(await CreateCustomerClientAsync($"racer{i}@test.local", $"Racer {i}"));
        }

        var responses = await Task.WhenAll(customers.Select(c => BookAsync(c, s, TuesdayTenLocal)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            (await ReadProblemAsync(conflict)).Code.Should().Be("Appointment.SlotTaken");
        }

        var all = await s.Admin.GetFromJsonAsync<PagedList<AppointmentDto>>($"/api/appointments?professionalId={s.Professional.Id}", Json, Ct);
        all!.TotalCount.Should().Be(1);

        customers.ForEach(c => c.Dispose());
    }

    // ---------- Reading ----------

    [Fact]
    public async Task Get_SomeoneElsesAppointment_Returns404ButAdminSeesIt()
    {
        var s = await ArrangeAsync();
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        using var other = await CreateCustomerClientAsync("joao@test.local", "João");

        var asOther = await other.GetAsync(new Uri($"/api/appointments/{booked.Id}", UriKind.Relative), Ct);
        var asAdmin = await s.Admin.GetAsync(new Uri($"/api/appointments/{booked.Id}", UriKind.Relative), Ct);

        asOther.StatusCode.Should().Be(HttpStatusCode.NotFound);
        asAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Mine_ReturnsOnlyMyAppointmentsAndFiltersByStatus()
    {
        var s = await ArrangeAsync();
        var first = await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        await BookOkAsync(s.Customer, s, TuesdayTenLocal.AddDays(1));
        using var other = await CreateCustomerClientAsync("joao@test.local", "João");
        await BookOkAsync(other, s, TuesdayTenLocal.AddHours(2));
        await s.Customer.PostAsJsonAsync($"/api/appointments/{first.Id}/cancel", new { reason = "Busy" }, Json, Ct);

        var mine = await s.Customer.GetFromJsonAsync<PagedList<AppointmentDto>>("/api/appointments/me", Json, Ct);
        var cancelled = await s.Customer.GetFromJsonAsync<PagedList<AppointmentDto>>("/api/appointments/me?status=Cancelled", Json, Ct);

        mine!.TotalCount.Should().Be(2);
        cancelled!.Items.Should().ContainSingle().Which.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task List_AsCustomer_Returns403()
    {
        var s = await ArrangeAsync();

        (await s.Customer.GetAsync(new Uri("/api/appointments", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_AsAdmin_FiltersByPeriod()
    {
        var s = await ArrangeAsync();
        await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        await BookOkAsync(s.Customer, s, TuesdayTenLocal.AddDays(1));

        var tuesdayOnly = await s.Admin.GetFromJsonAsync<PagedList<AppointmentDto>>(
            $"/api/appointments?from={Uri.EscapeDataString(Iso(TestTime.At(2026, 12, 8, 3)))}&to={Uri.EscapeDataString(Iso(TestTime.At(2026, 12, 9, 3)))}",
            Json,
            Ct);

        tuesdayOnly!.Items.Should().ContainSingle().Which.StartsAt.Should().Be(TuesdayTenLocal);
    }

    [Fact]
    public async Task List_WithToBeforeFrom_Returns400()
    {
        var s = await ArrangeAsync();

        var response = await s.Admin.GetAsync(
            new Uri($"/api/appointments?from={Uri.EscapeDataString(Iso(TuesdayTenLocal))}&to={Uri.EscapeDataString(Iso(TuesdayTenLocal.AddDays(-1)))}", UriKind.Relative),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Cancelling (R9) ----------

    [Fact]
    public async Task Cancel_ByOwnerInTime_FreesTheSlot()
    {
        var s = await ArrangeAsync(durationMinutes: 60);
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);

        var response = await s.Customer.PostAsJsonAsync($"/api/appointments/{booked.Id}/cancel", new { reason = "Sick" }, Json, Ct);
        var availability = await s.Customer.GetFromJsonAsync<AvailabilityDto>(
            $"/api/professionals/{s.Professional.Id}/availability?serviceId={s.Service.Id}&date=2026-12-08", Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await ReadAsync<AppointmentDto>(response);
        cancelled.Status.Should().Be(AppointmentStatus.Cancelled);
        cancelled.CancellationReason.Should().Be("Sick");
        availability!.Slots.Should().Contain(slot => slot.StartsAt == TuesdayTenLocal);
    }

    [Fact]
    public async Task Cancel_ByOwnerLessThanTwoHoursBefore_Returns422()
    {
        var s = await ArrangeAsync();
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        await TravelToAsync(TuesdayTenLocal.AddMinutes(-90));

        var response = await s.Customer.PostAsync(new Uri($"/api/appointments/{booked.Id}/cancel", UriKind.Relative), null, Ct);

        await ShouldFailWith(response, HttpStatusCode.UnprocessableEntity, "Appointment.CancellationWindowClosed");
    }

    [Fact]
    public async Task Cancel_ByAdminLessThanTwoHoursBefore_Succeeds()
    {
        var s = await ArrangeAsync();
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        await TravelToAsync(TuesdayTenLocal.AddMinutes(-30));

        var response = await s.Admin.PostAsync(new Uri($"/api/appointments/{booked.Id}/cancel", UriKind.Relative), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancel_SomeoneElsesAppointment_Returns404()
    {
        var s = await ArrangeAsync();
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        using var other = await CreateCustomerClientAsync("joao@test.local", "João");

        var response = await other.PostAsync(new Uri($"/api/appointments/{booked.Id}/cancel", UriKind.Relative), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- Completing / no-show (R10) ----------

    [Fact]
    public async Task Complete_BeforeStart_Returns422_AfterStart_Succeeds()
    {
        var s = await ArrangeAsync();
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);

        var early = await s.Admin.PostAsync(new Uri($"/api/appointments/{booked.Id}/complete", UriKind.Relative), null, Ct);
        await TravelToAsync(TuesdayTenLocal.AddMinutes(20));
        var onTime = await s.Admin.PostAsync(new Uri($"/api/appointments/{booked.Id}/complete", UriKind.Relative), null, Ct);
        var again = await s.Admin.PostAsync(new Uri($"/api/appointments/{booked.Id}/no-show", UriKind.Relative), null, Ct);

        await ShouldFailWith(early, HttpStatusCode.UnprocessableEntity, "Appointment.NotStartedYet");
        onTime.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AppointmentDto>(onTime)).Status.Should().Be(AppointmentStatus.Completed);
        await ShouldFailWith(again, HttpStatusCode.UnprocessableEntity, "Appointment.InvalidTransition");
    }

    [Fact]
    public async Task NoShow_ByCustomer_Returns403()
    {
        var s = await ArrangeAsync();
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        await TravelToAsync(TuesdayTenLocal.AddMinutes(20));

        var response = await s.Customer.PostAsync(new Uri($"/api/appointments/{booked.Id}/no-show", UriKind.Relative), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoShow_AfterStart_KeepsTheSlotBlocked()
    {
        var s = await ArrangeAsync();
        var booked = await BookOkAsync(s.Customer, s, TuesdayTenLocal);
        await TravelToAsync(TuesdayTenLocal.AddMinutes(20));

        var response = await s.Admin.PostAsync(new Uri($"/api/appointments/{booked.Id}/no-show", UriKind.Relative), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<AppointmentDto>(response)).Status.Should().Be(AppointmentStatus.NoShow);
    }
}
