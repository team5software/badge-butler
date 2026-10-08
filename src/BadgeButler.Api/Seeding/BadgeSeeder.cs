// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading;
using System.Threading.Tasks;

using T5S.BadgeButler.Api.Persistence;
using T5S.BadgeButler.Api.RequestDtos;

namespace T5S.BadgeButler.Api.Seeding;

public static class BadgeSeeder
{
    private static readonly (string Key, string Label, string Message, string Color)[] Samples =
    [
        ("sample-build", "build", "passing", "4c1"),
        ("sample-tests", "tests", "42 passed", "4c1"),
        ("sample-coverage", "coverage", "87%", "97ca00"),
        ("sample-release", "release", "v0.2.0", "blue"),
        ("sample-failing", "build", "failing", "e05d44"),
        ("sample-long-content", "a fairly long label for width testing", "an equally long message value here", "orange"),
    ];

    public static async Task SeedAsync(IApplicationStorage store, CancellationToken ct = default)
    {
        foreach ((string key, string label, string message, string color) in Samples)
        {
            if (await store.GetAsync(key, ct) is not null)
            {
                continue;
            }

            BadgeSvgRenderer.TryNormalizeColor(color, out string normalizedColor);
            BadgeAppearance metrics = BadgeSvgRenderer.CalculateAppearance(new BadgeWrite(label, message, normalizedColor));
            await store.UpsertAsync(key, label, message, metrics, null, ct);
        }
    }
}
