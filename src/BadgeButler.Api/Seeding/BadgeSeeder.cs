// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading;
using System.Threading.Tasks;

using T5S.BadgeButler.Api.Persistence;

namespace T5S.BadgeButler.Api.Seeding;

/// <summary>Dev-only sample data - see Program.cs, only ever called when IsDevelopment().</summary>
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

    public static async Task SeedAsync(IBadgeStore store, CancellationToken ct = default)
    {
        foreach ((string key, string label, string message, string color) in Samples)
        {
            if (await store.GetAsync(key, ct) is not null)
            {
                continue;
            }

            BadgeSvgRenderer.TryNormalizeColor(color, out string normalizedColor);
            BadgeMetrics metrics = BadgeSvgRenderer.Measure(label, message);
            await store.UpsertAsync(key, label, message, normalizedColor, metrics, providedAccessKey: null, ct);
        }
    }
}
