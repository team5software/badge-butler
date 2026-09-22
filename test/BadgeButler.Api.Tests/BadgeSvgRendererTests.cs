using AwesomeAssertions;

using T5S.BadgeButler.Api;

using Xunit;

namespace T5S.BadgeButler.Api.Tests;

public class BadgeSvgRendererTests
{
    [Fact]
    public void Render_IncludesLabelAndMessageText()
    {
        var svg = BadgeSvgRenderer.Render("coverage", "87%", "blue");

        svg.Should().Contain(">coverage<").And.Contain(">87%<");
    }

    [Fact]
    public void Render_IsValidSvgWithExpectedRootElement()
    {
        var svg = BadgeSvgRenderer.Render("build", "passing", "green");

        svg.Should().StartWith("<svg").And.EndWith("</svg>");
    }

    [Fact]
    public void Render_NormalizesBareHexColor_AddsLeadingHash()
    {
        var svg = BadgeSvgRenderer.Render("build", "passing", "4c1");

        svg.Should().Contain("fill=\"#4c1\"");
    }

    [Theory]
    [InlineData("#4c1")]
    [InlineData("green")]
    [InlineData("lightgrey")]
    public void Render_LeavesAlreadyPrefixedHexOrNamedColor_Unchanged(string color)
    {
        var svg = BadgeSvgRenderer.Render("build", "passing", color);

        svg.Should().Contain($"fill=\"{color}\"");
    }

    [Fact]
    public void Render_EscapesHtmlSpecialCharactersInLabelAndMessage()
    {
        var svg = BadgeSvgRenderer.Render("<script>", "a & b", "blue");

        svg.Should().NotContain("<script>")
            .And.Contain("&lt;script&gt;")
            .And.Contain("a &amp; b");
    }

    [Fact]
    public void Render_WidensSvgAsTextGetsLonger()
    {
        var shortSvg = BadgeSvgRenderer.Render("a", "b", "blue");
        var longSvg = BadgeSvgRenderer.Render("a much longer label", "b", "blue");

        var shortWidth = ExtractWidth(shortSvg);
        var longWidth = ExtractWidth(longSvg);

        longWidth.Should().BeGreaterThan(shortWidth);
    }

    private static int ExtractWidth(string svg)
    {
        var match = System.Text.RegularExpressions.Regex.Match(svg, @"<svg[^>]*\bwidth=""(\d+)""");
        return int.Parse(match.Groups[1].Value);
    }
}
