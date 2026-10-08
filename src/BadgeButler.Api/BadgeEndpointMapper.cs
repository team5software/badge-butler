// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

using T5S.BadgeButler.Api.Models;
using T5S.BadgeButler.Api.Persistence;
using T5S.BadgeButler.Api.RequestDtos;

namespace T5S.BadgeButler.Api;

public static class BadgeEndpointMapper
{
    private const string BadgeKeyHeaderName = "X-Badge-Key";

    public static IEndpointRouteBuilder MapBadges(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/badges/{key}", async (string key, IApplicationStorage store, HttpResponse response) =>
            {
                Badge? badge = await store.GetAsync(key);
                if (badge is null)
                {
                    return Results.NotFound();
                }

                // Values change independently of the URL, so this must never be cached as static.
                response.Headers.CacheControl = "no-cache";
                return Results.Text(BadgeSvgRenderer.Render(badge.Label, badge.Message, badge.Appearance), "image/svg+xml");
            })
            .WithName("GetBadge")
            .WithTags("Badges")
            .WithSummary("Get a badge")
            .WithDescription("""
                             Renders the badge's current label/message/color as an SVG image.
                             Never requires authentication.
                             """)
            .Produces(StatusCodes.Status200OK, typeof(string), "image/svg+xml")
            .Produces(StatusCodes.Status404NotFound);

        builder.MapPut("/badges/{key}", async (string key, BadgeWrite request, HttpRequest httpRequest, IApplicationStorage store) =>
            {
                if (string.IsNullOrWhiteSpace(request.Label)) { return Results.BadRequest("label is required."); }

                if (string.IsNullOrWhiteSpace(request.Message)) { return Results.BadRequest("message is required."); }

                if (!TryResolveColor(request.Color, out string color, out string? colorError)) { return Results.BadRequest(colorError); }

                BadgeAppearance appearance = BadgeSvgRenderer.CalculateAppearance(request with { Color = color });
                string? providedBadgeKey = GetProvidedBadgeKey(httpRequest);

                UpsertResult result = await store.UpsertAsync(key, request.Label, request.Message, appearance, providedBadgeKey);
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
            .WithDescription("""
                             Insert-or-replace: creates a badge if the key does not exist, otherwise fully replaces it.
                             color is a CSS/SVG color name or a hex code with an optional '#' prefix.
                             X-Badge-Key optionally protects the badge and must be provided for subsequent updates of the badge.
                             To change X-Badge-Key, the badge must be deleted and recreated with a new value.
                             X-API-Key is required when Auth:ApiKey is configured on this deployment.
                             """)
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        builder.MapDelete("/badges/{key}", async (string key, HttpRequest httpRequest, IApplicationStorage store) =>
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
            .WithDescription("""
                             X-Badge-Key must match if this badge has one set.
                             X-API-Key is required when Auth:ApiKey is configured on this deployment.
                             """)
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
        if (BadgeSvgRenderer.TryNormalizeColor(candidate, out color))
        {
            error = null;
            return true;
        }

        color = "";
        error = "color must be a valid CSS/SVG color name or a 3- or 6-digit hex code.";
        return false;
    }
}
