// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Threading;
using System.Threading.Tasks;

using Npgsql;

using Xunit;

namespace T5S.BadgeButler.Api.Tests;

/// <summary>
/// A throwaway database on the Postgres server named by BADGEBUTLER_TEST_POSTGRES (a connection
/// string whose user may CREATE DATABASE), one per test so tests can't see each other's tables.
/// Skips the calling test when the variable isn't set - except in CI (CI=true), where it fails
/// instead, so a missing Postgres service can't let the pipeline go green with storage untested.
/// </summary>
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    public const string ServerVariable = "BADGEBUTLER_TEST_POSTGRES";

    private readonly string serverConnectionString;
    private readonly string name;

    private PostgresTestDatabase(string serverConnectionString, string name)
    {
        this.serverConnectionString = serverConnectionString;
        this.name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
    }

    public string ConnectionString { get; }

    public static async Task<PostgresTestDatabase> CreateAsync(CancellationToken ct)
    {
        string? server = Environment.GetEnvironmentVariable(ServerVariable);
        if (string.IsNullOrEmpty(server) && Environment.GetEnvironmentVariable("CI") == "true")
        {
            throw new InvalidOperationException($"{ServerVariable} must be set in CI - is the postgres service missing from the workflow?");
        }

        Assert.SkipWhen(string.IsNullOrEmpty(server), $"Set {ServerVariable} to a Postgres connection string to run the Postgres store tests.");

        PostgresTestDatabase database = new(server!, $"badgebutler_test_{Guid.NewGuid():N}");
        await ExecuteOnAsync(server!, $"CREATE DATABASE {database.name}", ct);
        return database;
    }

    public Task ExecuteAsync(string sql, CancellationToken ct, params object[] parameters) =>
        ExecuteOnAsync(ConnectionString, sql, ct, parameters);

    public async Task<T> ScalarAsync<T>(string sql, CancellationToken ct)
    {
        await using NpgsqlConnection connection = new(ConnectionString);
        await connection.OpenAsync(ct);
        await using NpgsqlCommand command = new(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnAsync(serverConnectionString, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)", CancellationToken.None);
    }

    private static async Task ExecuteOnAsync(string connectionString, string sql, CancellationToken ct, params object[] parameters)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(ct);
        await using NpgsqlCommand command = new(sql, connection);
        foreach (object parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter);
        }

        await command.ExecuteNonQueryAsync(ct);
    }
}
