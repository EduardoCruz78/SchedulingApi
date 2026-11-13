using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Scheduling.Api.IntegrationTests.Infrastructure;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Professionals;
using Scheduling.Application.Services;
using Scheduling.Domain.Users;
using Scheduling.Infrastructure.Outbox;
using Scheduling.Infrastructure.Persistence;
using Scheduling.Infrastructure.Persistence.Repositories;
using Scheduling.Tests.Common;

namespace Scheduling.Api.IntegrationTests.Outbox;

/// <summary>ADR 0005: the outbox row is written with the appointment, or not at all.</summary>
public class OutboxWriteTests(DatabaseFixture database) : ApiTest(database)
{
    /// <summary>Tuesday 10:00 in São Paulo.</summary>
    private static readonly DateTimeOffset TuesdayTenLocal = TestTime.At(2026, 12, 8, 13);

    private async Task<(HttpClient Admin, HttpClient Customer, ServiceDto Service, ProfessionalDto Professional)> ArrangeAsync()
    {
        var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync(durationMinutes: 30, price: 50m);
        var professional = await admin.CreateProfessionalAsync("Ana", [service]);
        var customer = await CreateCustomerClientAsync();
        return (admin, customer, service, professional);
    }

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, ServiceDto service, ProfessionalDto professional, DateTimeOffset startsAt) =>
        client.PostAsJsonAsync(
            "/api/appointments",
            new { professionalId = professional.Id, serviceId = service.Id, startsAt = Iso(startsAt) },
            Json,
            Ct);

    private async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var db = Database.CreateDbContext();
        return await db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync(Ct);
    }

    [Fact]
    public async Task Booking_WritesAppointmentCreated_WithTheAppointment()
    {
        var (_, customer, service, professional) = await ArrangeAsync();

        var response = await BookAsync(customer, service, professional, TuesdayTenLocal);
        var appointment = await ReadAsync<AppointmentDto>(response);

        var message = (await OutboxAsync()).Should().ContainSingle().Subject;
        message.Type.Should().Be("appointment.created");
        message.OccurredAt.Should().Be(TestTime.Now);
        message.ProcessedAt.Should().BeNull();
        using var payload = JsonDocument.Parse(message.Payload);
        payload.RootElement.GetProperty("appointmentId").GetGuid().Should().Be(appointment.Id);
        payload.RootElement.GetProperty("professionalId").GetGuid().Should().Be(professional.Id);
        payload.RootElement.GetProperty("startsAt").GetDateTimeOffset().Should().Be(TuesdayTenLocal);
        payload.RootElement.GetProperty("price").GetDecimal().Should().Be(50m);
    }

    [Fact]
    public async Task Cancelling_WritesAppointmentCancelled()
    {
        var (_, customer, service, professional) = await ArrangeAsync();
        var appointment = await ReadAsync<AppointmentDto>(await BookAsync(customer, service, professional, TuesdayTenLocal));

        var cancel = await customer.PostAsJsonAsync($"/api/appointments/{appointment.Id}/cancel", new { reason = "Sick" }, Json, Ct);

        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var messages = await OutboxAsync();
        messages.Select(m => m.Type).Should().Equal("appointment.created", "appointment.cancelled");
        using var payload = JsonDocument.Parse(messages[1].Payload);
        payload.RootElement.GetProperty("cancelledBy").GetString().Should().Be("customer");
        payload.RootElement.GetProperty("reason").GetString().Should().Be("Sick");
    }

    [Fact]
    public async Task RefusedBooking_WritesNothing()
    {
        var (_, customer, service, professional) = await ArrangeAsync();

        // Saturday: outside working hours (R1).
        var response = await BookAsync(customer, service, professional, TestTime.At(2026, 12, 12, 13));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await OutboxAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SlotTaken_WritesOnlyTheWinnersEvent()
    {
        var (_, customer, service, professional) = await ArrangeAsync();
        var other = await CreateCustomerClientAsync("joao@test.local", "João");

        await BookAsync(customer, service, professional, TuesdayTenLocal);
        var second = await BookAsync(other, service, professional, TuesdayTenLocal);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OutboxAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ExclusionConstraintRace_RollsBackTheOutboxRowToo()
    {
        // Below the HTTP layer: two overlapping bookings saved through the real unit of work.
        // The friendly check is skipped on purpose, so it is the database that refuses the second one.
        await using var setup = Database.CreateDbContext();
        var customer = User.Create("Maria", "maria2@test.local", "hash", Role.Customer, TestTime.Now).Value;
        var service = new ServiceBuilder().WithDuration(30).Build();
        var professional = new ProfessionalBuilder().Performing(service).WorkingWeekdays().Build();
        setup.AddRange(customer, service, professional);
        await setup.SaveChangesAsync(Ct);

        async Task SaveBookingAsync(DateTimeOffset start)
        {
            await using var db = Database.CreateDbContext();
            new AppointmentRepository(db).Add(new AppointmentBuilder()
                .ForCustomer(customer.Id).WithProfessional(professional.Id).WithService(service).StartingAt(start).Build());
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await SaveBookingAsync(TuesdayTenLocal);
        var race = () => SaveBookingAsync(TuesdayTenLocal.AddMinutes(15));

        await race.Should().ThrowAsync<PersistenceConflictException>();
        (await OutboxAsync()).Should().ContainSingle();
    }
}

/// <summary>Publishing: <see cref="OutboxProcessor"/> against a stub WebhookRelay.</summary>
public class OutboxProcessorTests(DatabaseFixture database) : DatabaseTest(database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OutboxOptions Options(int batchSize = 20) => new()
    {
        RelayUrl = new Uri("http://relay.test"),
        ApiKey = "wr_test_key",
        BatchSize = batchSize,
    };

    private static OutboxProcessor CreateProcessor(SchedulingDbContext db, StubRelay relay, int batchSize = 20)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options(batchSize));
        return new OutboxProcessor(
            db,
            new WebhookRelayClient(new HttpClient(relay), options),
            options,
            new FakeTimeProvider(TestTime.Now.AddMinutes(1)),
            NullLogger<OutboxProcessor>.Instance);
    }

    private async Task<List<OutboxMessage>> GivenMessagesAsync(int count)
    {
        await using var db = Database.CreateDbContext();
        var messages = Enumerable.Range(0, count)
            .Select(i => new OutboxMessage(Guid.CreateVersion7(), "appointment.created", $$"""{"n":{{i}}}""", TestTime.Now.AddSeconds(i)))
            .ToList();
        db.OutboxMessages.AddRange(messages);
        await db.SaveChangesAsync(Ct);
        return messages;
    }

    private async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var db = Database.CreateDbContext();
        return await db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync(Ct);
    }

    private async Task<OutboxBatchResult> ProcessAsync(StubRelay relay, int batchSize = 20)
    {
        await using var db = Database.CreateDbContext();
        return await CreateProcessor(db, relay, batchSize).ProcessBatchAsync(Ct);
    }

    [Fact]
    public async Task Publishes_PendingMessages_InOrder_AndMarksThemProcessed()
    {
        var messages = await GivenMessagesAsync(3);
        var relay = new StubRelay();

        var result = await ProcessAsync(relay);

        result.Should().Be(new OutboxBatchResult(3, 0, 0));
        var requests = relay.Requests.ToList();
        requests.Should().HaveCount(3);
        requests.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Post);
            r.Uri.Should().Be(new Uri("http://relay.test/events"));
            r.Authorization.Should().Be("Bearer wr_test_key");
        });

        using var first = JsonDocument.Parse(requests[0].Body);
        first.RootElement.GetProperty("id").GetString().Should().Be(messages[0].Id.ToString());
        first.RootElement.GetProperty("type").GetString().Should().Be("appointment.created");
        first.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(TestTime.Now);
        first.RootElement.GetProperty("data").GetProperty("n").GetInt32().Should().Be(0);

        (await OutboxAsync()).Should().AllSatisfy(m =>
        {
            m.ProcessedAt.Should().Be(TestTime.Now.AddMinutes(1));
            m.Attempts.Should().Be(1);
        });
    }

    [Fact]
    public async Task AlreadyReceived_200_CountsAsPublished()
    {
        await GivenMessagesAsync(1);
        var relay = new StubRelay();
        relay.RespondWith(HttpStatusCode.OK);

        await ProcessAsync(relay);

        (await OutboxAsync()).Single().ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RelayDown_KeepsMessagesPending_AndStopsTheBatch()
    {
        await GivenMessagesAsync(3);
        var relay = new StubRelay();
        relay.RespondWith(HttpStatusCode.ServiceUnavailable);

        var result = await ProcessAsync(relay);

        result.Should().Be(new OutboxBatchResult(0, 0, 1));
        relay.Requests.Should().ContainSingle("the rest of the batch waits, in order");
        var outbox = await OutboxAsync();
        outbox.Should().AllSatisfy(m => m.ProcessedAt.Should().BeNull());
        outbox[0].Attempts.Should().Be(1);
        outbox[0].LastError.Should().Be("HTTP 503");
    }

    [Fact]
    public async Task NetworkError_IsTransient()
    {
        await GivenMessagesAsync(1);
        var relay = new StubRelay();
        relay.FailWith(new HttpRequestException("Connection refused"));

        var result = await ProcessAsync(relay);

        result.Failed.Should().Be(1);
        (await OutboxAsync()).Single().Should().Match<OutboxMessage>(m => m.ProcessedAt == null && m.LastError == "Connection refused");
    }

    [Fact]
    public async Task RelayRecovers_TheSameMessageIsPublishedLater()
    {
        var messages = await GivenMessagesAsync(1);
        var relay = new StubRelay();
        relay.RespondWith(HttpStatusCode.BadGateway, HttpStatusCode.Accepted);

        await ProcessAsync(relay);
        await ProcessAsync(relay);

        relay.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("id").GetString())
            .Should().Equal(messages[0].Id.ToString(), messages[0].Id.ToString());
        (await OutboxAsync()).Single().Should().Match<OutboxMessage>(m => m.ProcessedAt != null && m.Attempts == 2 && m.LastError == null);
    }

    [Fact]
    public async Task PermanentRejection_ClosesTheMessage_SoItCannotBlockTheQueue()
    {
        await GivenMessagesAsync(2);
        var relay = new StubRelay();
        relay.RespondWith(HttpStatusCode.BadRequest, HttpStatusCode.Accepted);

        var result = await ProcessAsync(relay);

        result.Should().Be(new OutboxBatchResult(1, 1, 0));
        var outbox = await OutboxAsync();
        outbox[0].Should().Match<OutboxMessage>(m => m.ProcessedAt != null && m.LastError == "HTTP 400");
        outbox[1].Should().Match<OutboxMessage>(m => m.ProcessedAt != null && m.LastError == null);
    }

    [Fact]
    public async Task RespectsTheBatchSize()
    {
        await GivenMessagesAsync(5);

        var result = await ProcessAsync(new StubRelay(), batchSize: 2);

        result.Published.Should().Be(2);
        (await OutboxAsync()).Count(m => m.ProcessedAt == null).Should().Be(3);
    }

    [Fact]
    public async Task ConcurrentDispatchers_NeverPublishTheSameMessageTwice()
    {
        // FOR UPDATE SKIP LOCKED: the second instance skips the rows the first one holds.
        await GivenMessagesAsync(10);
        var relay = new StubRelay { Latency = TimeSpan.FromMilliseconds(30) };

        await Task.WhenAll(
            ProcessAsync(relay, batchSize: 4),
            ProcessAsync(relay, batchSize: 4),
            ProcessAsync(relay, batchSize: 4));

        var ids = relay.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement.GetProperty("id").GetString()).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().HaveCount(10);
        (await OutboxAsync()).Should().AllSatisfy(m => m.ProcessedAt.Should().NotBeNull());
    }
}

/// <summary>The hosted dispatcher, running inside the real API.</summary>
public class OutboxDispatcherTests(DatabaseFixture database) : ApiTest(database)
{
    private readonly StubRelay _relay = new();

    protected override SchedulingApiFactory CreateFactory() => new(
        Database,
        new Dictionary<string, string>
        {
            ["Outbox:RelayUrl"] = "http://relay.test",
            ["Outbox:ApiKey"] = "wr_test_key",
            ["Outbox:PollIntervalSeconds"] = "5",
        },
        services => services.AddHttpClient<WebhookRelayClient>().ConfigurePrimaryHttpMessageHandler(() => _relay));

    [Fact]
    public async Task Booking_IsPublishedToWebhookRelay_ByTheBackgroundDispatcher()
    {
        var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync();
        var professional = await admin.CreateProfessionalAsync("Ana", [service]);
        var customer = await CreateCustomerClientAsync();

        var booked = await customer.PostAsJsonAsync(
            "/api/appointments",
            new { professionalId = professional.Id, serviceId = service.Id, startsAt = Iso(TestTime.At(2026, 12, 8, 13)) },
            Json,
            Ct);
        booked.StatusCode.Should().Be(HttpStatusCode.Created);

        // The dispatcher sleeps on the API's fake clock: moving it past the poll interval wakes it up.
        for (var i = 0; i < 50 && _relay.Requests.IsEmpty; i++)
        {
            Clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(100, Ct);
        }

        var request = _relay.Requests.Should().ContainSingle().Subject;
        using var body = JsonDocument.Parse(request.Body);
        body.RootElement.GetProperty("type").GetString().Should().Be("appointment.created");

        for (var i = 0; i < 50; i++)
        {
            await using var db = Database.CreateDbContext();
            if (await db.OutboxMessages.AllAsync(m => m.ProcessedAt != null, Ct))
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        throw new InvalidOperationException("The outbox message was published but never marked as processed.");
    }

    [Fact]
    public void Dispatcher_IsNotRegistered_WithoutRelaySettings()
    {
        using var plain = new SchedulingApiFactory(Database);

        plain.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should().NotContain(s => s is OutboxDispatcher);
    }
}
