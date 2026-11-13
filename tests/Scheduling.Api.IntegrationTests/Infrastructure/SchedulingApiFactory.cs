using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Scheduling.Application.Abstractions;
using Scheduling.Domain.Users;
using Scheduling.Infrastructure.Auth;
using Scheduling.Tests.Common;

namespace Scheduling.Api.IntegrationTests.Infrastructure;

/// <summary>Runs the real API in memory against the test database, with a controllable clock.</summary>
public sealed class SchedulingApiFactory(
    DatabaseFixture database,
    IReadOnlyDictionary<string, string>? settings = null,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(TestTime.Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", database.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Database:Seed", "false");
        builder.UseSetting("Jwt:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("RateLimiting:AuthPermitLimit", "10000");

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            configureServices?.Invoke(services);
        });
    }
}

/// <summary>Base class for HTTP tests: fresh database and a fresh API instance per test.</summary>
public abstract class ApiTest(DatabaseFixture database) : DatabaseTest(database)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private SchedulingApiFactory? _factory;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected SchedulingApiFactory Factory => _factory ??= CreateFactory();

    protected FakeTimeProvider Clock => Factory.Clock;

    /// <summary>Override to pass extra configuration to the API under test.</summary>
    protected virtual SchedulingApiFactory CreateFactory() => new(Database);

    protected HttpClient CreateClient() => Factory.CreateClient();

    protected async Task<HttpClient> CreateCustomerClientAsync(string email = "maria@test.local", string name = "Maria")
    {
        var client = CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new { name, email, password = "Password123" }, Json, Ct);
        register.EnsureSuccessStatusCode();
        await AuthenticateAsync(client, email, "Password123");
        return client;
    }

    protected async Task<HttpClient> CreateAdminClientAsync()
    {
        const string email = "admin@test.local";
        const string password = "AdminPassword1";

        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            if (await users.GetByEmailAsync(email, Ct) is null)
            {
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                users.Add(User.Create("Admin", email, hasher.Hash(password), Role.Admin, Clock.GetUtcNow()).Value);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
            }
        }

        var client = CreateClient();
        await AuthenticateAsync(client, email, password);
        return client;
    }

    private readonly Dictionary<HttpClient, (string Email, string Password)> _credentials = [];

    protected async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Json, Ct);
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<AuthToken>(Json, Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        _credentials[client] = (email, password);
    }

    /// <summary>
    /// Moves the API clock and logs every authenticated client in again:
    /// tokens expire after an hour of (fake) time, exactly as they would in production.
    /// </summary>
    protected async Task TravelToAsync(DateTimeOffset instant)
    {
        Clock.SetUtcNow(instant);
        foreach (var (client, (email, password)) in _credentials.ToList())
        {
            await AuthenticateAsync(client, email, password);
        }
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;

    protected static async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response) =>
        await ReadAsync<ProblemResponse>(response);

    protected static string Iso(DateTimeOffset instant) => instant.ToString("O", CultureInfo.InvariantCulture);

    public override async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Subset of RFC 9457 ProblemDetails (plus the "code" and "errors" extensions) used by assertions.</summary>
public sealed record ProblemResponse(
    int Status,
    string? Title,
    string? Detail,
    string? Code,
    string? TraceId,
    Dictionary<string, string[]>? Errors);
