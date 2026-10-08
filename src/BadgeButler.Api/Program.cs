// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Scalar.AspNetCore;

using T5S.BadgeButler.Api;
using T5S.BadgeButler.Api.Authentication;
using T5S.BadgeButler.Api.Persistence;
using T5S.BadgeButler.Api.Seeding;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string appVersion = builder.Configuration["App:Version"] ?? "dev";
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Badge Butler API";
        document.Info.Description = "Stores and serves status badges.";
        document.Info.Version = appVersion;
        return Task.CompletedTask;
    });
});

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(ApiKeyAuthenticationOptions.DefaultSchema)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.DefaultSchema, _ => { });
builder.Services.AddAuthorization();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IApplicationStorage, InMemoryStorage>();
}
else
{
    string connectionString = builder.Configuration.GetConnectionString("BadgeButler")
                              ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:BadgeButler");
    builder.Services.AddSingleton<IApplicationStorage>(services => new PostgresStorage(connectionString, services.GetRequiredService<ILogger<PostgresStorage>>()));
}

WebApplication app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseHttpsRedirection();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapBadges();

IApplicationStorage applicationStorage = app.Services.GetRequiredService<IApplicationStorage>();
AppearanceCalculator appearanceCalculator = new(BadgeSvgRenderer.AppearanceFingerprint, BadgeSvgRenderer.CalculateAppearance);
if (await applicationStorage.InitializeAsync(appearanceCalculator) == InitializationResult.Failed)
{
    throw new InvalidOperationException("Database initialization failed - schema version is newer than this build supports, or a migration step failed.");
}

if (app.Environment.IsDevelopment())
{
    await BadgeSeeder.SeedAsync(applicationStorage);
}

app.Run();
