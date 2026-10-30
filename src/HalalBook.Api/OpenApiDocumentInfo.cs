using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HalalBook.Api;

/// <summary>Adds the API description and the JWT "Authorize" button to the OpenAPI document.</summary>
public sealed class OpenApiDocumentInfo : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "HalalBook API",
            Version = "v1",
            Description = "REST API for HalalBook, a social network for the Muslim community. " +
                          "Sign in with POST /api/auth/login, then click Authorize and paste the token.",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the token from /api/auth/login (without the word Bearer).",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}
