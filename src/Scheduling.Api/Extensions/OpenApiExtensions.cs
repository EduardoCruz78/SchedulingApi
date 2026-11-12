using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Scheduling.Api.Extensions;

/// <summary>Declares the JWT bearer scheme so Scalar shows an "Authorize" input and sends the token.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeId = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "SchedulingApi";
        document.Info.Description =
            "Appointment booking for small businesses. Log in with POST /api/auth/login and paste the accessToken into Authorize.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Access token from POST /api/auth/login.",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeId, document)] = [],
        });

        return Task.CompletedTask;
    }
}
