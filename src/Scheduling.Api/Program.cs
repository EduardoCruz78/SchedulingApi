var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

await app.RunAsync();

/// <summary>Entry point marker so integration tests can reference the API via WebApplicationFactory.</summary>
public partial class Program;
