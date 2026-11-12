using System.Net.Http.Json;
using Scheduling.Application.Professionals;
using Scheduling.Application.Services;

namespace Scheduling.Api.IntegrationTests.Infrastructure;

/// <summary>Builds catalog data through the real admin endpoints, so tests exercise the same path a client would.</summary>
public static class CatalogApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<ServiceDto> CreateServiceAsync(this HttpClient admin, string name = "Haircut", int durationMinutes = 30, decimal price = 50m)
    {
        var response = await admin.PostAsJsonAsync("/api/services", new { name, durationMinutes, price }, ApiTest.Json, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServiceDto>(ApiTest.Json, Ct))!;
    }

    /// <summary>Creates an active professional performing the services, working Monday-Friday 09:00-18:00 by default.</summary>
    public static async Task<ProfessionalDto> CreateProfessionalAsync(
        this HttpClient admin, string name, IEnumerable<ServiceDto> services, string start = "09:00", string end = "18:00")
    {
        var created = await admin.PostAsJsonAsync("/api/professionals", new { name }, ApiTest.Json, Ct);
        created.EnsureSuccessStatusCode();
        var professional = (await created.Content.ReadFromJsonAsync<ProfessionalDto>(ApiTest.Json, Ct))!;

        var setServices = await admin.PutAsJsonAsync(
            $"/api/professionals/{professional.Id}/services", new { serviceIds = services.Select(s => s.Id) }, ApiTest.Json, Ct);
        setServices.EnsureSuccessStatusCode();

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var setHours = await admin.PutAsJsonAsync(
            $"/api/professionals/{professional.Id}/working-hours",
            new { intervals = weekdays.Select(day => new { day, start, end }) },
            ApiTest.Json,
            Ct);
        setHours.EnsureSuccessStatusCode();

        return (await setHours.Content.ReadFromJsonAsync<ProfessionalDto>(ApiTest.Json, Ct))!;
    }
}
