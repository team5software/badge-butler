using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

namespace T5S.BadgeButler.Api;

public sealed record Badge(string Key, string Label, string Message, string Color, DateTimeOffset UpdatedAt);

public sealed record BadgeUpdate(string Label, string Message, string? Color);

/// <summary>Postgres-backed storage for badge state. One row per badge key.</summary>
public sealed class BadgeStore(string connectionString)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS badges (
                key TEXT PRIMARY KEY,
                label TEXT NOT NULL,
                message TEXT NOT NULL,
                color TEXT NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL
            )
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Badge?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT label, message, color, updated_at FROM badges WHERE key = $1";
        command.Parameters.AddWithValue(key);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new Badge(
            key,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task UpsertAsync(string key, string label, string message, string color, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO badges (key, label, message, color, updated_at)
            VALUES ($1, $2, $3, $4, now())
            ON CONFLICT (key) DO UPDATE SET
                label = EXCLUDED.label,
                message = EXCLUDED.message,
                color = EXCLUDED.color,
                updated_at = now()
            """;
        command.Parameters.AddWithValue(key);
        command.Parameters.AddWithValue(label);
        command.Parameters.AddWithValue(message);
        command.Parameters.AddWithValue(color);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM badges WHERE key = $1";
        command.Parameters.AddWithValue(key);

        var rows = await command.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }
}

/// <summary>
/// Renders a flat-style status badge SVG (shields.io-ish), self-contained so the app has no
/// runtime dependency on shields.io. Text width is a fixed-average-per-character approximation
/// rather than a real font metrics table, so long/wide characters can look slightly off-center -
/// acceptable for label/value text, not pursued further for simplicity.
/// Only real CSS/SVG color keywords and hex codes are supported - not shields.io's own named
/// palette (e.g. "brightgreen"), since that isn't a standard and would need its own lookup table.
/// </summary>
public static class BadgeSvgRenderer
{
    private const int Height = 20;
    private const int HorizontalPadding = 10;
    private const double AverageCharWidth = 6.5;
    private static readonly Regex BareHex = new("^[0-9a-fA-F]{3}$|^[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static string Render(string label, string message, string color)
    {
        var labelWidth = MeasureText(label) + (HorizontalPadding * 2);
        var messageWidth = MeasureText(message) + (HorizontalPadding * 2);
        var totalWidth = labelWidth + messageWidth;
        var fill = NormalizeColor(color);

        var labelX = labelWidth / 2.0;
        var messageX = labelWidth + (messageWidth / 2.0);
        var textY = Height - 6;

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="{Height}" role="img" aria-label="{Escape(label)}: {Escape(message)}">
              <linearGradient id="s" x2="0" y2="100%">
                <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
                <stop offset="1" stop-opacity=".1"/>
              </linearGradient>
              <clipPath id="r">
                <rect width="{totalWidth}" height="{Height}" rx="3" fill="#fff"/>
              </clipPath>
              <g clip-path="url(#r)">
                <rect width="{labelWidth}" height="{Height}" fill="#555"/>
                <rect x="{labelWidth}" width="{messageWidth}" height="{Height}" fill="{fill}"/>
                <rect width="{totalWidth}" height="{Height}" fill="url(#s)"/>
              </g>
              <g fill="#fff" text-anchor="middle" font-family="Verdana,Geneva,sans-serif" font-size="11">
                <text x="{labelX}" y="{textY}">{Escape(label)}</text>
                <text x="{messageX}" y="{textY}">{Escape(message)}</text>
              </g>
            </svg>
            """;
    }

    private static int MeasureText(string text) => (int)Math.Ceiling(text.Length * AverageCharWidth);

    private static string Escape(string text) => WebUtility.HtmlEncode(text);

    private static string NormalizeColor(string color) =>
        BareHex.IsMatch(color) ? $"#{color}" : color;
}
