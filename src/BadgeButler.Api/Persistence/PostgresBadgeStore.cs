// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

namespace T5S.BadgeButler.Api.Persistence;

/// <summary>
/// Postgres-backed storage for badge state, one row per badge key. Prerelease (v0.x): schema
/// changes are made directly rather than via migration scripts - CREATE TABLE IF NOT EXISTS does
/// not retroactively alter an existing table, so upgrading a live instance across a schema change
/// currently means dropping and letting it recreate (acceptable for now; revisit once there are
/// real consumers who'd lose data over it).
/// </summary>
public sealed class PostgresBadgeStore(string connectionString) : IBadgeStore
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
                               CREATE TABLE IF NOT EXISTS badges (
                                   key TEXT PRIMARY KEY,
                                   label VARCHAR(255) NOT NULL,
                                   message VARCHAR(255) NOT NULL,
                                   color CHAR(6) NOT NULL CHECK (color ~ '^[0-9a-fA-F]{6}$'),
                                   access_key TEXT NULL,
                                   label_width REAL NOT NULL,
                                   message_width REAL NOT NULL,
                                   label_base_x REAL NOT NULL,
                                   message_base_x REAL NOT NULL,
                                   font_height REAL NOT NULL,
                                   updated_at TIMESTAMPTZ NOT NULL
                               )
                               """;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Badge?> GetAsync(string key, CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
                               SELECT label, message, color, access_key, label_width, message_width, label_base_x, message_base_x, font_height, updated_at
                               FROM badges WHERE key = $1
                               """;
        command.Parameters.AddWithValue(key);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBadge(key, reader) : null;
    }

    public async Task<UpsertResult> UpsertAsync(
        string key,
        string label,
        string message,
        string color,
        BadgeMetrics metrics,
        string? providedAccessKey,
        CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        (bool found, string? existingAccessKey) = await GetAccessKeyAsync(connection, key, ct);
        if (found && !AccessKeyCheck.IsAuthorized(existingAccessKey, providedAccessKey))
        {
            return UpsertResult.Unauthorized;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
                               INSERT INTO badges (key, label, message, color, access_key, label_width, message_width, label_base_x, message_base_x, font_height, updated_at)
                               VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, now())
                               ON CONFLICT (key) DO UPDATE SET
                                   label = EXCLUDED.label,
                                   message = EXCLUDED.message,
                                   color = EXCLUDED.color,
                                   access_key = EXCLUDED.access_key,
                                   label_width = EXCLUDED.label_width,
                                   message_width = EXCLUDED.message_width,
                                   label_base_x = EXCLUDED.label_base_x,
                                   message_base_x = EXCLUDED.message_base_x,
                                   font_height = EXCLUDED.font_height,
                                   updated_at = now()
                               """;
        command.Parameters.AddWithValue(key);
        command.Parameters.AddWithValue(label);
        command.Parameters.AddWithValue(message);
        command.Parameters.AddWithValue(color);
        command.Parameters.AddWithValue((object?)providedAccessKey ?? DBNull.Value);
        command.Parameters.AddWithValue(metrics.LabelWidth);
        command.Parameters.AddWithValue(metrics.MessageWidth);
        command.Parameters.AddWithValue(metrics.LabelBaseX);
        command.Parameters.AddWithValue(metrics.MessageBaseX);
        command.Parameters.AddWithValue(metrics.FontHeight);
        await command.ExecuteNonQueryAsync(ct);

        return found ? UpsertResult.Updated : UpsertResult.Created;
    }

    public async Task<DeleteResult> DeleteAsync(string key, string? providedAccessKey, CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        (bool found, string? existingAccessKey) = await GetAccessKeyAsync(connection, key, ct);
        if (!found)
        {
            return DeleteResult.NotFound;
        }

        if (!AccessKeyCheck.IsAuthorized(existingAccessKey, providedAccessKey))
        {
            return DeleteResult.Unauthorized;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM badges WHERE key = $1";
        command.Parameters.AddWithValue(key);
        await command.ExecuteNonQueryAsync(ct);

        return DeleteResult.Success;
    }

    private static async Task<(bool Found, string? AccessKey)> GetAccessKeyAsync(NpgsqlConnection connection, string key, CancellationToken ct)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT access_key FROM badges WHERE key = $1";
        command.Parameters.AddWithValue(key);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return (false, null);
        }

        return (true, await reader.IsDBNullAsync(0, ct) ? null : reader.GetString(0));
    }

    private static Badge ReadBadge(string key, NpgsqlDataReader reader) =>
        new(
            key,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetFloat(4),
            reader.GetFloat(5),
            reader.GetFloat(6),
            reader.GetFloat(7),
            reader.GetFloat(8),
            reader.GetFieldValue<DateTimeOffset>(9));
}
