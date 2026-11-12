using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence;

public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<Professional> Professionals => Set<Professional>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    /// <summary>Single place for provider options, shared by DI and the design-time factory.</summary>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, ConfigureNpgsql)
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Needed by the exclusion constraint on appointments (ADR 0003): lets a GiST index mix "=" on uuid with "&&" on ranges.
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SchedulingDbContext).Assembly);
    }

    private static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history");
}

/// <summary>Used only by "dotnet ef" to build the model and generate migrations. No database connection is opened.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SchedulingDbContext>
{
    public SchedulingDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SchedulingDbContext>();
        SchedulingDbContext.Configure(builder, "Host=localhost;Database=scheduling_design_time");
        return new SchedulingDbContext(builder.Options);
    }
}
