using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Scheduling.Tests.Common;

namespace Scheduling.Api.IntegrationTests.Infrastructure;

/// <summary>Runs the real API in memory against the test database, with a controllable clock.</summary>
public sealed class SchedulingApiFactory(DatabaseFixture database) : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new(TestTime.Now);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", database.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Database:Seed", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>Base class for HTTP tests: fresh database and a fresh API instance per test class.</summary>
public abstract class ApiTest(DatabaseFixture database) : DatabaseTest(database)
{
    private SchedulingApiFactory? _factory;

    protected SchedulingApiFactory Factory => _factory ??= new SchedulingApiFactory(Database);

    protected HttpClient CreateClient() => Factory.CreateClient();

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
