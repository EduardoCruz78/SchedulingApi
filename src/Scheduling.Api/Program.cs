using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using Scheduling.Api.Extensions;
using Scheduling.Api.Middleware;
using Scheduling.Application;
using Scheduling.Application.Common;
using Scheduling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------

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

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiSecurity(builder.Configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The OpenAPI generator reads these options, not the MVC ones above. Mirror the wire format here so the
// document says "Scheduled" (not 0) and "number" (not number-or-string): clients generate types from it.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

// The admin dashboard (portfolio project 3) calls this API from the browser.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// Behind a reverse proxy (Render), the client IP arrives in X-Forwarded-For. Needed for per-IP rate limiting.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

// ---------- Pipeline ----------

if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("SchedulingApi"));
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapControllers();

await app.Services.InitializeDatabaseAsync();
await app.RunAsync();

/// <summary>Entry point marker so integration tests can reference the API via WebApplicationFactory.</summary>
public partial class Program;
