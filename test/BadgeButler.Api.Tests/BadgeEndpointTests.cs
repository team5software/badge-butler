// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using AwesomeAssertions;

using Xunit;

namespace T5S.BadgeButler.Api.Tests;

/// <summary>HTTP-level tests of the real app: routing, binding, validation, auth and rendering together.</summary>
public class BadgeEndpointTests
{
    private const string ApiKey = "test-api-key";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_IsServedWithTheApiTitle()
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Badge Butler API").And.Contain("/badges/{key}");
    }

    [Fact]
    public async Task Get_InDevelopment_ServesTheSeededSampleBadges()
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/badges/sample-build", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_UnknownBadge_ReturnsNotFound()
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/badges/missing", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_ThenGet_CreatesAndRendersTheBadgeUncached()
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage put = await PutAsync(client, "k1", new { label = "build", message = "passing", color = "4c1" });
        HttpResponseMessage get = await client.GetAsync("/badges/k1", Ct);
        string svg = await get.Content.ReadAsStringAsync(Ct);

        put.StatusCode.Should().Be(HttpStatusCode.Created);
        put.Headers.Location!.ToString().Should().Be("/badges/k1");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        get.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        get.Headers.CacheControl!.NoCache.Should().BeTrue();
        svg.Should().Contain(">build<").And.Contain(">passing<").And.Contain("fill=\"#44CC11\"").And.Contain("fill=\"#000000\"");
    }

    [Fact]
    public async Task Put_OnExistingBadge_ReplacesItAndReturnsNoContent()
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await PutAsync(client, "k1", new { label = "build", message = "passing", color = "4c1" });

        HttpResponseMessage put = await PutAsync(client, "k1", new { label = "build", message = "failing", color = "e05d44" });
        string svg = await client.GetStringAsync("/badges/k1", Ct);

        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        svg.Should().Contain(">failing<").And.Contain("fill=\"#E05D44\"").And.NotContain(">passing<");
    }

    [Theory]
    [InlineData("blue", "0000FF")]
    [InlineData("#00f", "0000FF")]
    [InlineData(null, "D3D3D3")] // omitted -> lightgrey
    public async Task Put_NormalizesTheColor(string? color, string expectedHex)
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        await PutAsync(client, "k1", new { label = "release", message = "v1.0.0", color });
        string svg = await client.GetStringAsync("/badges/k1", Ct);

        svg.Should().Contain($"fill=\"#{expectedHex}\"");
    }

    [Theory]
    [InlineData("", "passing", "4c1", "label is required.")]
    [InlineData("build", " ", "4c1", "message is required.")]
    [InlineData("build", "passing", "notacolor", "color must be")]
    public async Task Put_WithInvalidBody_ReturnsBadRequestAndStoresNothing(string label, string message, string color, string expectedError)
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage put = await PutAsync(client, "k1", new { label, message, color });

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await put.Content.ReadAsStringAsync(Ct)).Should().Contain(expectedError);
        (await client.GetAsync("/badges/k1", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BadgeKey_ProtectsUpdatesAndDeletesOfThatBadge()
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        await PutAsync(client, "k1", new { label = "build", message = "passing" }, badgeKey: "secret");

        (await PutAsync(client, "k1", new { label = "build", message = "hijacked" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PutAsync(client, "k1", new { label = "build", message = "hijacked" }, badgeKey: "wrong")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await DeleteAsync(client, "k1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetStringAsync("/badges/k1", Ct)).Should().Contain(">passing<");

        (await PutAsync(client, "k1", new { label = "build", message = "failing" }, badgeKey: "secret")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DeleteAsync(client, "k1", badgeKey: "secret")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/badges/k1", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UnknownBadge_ReturnsNotFound()
    {
        await using BadgeApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await DeleteAsync(client, "missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApiKey_WhenConfigured_IsRequiredForWritesButNotForReads()
    {
        await using BadgeApiFactory factory = new(apiKey: ApiKey);
        using HttpClient client = factory.CreateClient();
        object body = new { label = "build", message = "passing" };

        (await PutAsync(client, "k1", body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PutAsync(client, "k1", body, apiKey: "wrong")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PutAsync(client, "k1", body, apiKey: ApiKey)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.GetAsync("/badges/k1", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DeleteAsync(client, "k1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await DeleteAsync(client, "k1", apiKey: ApiKey)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Production_AgainstPostgres_InitializesTheDatabaseAndServesBadges()
    {
        await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync(Ct);
        await using BadgeApiFactory factory = new(apiKey: ApiKey, postgresConnectionString: database.ConnectionString);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage put = await PutAsync(client, "k1", new { label = "build", message = "passing", color = "4c1" }, apiKey: ApiKey, badgeKey: "secret");
        string svg = await client.GetStringAsync("/badges/k1", Ct);

        put.StatusCode.Should().Be(HttpStatusCode.Created);
        svg.Should().Contain(">passing<").And.Contain("fill=\"#44CC11\"");
        (await database.ScalarAsync<string>("SELECT access_key FROM badges WHERE key = 'k1'", Ct)).Should().Be("secret");
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string key, object body, string? apiKey = null, string? badgeKey = null)
    {
        HttpRequestMessage request = new(HttpMethod.Put, $"/badges/{key}") { Content = JsonContent.Create(body) };
        AddKeys(request, apiKey, badgeKey);
        return client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string key, string? apiKey = null, string? badgeKey = null)
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"/badges/{key}");
        AddKeys(request, apiKey, badgeKey);
        return client.SendAsync(request, Ct);
    }

    private static void AddKeys(HttpRequestMessage request, string? apiKey, string? badgeKey)
    {
        if (apiKey is not null)
        {
            request.Headers.Add("X-API-Key", apiKey);
        }

        if (badgeKey is not null)
        {
            request.Headers.Add("X-Badge-Key", badgeKey);
        }
    }
}
