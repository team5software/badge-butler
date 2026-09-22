using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Scalar.AspNetCore;

using T5S.BadgeButler.Api;

var builder = WebApplication.CreateBuilder(args);

// Set by the Helm chart from the same value used for the image tag, so the OpenAPI doc always
// reports the version actually running, not whatever was baked in at compile time.
var appVersion = builder.Configuration["App:Version"] ?? "dev";

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Badge Butler API";
        document.Info.Description = "Stores and serves status badges (build, coverage, etc.) for repos.";
        document.Info.Version = appVersion;
        return Task.CompletedTask;
    });
});
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("BadgeButler")
    ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:BadgeButler");
builder.Services.AddSingleton(new BadgeStore(connectionString));

// Fixed key, set at deployment time. Unset -> no auth, writes are open. GET is never gated.
var apiKey = builder.Configuration["Auth:ApiKey"];
apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;

var app = builder.Build();

app.UseExceptionHandler();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

// Liveness/readiness probe target for the container/Helm chart. Not part of the OpenAPI doc -
// the health-checks middleware excludes its own endpoint from API description by default.
app.MapHealthChecks("/health");

await app.Services.GetRequiredService<BadgeStore>().InitializeAsync();

app.MapGet("/badges/{key}", async (string key, BadgeStore store, HttpResponse response) =>
    {
        var badge = await store.GetAsync(key);
        if (badge is null)
        {
            return Results.NotFound();
        }

        // Values change independently of the URL, so this must never be cached as static.
        response.Headers.CacheControl = "no-cache";
        return Results.Text(BadgeSvgRenderer.Render(badge.Label, badge.Message, badge.Color), "image/svg+xml");
    })
    .WithName("GetBadge")
    .WithTags("Badges")
    .WithSummary("Get a badge")
    .WithDescription("Renders the badge's current label/message/color as an SVG image. Never requires authentication.")
    .Produces(StatusCodes.Status200OK, typeof(string), "image/svg+xml")
    .Produces(StatusCodes.Status404NotFound);

app.MapPut("/badges/{key}", async (string key, BadgeUpdate update, HttpRequest request, BadgeStore store) =>
    {
        if (!IsAuthorized(request))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(update.Label) || string.IsNullOrWhiteSpace(update.Message))
        {
            return Results.BadRequest("label and message are required.");
        }

        var color = string.IsNullOrWhiteSpace(update.Color) ? "lightgrey" : update.Color;
        await store.UpsertAsync(key, update.Label, update.Message, color);
        return Results.NoContent();
    })
    .WithName("UpsertBadge")
    .WithTags("Badges")
    .WithSummary("Create or update a badge")
    .WithDescription("Upserts the badge's label/message/color. color defaults to \"lightgrey\" when omitted. " +
        "Requires X-Api-Key when Auth:ApiKey is configured on this deployment; open otherwise.")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized);

app.MapDelete("/badges/{key}", async (string key, HttpRequest request, BadgeStore store) =>
    {
        if (!IsAuthorized(request))
        {
            return Results.Unauthorized();
        }

        return await store.DeleteAsync(key) ? Results.NoContent() : Results.NotFound();
    })
    .WithName("DeleteBadge")
    .WithTags("Badges")
    .WithSummary("Delete a badge")
    .WithDescription("Requires X-Api-Key when Auth:ApiKey is configured on this deployment; open otherwise.")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status404NotFound);

app.Run();

bool IsAuthorized(HttpRequest request) =>
    apiKey is null
    || (request.Headers.TryGetValue("X-Api-Key", out var provided) && provided == apiKey);
