// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;

namespace T5S.BadgeButler.Api;

/// <summary>
/// A stored badge. Color is always a 6-character hex string (no leading '#') - callers provide
/// either a hex code or a CSS/SVG color name, normalized once at write time via
/// <see cref="BadgeSvgRenderer.TryNormalizeColor"/>. The five metric fields are computed once at
/// the same time (see <see cref="BadgeSvgRenderer.Measure"/>) so GET never needs to touch the
/// font at all.
/// </summary>
public sealed record Badge(
    string Key,
    string Label,
    string Message,
    string Color,
    string? AccessKey,
    float LabelWidth,
    float MessageWidth,
    float LabelBaseX,
    float MessageBaseX,
    float FontHeight,
    DateTimeOffset UpdatedAt)
{
    public BadgeMetrics Metrics => new(LabelWidth, MessageWidth, LabelBaseX, MessageBaseX, FontHeight);
}

/// <summary>
/// Upsert request body. No AccessKey field here on purpose - the badge's protection key is
/// carried by the X-Badge-Key header instead (see BadgeEndpointMapper), so a CI script can PUT
/// the same body/headers on every run without caring whether this is the first call or the
/// hundredth.
/// </summary>
public sealed record BadgeWrite(string Label, string Message, string? Color);
