// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;

namespace T5S.BadgeButler.Api.Models;

public sealed record Badge(
    string Key,
    string Label,
    string Message,
    string MessageColorBackground,
    string MessageColorForeground,
    string? AccessKey,
    float LabelWidth,
    float MessageWidth,
    DateTimeOffset UpdatedAt)
{
    public BadgeAppearance Appearance => new(LabelWidth, MessageWidth, MessageColorBackground, MessageColorForeground);
}
