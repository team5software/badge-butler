// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

using T5S.BadgeButler.Api.Models;
using T5S.BadgeButler.Api.Persistence.Migration;
using T5S.BadgeButler.Api.Persistence.Migration.Postgres;
using T5S.BadgeButler.Api.RequestDtos;

namespace T5S.BadgeButler.Api.Persistence;

public sealed class PostgresStorage(string connectionString) : DatabaseStorage(connectionString)
{
    private const long InitializationLockKey = 0x4261646765427574;

    private static readonly Dictionary<int, IMigration> Migrations = new()
    {
        { 1, new InitialCreate() },
        { 2, new RemovePositionAndHeight_AddForegroundColor() }
    };

    public override async Task<InitializationResult> InitializeAsync(Func<BadgeWrite, BadgeAppearance> calculateAppearance, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand lockCommand = connection.CreateCommand())
        {
            lockCommand.CommandText = "SELECT pg_advisory_xact_lock($1)";
            lockCommand.Parameters.AddWithValue(InitializationLockKey);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        SchemaInfo? schemaInfo;
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT to_regclass('schema_info') IS NOT NULL";
            bool hasSchemaInfoTable;
            await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                hasSchemaInfoTable = reader.GetBoolean(0);
            }

            if (!hasSchemaInfoTable)
            {
                schemaInfo = null;
            }
            else
            {
                command.CommandText = "SELECT schema_version, constants_fingerprint FROM schema_info WHERE id = 1";
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                schemaInfo = await reader.ReadAsync(cancellationToken)
                    ? ReadSchemaInfoAsync(reader)
                    : null;
            }
        }

        bool migrationResult = schemaInfo is null
            ? await CreateDatabaseAsync(connection, cancellationToken)
            : await UpdateDatabaseAsync(connection, schemaInfo.SchemaVersion, cancellationToken);
        if (!migrationResult)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InitializationResult.Failed;
        }

        if (schemaInfo is not null && !schemaInfo.ConstantsFingerprint.Equals(BadgeSvgRenderer.MeasurementFingerprint))
        {
            await RecalculateAppearanceAsync(connection, calculateAppearance, cancellationToken);
        }

        await using (NpgsqlCommand schemaInfoUpdate = connection.CreateCommand())
        {
            schemaInfoUpdate.CommandText = """
                                           INSERT INTO schema_info (id, schema_version, constants_fingerprint) VALUES (1, $1, $2)
                                           ON CONFLICT (id) DO UPDATE SET schema_version = EXCLUDED.schema_version, constants_fingerprint = EXCLUDED.constants_fingerprint
                                           """;
            schemaInfoUpdate.Parameters.AddWithValue(CurrentSchemaVersion);
            schemaInfoUpdate.Parameters.AddWithValue(BadgeSvgRenderer.MeasurementFingerprint);
            await schemaInfoUpdate.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return InitializationResult.Success;
    }

    public override async Task<Badge?> GetAsync(string key, CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(ct);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
                              SELECT label, message, background_color, foreground_color, access_key, label_width, message_width, updated_at
                              FROM badges WHERE key = $1
                              """;
        command.Parameters.AddWithValue(key);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBadge(key, reader) : null;
    }

    public override async Task<UpsertResult> UpsertAsync(
        string key,
        string label,
        string message,
        BadgeAppearance appearance,
        string? providedAccessKey,
        CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(ct);

        (bool found, string? existingAccessKey) = await GetAccessKeyAsync(connection, key, ct);
        if (found && !AccessKeyCheck.IsAuthorized(existingAccessKey, providedAccessKey))
        {
            return UpsertResult.Unauthorized;
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
                              INSERT INTO badges (key, label, message, background_color, foreground_color, access_key, label_width, message_width, updated_at)
                              VALUES ($1, $2, $3, $4, $5, $6, $7, $8, now())
                              ON CONFLICT (key) DO UPDATE SET
                                  label = EXCLUDED.label,
                                  message = EXCLUDED.message,
                                  background_color = EXCLUDED.background_color,
                                  foreground_color = EXCLUDED.foreground_color,
                                  access_key = EXCLUDED.access_key,
                                  label_width = EXCLUDED.label_width,
                                  message_width = EXCLUDED.message_width,
                                  updated_at = now()
                              """;
        command.Parameters.AddWithValue(key);
        command.Parameters.AddWithValue(label);
        command.Parameters.AddWithValue(message);
        command.Parameters.AddWithValue(appearance.MessageBackgroundHex);
        command.Parameters.AddWithValue(appearance.MessageForegroundHex);
        command.Parameters.AddWithValue((object?)providedAccessKey ?? DBNull.Value);
        command.Parameters.AddWithValue(appearance.LabelWidth);
        command.Parameters.AddWithValue(appearance.MessageWidth);
        await command.ExecuteNonQueryAsync(ct);

        return found ? UpsertResult.Updated : UpsertResult.Created;
    }

    public override async Task<DeleteResult> DeleteAsync(string key, string? providedAccessKey, CancellationToken ct = default)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
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

    private static Task<bool> CreateDatabaseAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        IMigration migration = Migrations[CurrentSchemaVersion];
        return migration.Create(connection, cancellationToken);
    }

    private static async Task<bool> UpdateDatabaseAsync(NpgsqlConnection connection, int version, CancellationToken cancellationToken)
    {
        if (version > CurrentSchemaVersion) { return false; }

        for (int i = version + 1; i <= CurrentSchemaVersion; i++)
        {
            IMigration migration = Migrations[i];
            if (!await migration.Up(connection, cancellationToken)) { return false; }
        }

        return true;
    }

    private static async Task RecalculateAppearanceAsync(NpgsqlConnection connection, Func<BadgeWrite, BadgeAppearance> calculateAppearance, CancellationToken cancellationToken)
    {
        Dictionary<string, BadgeAppearance> appearances = new();
        await using (NpgsqlCommand selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT key, label, message, background_color FROM badges FOR UPDATE";
            await using NpgsqlDataReader reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                appearances.Add(
                    reader.GetString(0),
                    calculateAppearance(
                        new BadgeWrite(reader.GetString(1),
                            reader.GetString(2),
                            reader.GetString(3))));
            }
        }

        if (appearances.Count == 0) { return; }

        await using NpgsqlCommand updateCommand = connection.CreateCommand();
        updateCommand.CommandText = """
                                    UPDATE badges AS b SET
                                        foreground_color = u.foreground_color,
                                        label_width = u.label_width,
                                        message_width = u.message_width
                                    FROM unnest($1::text[], $2::text[], $3::real[], $4::real[])
                                        AS u(key, foreground_color, label_width, message_width)
                                    WHERE b.key = u.key
                                    """;
        updateCommand.Parameters.AddWithValue(appearances.Keys.ToArray());
        updateCommand.Parameters.AddWithValue(appearances.Select(item => item.Value.MessageForegroundHex).ToArray());
        updateCommand.Parameters.AddWithValue(appearances.Select(item => item.Value.LabelWidth).ToArray());
        updateCommand.Parameters.AddWithValue(appearances.Select(item => item.Value.MessageWidth).ToArray());
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(bool Found, string? AccessKey)> GetAccessKeyAsync(NpgsqlConnection connection, string key, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT access_key FROM badges WHERE key = $1";
        command.Parameters.AddWithValue(key);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (false, null);
        }

        return (true, await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetString(0));
    }

    private static SchemaInfo ReadSchemaInfoAsync(NpgsqlDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1));

    private static Badge ReadBadge(string key, NpgsqlDataReader reader) => new(
        key,
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetFloat(5),
        reader.GetFloat(6),
        reader.GetFieldValue<DateTimeOffset>(7));
}
