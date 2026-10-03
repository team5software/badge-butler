// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

using AwesomeAssertions;

using Xunit;

namespace T5S.BadgeButler.Api.Tests;

public class BadgeSvgRendererTests
{
    [Fact]
    public void Render_IncludesLabelAndMessageText()
    {
        BadgeMetrics metrics = BadgeSvgRenderer.Measure("coverage", "87%");
        string svg = BadgeSvgRenderer.Render("coverage", "87%", "0000FF", metrics);

        svg.Should().Contain(">coverage<").And.Contain(">87%<");
    }

    [Fact]
    public void Render_IsValidSvgWithExpectedRootElement()
    {
        BadgeMetrics metrics = BadgeSvgRenderer.Measure("build", "passing");
        string svg = BadgeSvgRenderer.Render("build", "passing", "008000", metrics);

        svg.Should().StartWith("<svg").And.EndWith("</svg>");
    }

    [Fact]
    public void Render_UsesTheGivenColorAsMessageBackground()
    {
        BadgeMetrics metrics = BadgeSvgRenderer.Measure("build", "passing");
        string svg = BadgeSvgRenderer.Render("build", "passing", "4CC11C", metrics);

        svg.Should().Contain("fill=\"#4CC11C\"");
    }

    [Fact]
    public void Render_EscapesHtmlSpecialCharactersInLabelAndMessage()
    {
        BadgeMetrics metrics = BadgeSvgRenderer.Measure("<script>", "a & b");
        string svg = BadgeSvgRenderer.Render("<script>", "a & b", "0000FF", metrics);

        svg.Should().NotContain("<script>")
            .And.Contain("&lt;script&gt;")
            .And.Contain("a &amp; b");
    }

    [Fact]
    public void Render_WidensSvgAsTextGetsLonger()
    {
        BadgeMetrics shortMetrics = BadgeSvgRenderer.Measure("a", "b");
        BadgeMetrics longMetrics = BadgeSvgRenderer.Measure("a much longer label", "b");

        string shortSvg = BadgeSvgRenderer.Render("a", "b", "0000FF", shortMetrics);
        string longSvg = BadgeSvgRenderer.Render("a much longer label", "b", "0000FF", longMetrics);

        ExtractWidth(shortSvg).Should().BeLessThan(ExtractWidth(longSvg));
    }

    [Fact]
    public void Render_AlwaysUsesPeriodDecimalSeparator_RegardlessOfThreadCulture()
    {
        // Regression test: plain string interpolation uses CurrentCulture, which produced
        // width="124,828125" (a comma) under a comma-decimal culture - invalid SVG/XML, silently
        // broken for any host whose configured locale isn't period-decimal. Render must format
        // every number with CultureInfo.InvariantCulture regardless of what's running the app.
        CultureInfo original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            BadgeMetrics metrics = BadgeSvgRenderer.Measure("build", "passing");
            string svg = BadgeSvgRenderer.Render("build", "passing", "0000FF", metrics);

            // Not a blanket "no commas anywhere" check - font-family legitimately uses commas
            // as its font-stack separator. Only the numeric attributes matter here.
            svg.Should().MatchRegex("width=\"\\d+\\.\\d+\"");
            svg.Should().MatchRegex("height=\"\\d+\\.\\d+\"");
            svg.Should().NotMatchRegex(@"(?:width|height|x)=""\d+,\d+""");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("4c1", "44CC11")]
    [InlineData("4CC11C", "4CC11C")]
    [InlineData("#4c1", "44CC11")]
    [InlineData("#4CC11C", "4CC11C")]
    [InlineData("blue", "0000FF")]
    [InlineData("green", "008000")]
    [InlineData("lightgray", "D3D3D3")]
    [InlineData("lightgrey", "D3D3D3")]
    [InlineData("DimGrey", "696969")]
    public void TryNormalizeColor_ResolvesToExpectedHex(string input, string expectedHex)
    {
        bool resolved = BadgeSvgRenderer.TryNormalizeColor(input, out string hex);

        resolved.Should().BeTrue();
        hex.Should().Be(expectedHex);
    }

    [Theory]
    [InlineData("notacolor")]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("ActiveBorder")]
    [InlineData("Desktop")]
    public void TryNormalizeColor_RejectsInvalidOrSystemColors(string input)
    {
        bool resolved = BadgeSvgRenderer.TryNormalizeColor(input, out _);

        resolved.Should().BeFalse();
    }

    private static int ExtractWidth(string svg)
    {
        Match match = Regex.Match(svg, @"<svg[^>]*\bwidth=""([\d.]+)""");
        return (int)double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
