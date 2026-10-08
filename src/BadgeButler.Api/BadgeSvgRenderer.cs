// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using SixLabors.Fonts;

using T5S.BadgeButler.Api.RequestDtos;

namespace T5S.BadgeButler.Api;

public sealed record BadgeAppearance(float LabelWidth, float MessageWidth, string MessageBackgroundHex, string MessageForegroundHex);

public static class BadgeSvgRenderer
{
    private const int BadgeHeight = 20;
    private const int BadgeCornerRadius = 3;
    private const int HorizontalPadding = 10;
    private const int FontSize = 13;
    private const int FontPositionY = 15;

    private static readonly Regex BareHex = new("^[0-9a-fA-F]{3}$|^[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly string FontPath = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
    private static readonly Font BadgeFont = LoadFont();

    public static string MeasurementFingerprint { get; } = ComputeMeasurementFingerprint();

    private static Font LoadFont()
    {
        FontFamily family = new FontCollection().Add(FontPath);
        return family.CreateFont(FontSize, FontStyle.Regular);
    }

    private static string ComputeMeasurementFingerprint()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(File.ReadAllBytes(FontPath));
        hash.AppendData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"|size={FontSize}")));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static BadgeAppearance CalculateAppearance(BadgeWrite writeRequest)
    {
        FontRectangle labelBounds = TextMeasurer.MeasureBounds(writeRequest.Label, new TextOptions(BadgeFont));
        FontRectangle messageBounds = TextMeasurer.MeasureBounds(writeRequest.Message, new TextOptions(BadgeFont));
        string foregroundHex = ContrastingForeground(writeRequest.Color);

        return new BadgeAppearance(
            labelBounds.Width,
            messageBounds.Width,
            writeRequest.Color,
            foregroundHex);
    }

    public static string Render(string label, string message, BadgeAppearance appearance)
    {
        float totalWidth = HorizontalPadding + appearance.LabelWidth + (HorizontalPadding * 2) + appearance.MessageWidth + HorizontalPadding;

        string labelBackgroundHex = ToHex(Color.DimGray);
        string labelForegroundHex = ToHex(Color.White);

        float labelBoxWidth = appearance.LabelWidth + (HorizontalPadding * 2);
        float messageBoxWidth = appearance.MessageWidth + (HorizontalPadding * 2);

        return FormattableString.Invariant($"""
                                            <svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="{BadgeHeight}" role="img" aria-label="{Escape(label)}: {Escape(message)}">
                                              <clipPath id="badge-rect">
                                                <rect width="{totalWidth}" height="{BadgeHeight}" rx="{BadgeCornerRadius}" />
                                              </clipPath>
                                              <g clip-path="url(#badge-rect)">
                                                <rect width="{labelBoxWidth}" height="{BadgeHeight}" fill="#{labelBackgroundHex}" />
                                                <rect x="{labelBoxWidth}" width="{messageBoxWidth}" height="{BadgeHeight}" fill="#{appearance.MessageBackgroundHex}" />
                                              </g>
                                              <g font-size="{FontSize}" font-family="Verdana,DejaVu Sans,sans-serif">
                                                <text x="{HorizontalPadding}" y="{FontPositionY}" fill="#{labelForegroundHex}">{Escape(label)}</text>
                                                <text x="{labelBoxWidth + HorizontalPadding}" y="{FontPositionY}" fill="#{appearance.MessageForegroundHex}">{Escape(message)}</text>
                                              </g>
                                            </svg>
                                            """);
    }

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
        if (Enum.TryParse(nameCandidate, ignoreCase: true, out KnownColor known) && Enum.IsDefined(known))
        {
            Color color = Color.FromKnownColor(known);
            if (!color.IsSystemColor)
            {
                hex = ToHex(color);
                return true;
            }
        }

        return false;
    }

    public static string ContrastingForeground(string backgroundHex)
    {
        double luminance = RelativeLuminance(backgroundHex);
        double contrastWithBlack = (luminance + 0.05) / 0.05;
        double contrastWithWhite = 1.05 / (luminance + 0.05);
        return contrastWithBlack >= contrastWithWhite ? "000000" : "FFFFFF";
    }

    private static double RelativeLuminance(string hex)
    {
        // https://en.wikipedia.org/wiki/Relative_luminance#Relative_luminance_and_%22gamma_encoded%22_colorspaces
        return (0.2126 * Linearize(hex[..2])) + (0.7152 * Linearize(hex[2..4])) + (0.0722 * Linearize(hex[4..6]));

        // https://en.wikipedia.org/wiki/SRGB#Transfer_function_(%22gamma%22)
        static double Linearize(string component)
        {
            double channel = int.Parse(component, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
    }

    private static string ToHex(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ExpandHex(string hex) =>
        hex.Length == 6
            ? hex.ToUpperInvariant()
            : string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]).ToUpperInvariant();

    private static string Escape(string text) => WebUtility.HtmlEncode(text);
}
