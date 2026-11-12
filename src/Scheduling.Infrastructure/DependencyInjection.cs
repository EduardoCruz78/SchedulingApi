using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Scheduling.Application.Abstractions;
using Scheduling.Infrastructure.Auth;
using Scheduling.Infrastructure.Persistence;
using Scheduling.Infrastructure.Persistence.Repositories;
using Scheduling.Infrastructure.Persistence.Seed;

namespace Scheduling.Infrastructure;

/// <summary>Startup database behavior, bound from the "Database" section.</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending migrations at startup. Convenient for the demo; a real pipeline would run a migration bundle.</summary>
    public bool MigrateOnStartup { get; init; }

    /// <summary>Insert demo data when the database is empty.</summary>
    public bool Seed { get; init; }
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<SchedulingDbContext>(options => SchedulingDbContext.Configure(options, connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IProfessionalRepository, ProfessionalRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddScoped<DataSeeder>();

        services.AddHealthChecks().AddDbContextCheck<SchedulingDbContext>("database");

        return services;
    }

    /// <summary>Runs migrations and seeding according to <see cref="DatabaseOptions"/>.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        if (options.MigrateOnStartup)
        {
            var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
        }

        if (options.Seed)
        {
            await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(cancellationToken);
        }
    }
}
