using Scheduling.Application.Common;
using Scheduling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<SchedulingOptions>()
    .BindConfiguration(SchedulingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<BusinessOptions>()
    .BindConfiguration(BusinessOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(o => TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _), "Business:TimeZone is not a known time zone id.")
    .ValidateOnStart();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.MapOpenApi();
app.MapHealthChecks("/health");

await app.RunAsync();

/// <summary>Entry point marker so integration tests can reference the API via WebApplicationFactory.</summary>
public partial class Program;
