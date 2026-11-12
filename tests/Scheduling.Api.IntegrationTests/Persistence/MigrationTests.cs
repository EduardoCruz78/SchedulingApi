using Microsoft.EntityFrameworkCore;
using Scheduling.Api.IntegrationTests.Infrastructure;

namespace Scheduling.Api.IntegrationTests.Persistence;

public class MigrationTests(DatabaseFixture database) : DatabaseTest(database)
{
    [Fact]
    public async Task Migrations_ApplyFromScratch_WithNothingPending()
    {
        await using var db = Database.CreateDbContext();

        var pending = await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        var applied = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        pending.Should().BeEmpty();
        applied.Should().Equal(db.Database.GetMigrations());
    }

    [Fact]
    public void Model_HasNoChangesMissingFromMigrations()
    {
        using var db = Database.CreateDbContext();

        db.Database.HasPendingModelChanges().Should().BeFalse("every model change needs a migration");
    }

    [Fact]
    public async Task ExclusionConstraint_Exists()
    {
        await using var db = Database.CreateDbContext();

        var count = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_constraint WHERE conname = 'ex_appointments_professional_overlap' AND contype = 'x'")
            .SingleAsync(TestContext.Current.CancellationToken);

        count.Should().Be(1);
    }
}
