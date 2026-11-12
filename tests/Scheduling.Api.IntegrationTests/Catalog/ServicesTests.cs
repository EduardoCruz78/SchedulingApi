using System.Net;
using System.Net.Http.Json;
using Scheduling.Api.IntegrationTests.Infrastructure;
using Scheduling.Application.Common;
using Scheduling.Application.Services;

namespace Scheduling.Api.IntegrationTests.Catalog;

public class ServicesTests(DatabaseFixture database) : ApiTest(database)
{
    [Fact]
    public async Task Create_AsAdmin_Returns201WithLocation()
    {
        using var admin = await CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/services", new { name = "Haircut", durationMinutes = 45, price = 60.5m }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var service = await ReadAsync<ServiceDto>(response);
        response.Headers.Location!.ToString().Should().EndWith($"/api/services/{service.Id}");
        service.Should().BeEquivalentTo(new { Name = "Haircut", DurationMinutes = 45, Price = 60.5m, IsActive = true });
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/services", new { name = "Haircut", durationMinutes = 30, price = 50 }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_AsCustomer_Returns403()
    {
        using var customer = await CreateCustomerClientAsync();

        var response = await customer.PostAsJsonAsync("/api/services", new { name = "Haircut", durationMinutes = 30, price = 50 }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithInvalidDurationAndPrice_Returns400WithFieldErrors()
    {
        using var admin = await CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/services", new { name = "Haircut", durationMinutes = 20, price = -1 }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).Errors.Should().ContainKeys("durationMinutes", "price");
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(new Uri($"/api/services/{Guid.NewGuid()}", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblemAsync(response)).Code.Should().Be("Service.NotFound");
    }

    [Fact]
    public async Task Update_ChangesTheService()
    {
        using var admin = await CreateAdminClientAsync();
        var service = await admin.CreateServiceAsync();

        var response = await admin.PutAsJsonAsync(
            $"/api/services/{service.Id}", new { name = "Premium haircut", durationMinutes = 60, price = 90, isActive = true }, Json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<ServiceDto>(response)).Should().BeEquivalentTo(new { Name = "Premium haircut", DurationMinutes = 60, Price = 90m });
    }

    // R12
    [Fact]
    public async Task Delete_DeactivatesAndHidesFromPublicListButKeepsTheRecord()
    {
        using var admin = await CreateAdminClientAsync();
        var kept = await admin.CreateServiceAsync("Haircut");
        var removed = await admin.CreateServiceAsync("Beard");
        using var anonymous = CreateClient();

        var delete = await admin.DeleteAsync(new Uri($"/api/services/{removed.Id}", UriKind.Relative), Ct);
        var publicList = await anonymous.GetFromJsonAsync<PagedList<ServiceDto>>("/api/services?includeInactive=true", Json, Ct);
        var adminList = await admin.GetFromJsonAsync<PagedList<ServiceDto>>("/api/services?includeInactive=true", Json, Ct);
        var stillThere = await anonymous.GetFromJsonAsync<ServiceDto>($"/api/services/{removed.Id}", Json, Ct);

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        publicList!.Items.Select(s => s.Id).Should().Equal(kept.Id);
        adminList!.TotalCount.Should().Be(2);
        stillThere!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task List_IsPaginated()
    {
        using var admin = await CreateAdminClientAsync();
        await admin.CreateServiceAsync("A");
        await admin.CreateServiceAsync("B");
        await admin.CreateServiceAsync("C");

        var page = await admin.GetFromJsonAsync<PagedList<ServiceDto>>("/api/services?page=2&pageSize=2", Json, Ct);

        page!.Items.Select(s => s.Name).Should().Equal("C");
        page.Should().BeEquivalentTo(new { Page = 2, PageSize = 2, TotalCount = 3 });
    }
}
