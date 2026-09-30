// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

using SixLabors.Fonts;

namespace T5S.BadgeButler.Api;

/// <summary>The five numbers <see cref="BadgeSvgRenderer.Measure"/> produces, stored on the badge row.</summary>
public sealed record BadgeMetrics(float LabelWidth, float MessageWidth, float LabelBaseX, float MessageBaseX, float FontHeight);

/// <summary>
/// Renders a flat-style status badge SVG (shields.io-ish), self-contained so the app has no
/// runtime dependency on shields.io or the OS's installed fonts. Measurement and rendering are
/// split deliberately: <see cref="Measure"/> runs once, at insert/update time, and its result is
/// stored; <see cref="Render"/> runs on every GET and never touches the font.
/// </summary>
public static class BadgeSvgRenderer
{
    private const int FontSize = 16;
    private const int HorizontalPadding = 10;
    private const int VerticalPadding = 2;
    private static readonly Regex BareHex = new("^[0-9a-fA-F]{3}$|^[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly Font BadgeFont = LoadFont();

    private static Font LoadFont()
    {
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "LiberationSans-Regular.ttf");
        FontFamily family = new FontCollection().Add(fontPath);
        return family.CreateFont(FontSize, FontStyle.Regular);
    }

    public static BadgeMetrics Measure(string label, string message)
    {
        FontMetrics fontMetrics = BadgeFont.FontMetrics;
        FontRectangle labelBounds = TextMeasurer.MeasureBounds(label, new TextOptions(BadgeFont));
        FontRectangle messageBounds = TextMeasurer.MeasureBounds(message, new TextOptions(BadgeFont));

        float fontHeight = FontSize + (FontSize * (Math.Abs(fontMetrics.HorizontalMetrics.Descender) / (float)fontMetrics.UnitsPerEm));

        return new BadgeMetrics(
            LabelWidth: labelBounds.Width,
            MessageWidth: messageBounds.Width,
            LabelBaseX: -labelBounds.X,
            MessageBaseX: -messageBounds.X,
            FontHeight: fontHeight);
    }

    public static string Render(string label, string message, string color, BadgeMetrics metrics)
    {
        float totalWidth = HorizontalPadding + metrics.LabelWidth + (HorizontalPadding * 2) + metrics.MessageWidth + HorizontalPadding;
        float totalHeight = VerticalPadding + metrics.FontHeight + VerticalPadding;

        Color labelBackground = Color.DimGray;
        Color labelForeground = labelBackground.GetBrightness() > 0.5 ? Color.White : Color.Black;
        string labelBackgroundHex = $"{labelBackground.R:X2}{labelBackground.G:X2}{labelBackground.B:X2}";
        string labelForegroundHex = $"{labelForeground.R:X2}{labelForeground.G:X2}{labelForeground.B:X2}";

        // color is always a validated 6-hex-digit string by the time it reaches here (normalized
        // at insert/update via TryNormalizeColor, enforced again by the database CHECK constraint).
        Color messageBackground = Color.FromArgb(
            int.Parse(color[..2], NumberStyles.HexNumber),
            int.Parse(color[2..4], NumberStyles.HexNumber),
            int.Parse(color[4..6], NumberStyles.HexNumber));
        Color messageForeground = messageBackground.GetBrightness() > 0.5 ? Color.White : Color.Black;
        string messageForegroundHex = $"{messageForeground.R:X2}{messageForeground.G:X2}{messageForeground.B:X2}";

        float labelBoxWidth = metrics.LabelWidth + (HorizontalPadding * 2);
        float messageBoxWidth = metrics.MessageWidth + (HorizontalPadding * 2);

        // FormattableString.Invariant: the numeric placeholders below (float widths/positions)
        // must render with a '.' decimal point regardless of the host's configured locale - SVG/
        // XML numeric attributes aren't locale-aware, and plain interpolation uses CurrentCulture
        // (confirmed this actually breaks: this machine's culture renders "124,828125", a comma,
        // which most SVG renderers reject outright).
        return FormattableString.Invariant($"""
                <svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="{totalHeight}" role="img" aria-label="{Escape(label)}: {Escape(message)}">
                  <clipPath id="badge-rect">
                    <rect width="{totalWidth}" height="{totalHeight}" rx="3" />
                  </clipPath>
                  <g clip-path="url(#badge-rect)">
                    <rect width="{labelBoxWidth}" height="{totalHeight}" fill="#{labelBackgroundHex}" />
                    <rect x="{labelBoxWidth}" width="{messageBoxWidth}" height="{totalHeight}" fill="#{color}" />
                  </g>
                  <g>
                    <text x="{metrics.LabelBaseX + HorizontalPadding}" y="{FontSize}" font-family="Arial, Liberation Sans, sans-serif" font-size="{FontSize}" fill="#{labelForegroundHex}">{Escape(label)}</text>
                    <text x="{metrics.MessageBaseX + labelBoxWidth + HorizontalPadding}" y="{FontSize}" font-family="Arial, Liberation Sans, sans-serif" font-size="{FontSize}" fill="#{messageForegroundHex}">{Escape(message)}</text>
                  </g>
                </svg>
                """);
    }

    /// <summary>
    /// Resolves a caller-supplied color (a CSS/SVG color name, or a hex code with or without a
    /// leading '#', 3- or 6-digit) to a canonical 6-digit uppercase hex string with no '#'. Named
    /// colors are matched via <see cref="KnownColor"/> - the "web" palette only, not Windows
    /// system colors (Desktop, ActiveBorder, ...), and both British and American "grey"/"gray"
    /// spellings are accepted even though the enum itself only defines the American one.
    /// </summary>
    public static bool TryNormalizeColor(string input, out string hex)
    {
        hex = "";
        string candidate = input.Trim();

        if (candidate.StartsWith('#'))
        {
            candidate = candidate[1..];
        }

        if (BareHex.IsMatch(candidate))
        {
            hex = ExpandHex(candidate);
            return true;
        }

        string nameCandidate = candidate.Replace("grey", "gray", StringComparison.OrdinalIgnoreCase);
        // Enum.TryParse accepts a purely-numeric string as the enum's raw underlying value even
        // when nothing defines it (confirmed: "12345" parses to KnownColor 12345, no such member) -
        // IsDefined is required, not optional, or any digit string would resolve to some color.
        if (Enum.TryParse<KnownColor>(nameCandidate, ignoreCase: true, out KnownColor known) && Enum.IsDefined(known))
        {
            Color color = Color.FromKnownColor(known);
            if (!color.IsSystemColor)
            {
                hex = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                return true;
            }
        }

        return false;
    }

    private static string ExpandHex(string hex) =>
        hex.Length == 6
            ? hex.ToUpperInvariant()
            : string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]).ToUpperInvariant();

    private static string Escape(string text) => WebUtility.HtmlEncode(text);
}
