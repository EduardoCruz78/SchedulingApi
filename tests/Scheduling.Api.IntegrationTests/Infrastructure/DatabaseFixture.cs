using Microsoft.EntityFrameworkCore;
using Scheduling.Api.IntegrationTests.Infrastructure;
using Scheduling.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

// Tests share one database and reset it between tests, so they run one at a time (see xunit.runner.json).
[assembly: AssemblyFixture(typeof(DatabaseFixture))]

namespace Scheduling.Api.IntegrationTests.Infrastructure;

/// <summary>
/// One real PostgreSQL for the whole test run. By default a throwaway Docker container (Testcontainers).
/// Set SCHEDULING_TEST_DB to a connection string to use an existing server instead;
/// point it at a dedicated, empty database because tests truncate every table.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string ExternalConnectionVariable = "SCHEDULING_TEST_DB";

    private PostgreSqlContainer? _container;

    public string ConnectionString { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalConnectionVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public SchedulingDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<SchedulingDbContext>();
        SchedulingDbContext.Configure(builder, ConnectionString);
        return new SchedulingDbContext(builder.Options);
    }

    /// <summary>Removes all rows, keeping the schema.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE outbox_messages, appointments, time_offs, professional_services, professional_working_hours, professionals, services, users CASCADE;");
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>Base class: each test starts with an empty database.</summary>
public abstract class DatabaseTest(DatabaseFixture database) : IAsyncLifetime
{
    protected DatabaseFixture Database { get; } = database;

    public virtual async ValueTask InitializeAsync() => await Database.ResetAsync();

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
