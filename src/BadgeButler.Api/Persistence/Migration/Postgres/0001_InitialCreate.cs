// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading;
using System.Threading.Tasks;

using Npgsql;

namespace T5S.BadgeButler.Api.Persistence.Migration.Postgres;

public class InitialCreate : IMigration
{
    public async Task<bool> Create(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        const string commandText = """
                                   DROP TABLE IF EXISTS schema_info, badges;
                                   CREATE TABLE schema_info (
                                       id INT PRIMARY KEY CHECK (id = 1),
                                       schema_version INT NOT NULL,
                                       constants_fingerprint TEXT NOT NULL
                                   );
                                   CREATE TABLE badges (
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
                                   );
                                   """;

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);

        return true;
    }

    public Task<bool> Up(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        return Create(connection, cancellationToken);
    }
}
