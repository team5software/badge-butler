// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

using T5S.BadgeButler.Api.Persistence;

namespace T5S.BadgeButler.Api;

public static class BadgeEndpointMapper
{
    /// <summary>
    /// Per-badge access key, independent of the deployment-wide X-API-Key. Never carried in the
    /// request body - whatever this header holds becomes the badge's stored key on every
    /// successful write (see IBadgeStore.UpsertAsync), so a CI script just sends the same secret
    /// on every call, first one included, without needing to know whether the badge exists yet.
    /// </summary>
    public const string BadgeKeyHeaderName = "X-Badge-Key";

    public static IEndpointRouteBuilder MapBadges(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/badges/{key}", async (string key, IBadgeStore store, HttpResponse response) =>
            {
                Badge? badge = await store.GetAsync(key);
                if (badge is null)
                {
                    return Results.NotFound();
                }

                // Values change independently of the URL, so this must never be cached as static.
                response.Headers.CacheControl = "no-cache";
                return Results.Text(BadgeSvgRenderer.Render(badge.Label, badge.Message, badge.Color, badge.Metrics), "image/svg+xml");
            })
            .WithName("GetBadge")
            .WithTags("Badges")
            .WithSummary("Get a badge")
            .WithDescription("Renders the badge's current label/message/color as an SVG image. Never requires authentication.")
            .Produces(StatusCodes.Status200OK, typeof(string), "image/svg+xml")
            .Produces(StatusCodes.Status404NotFound);

        builder.MapPut("/badges/{key}", async (string key, BadgeWrite request, HttpRequest httpRequest, IBadgeStore store) =>
            {
                if (string.IsNullOrWhiteSpace(request.Label) || string.IsNullOrWhiteSpace(request.Message))
                {
                    return Results.BadRequest("label and message are required.");
                }

                if (!TryResolveColor(request.Color, out string color, out string? colorError))
                {
                    return Results.BadRequest(colorError);
                }

                BadgeMetrics metrics = BadgeSvgRenderer.Measure(request.Label, request.Message);
                string? providedBadgeKey = GetProvidedBadgeKey(httpRequest);

                UpsertResult result = await store.UpsertAsync(key, request.Label, request.Message, color, metrics, providedBadgeKey);
                return result switch
                {
                    UpsertResult.Created => Results.Created($"/badges/{key}", null),
                    UpsertResult.Updated => Results.NoContent(),
                    UpsertResult.Unauthorized => Results.Unauthorized(),
                    _ => Results.Problem(),
                };
            })
            .RequireAuthorization()
            .WithName("UpsertBadge")
            .WithTags("Badges")
            .WithSummary("Create or update a badge")
            .WithDescription("Insert-or-replace: creates the badge if key doesn't exist yet, otherwise fully replaces " +
                              "label/message/color. color is a CSS/SVG color name or hex code (with or without '#'), " +
                              "normalized to hex before storage; defaults to \"lightgrey\" when omitted. " +
                              "X-Badge-Key optionally protects this specific badge: if it already has a key, this " +
                              "request's X-Badge-Key must match it (401 otherwise); either way, whatever X-Badge-Key is " +
                              "sent (including none) becomes the badge's key going forward - so an unprotected badge " +
                              "stays open to anyone by design, same as it's already open to DELETE. " +
                              "Also requires X-API-Key when Auth:ApiKey is configured on this deployment.")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        builder.MapDelete("/badges/{key}", async (string key, HttpRequest httpRequest, IBadgeStore store) =>
            {
                string? providedBadgeKey = GetProvidedBadgeKey(httpRequest);
                DeleteResult result = await store.DeleteAsync(key, providedBadgeKey);
                return result switch
                {
                    DeleteResult.Success => Results.NoContent(),
                    DeleteResult.NotFound => Results.NotFound(),
                    DeleteResult.Unauthorized => Results.Unauthorized(),
                    _ => Results.Problem(),
                };
            })
            .RequireAuthorization()
            .WithName("DeleteBadge")
            .WithTags("Badges")
            .WithSummary("Delete a badge")
            .WithDescription("X-Badge-Key must match if this badge has one set. Requires X-API-Key when Auth:ApiKey is configured on this deployment.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return builder;
    }

    private static string? GetProvidedBadgeKey(HttpRequest request) =>
        request.Headers.TryGetValue(BadgeKeyHeaderName, out StringValues value) && !string.IsNullOrEmpty(value)
            ? value.ToString()
            : null;

    private static bool TryResolveColor(string? input, out string color, out string? error)
    {
        string candidate = string.IsNullOrWhiteSpace(input) ? "lightgrey" : input;
        if (BadgeSvgRenderer.TryNormalizeColor(candidate, out color!))
        {
            error = null;
            return true;
        }

        color = "";
        error = "color must be a valid CSS/SVG color name or a 3- or 6-digit hex code.";
        return false;
    }
}
