using Npgsql;
using Scheduling.Infrastructure.Persistence;

namespace Scheduling.Api.IntegrationTests.Persistence;

public class ConnectionStringsTests
{
    [Fact]
    public void Normalize_KeyValueString_IsUnchanged()
    {
        const string value = "Host=localhost;Database=scheduling;Username=app;Password=secret";

        ConnectionStrings.Normalize(value).Should().Be(value);
    }

    [Fact]
    public void Normalize_ProviderUrl_BecomesNpgsqlString()
    {
        var normalized = ConnectionStrings.Normalize(
            "postgresql://app_user:p%40ss@ep-cool-name.sa-east-1.aws.neon.tech/scheduling?sslmode=require&channel_binding=require");

        var parsed = new NpgsqlConnectionStringBuilder(normalized);
        parsed.Host.Should().Be("ep-cool-name.sa-east-1.aws.neon.tech");
        parsed.Port.Should().Be(5432);
        parsed.Database.Should().Be("scheduling");
        parsed.Username.Should().Be("app_user");
        parsed.Password.Should().Be("p@ss");
        parsed.SslMode.Should().Be(SslMode.Require);
        parsed.ChannelBinding.Should().Be(ChannelBinding.Require);
    }

    [Fact]
    public void Normalize_UrlWithPortAndVerifyFull_IsParsed()
    {
        var parsed = new NpgsqlConnectionStringBuilder(
            ConnectionStrings.Normalize("postgres://u:p@db.internal:6543/app?sslmode=verify-full"));

        parsed.Port.Should().Be(6543);
        parsed.SslMode.Should().Be(SslMode.VerifyFull);
    }
}
