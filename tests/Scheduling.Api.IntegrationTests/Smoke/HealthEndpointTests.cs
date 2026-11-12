using System.Net;
using Scheduling.Api.IntegrationTests.Infrastructure;

namespace Scheduling.Api.IntegrationTests.Smoke;

public class HealthEndpointTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Health_WithDatabaseReachable_ReturnsHealthy()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("Healthy");
    }
}
