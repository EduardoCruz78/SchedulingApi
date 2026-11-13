using System.Text.Json;
using Scheduling.Api.IntegrationTests.Infrastructure;

namespace Scheduling.Api.IntegrationTests.Contract;

/// <summary>
/// The OpenAPI document is a contract: the admin dashboard generates its TypeScript types from it.
/// It must describe what goes over the wire, not the CLR types behind it.
/// </summary>
public class OpenApiDocumentTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Enums_AreDescribedAsTheStringsTheApiSends()
    {
        var schemas = await GetSchemasAsync();

        var status = schemas.GetProperty("AppointmentStatus");
        // .NET describes string enums by their values; a numeric "type" would make clients expect 0..3.
        if (status.TryGetProperty("type", out var type))
        {
            type.GetString().Should().Be("string");
        }

        status.GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Scheduled", "Cancelled", "Completed", "NoShow");

        schemas.GetProperty("DayOfWeek").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().Contain(["Monday", "Sunday"]);
    }

    [Fact]
    public async Task Numbers_AreDescribedAsPlainNumbers()
    {
        var schemas = await GetSchemasAsync();

        var price = schemas.GetProperty("ServiceDto").GetProperty("properties").GetProperty("price");
        price.GetProperty("type").ValueKind.Should().Be(JsonValueKind.String);
        price.GetProperty("type").GetString().Should().Be("number");

        var totalCount = schemas.GetProperty("PagedListOfServiceDto").GetProperty("properties").GetProperty("totalCount");
        totalCount.GetProperty("type").GetString().Should().Be("integer");
    }

    private async Task<JsonElement> GetSchemasAsync()
    {
        using var client = CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("components").GetProperty("schemas").Clone();
    }
}
