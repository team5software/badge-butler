// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace T5S.BadgeButler.Api.Tests;

/// <summary>
/// Hosts the real app in memory. Without a connection string it runs as Development (in-memory
/// storage plus the dev seeder); with one it runs as Production against that Postgres database,
/// exactly the path the deployed app takes. Internal because the top-level Program class is.
/// </summary>
internal sealed class BadgeApiFactory(string? apiKey = null, string? postgresConnectionString = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(postgresConnectionString is null ? Environments.Development : Environments.Production);
        builder.UseSetting("Auth:ApiKey", apiKey ?? "");
        if (postgresConnectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:BadgeButler", postgresConnectionString);
        }
    }
}
