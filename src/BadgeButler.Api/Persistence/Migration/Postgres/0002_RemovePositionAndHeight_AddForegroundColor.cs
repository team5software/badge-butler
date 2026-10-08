// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading;
using System.Threading.Tasks;

using Npgsql;

namespace T5S.BadgeButler.Api.Persistence.Migration.Postgres;

public class RemovePositionAndHeight_AddForegroundColor : IMigration
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
                                       background_color CHAR(6) NOT NULL CHECK (background_color ~ '^[0-9a-fA-F]{6}$'),
                                       foreground_color CHAR(6) NOT NULL CHECK (foreground_color ~ '^[0-9a-fA-F]{6}$'),
                                       access_key TEXT NULL,
                                       label_width REAL NOT NULL,
                                       message_width REAL NOT NULL,
                                       updated_at TIMESTAMPTZ NOT NULL
                                   );
                                   """;

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);

        return true;
    }

    public async Task<bool> Up(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        const string commandText = """
                                   ALTER TABLE badges RENAME COLUMN color TO background_color;
                                   ALTER TABLE badges RENAME CONSTRAINT badges_color_check TO badges_background_color_check;
                                   ALTER TABLE badges RENAME CONSTRAINT badges_color_not_null TO badges_background_color_not_null;
                                   ALTER TABLE badges
                                       DROP COLUMN label_base_x,
                                       DROP COLUMN message_base_x,
                                       DROP COLUMN font_height,
                                       ADD COLUMN foreground_color CHAR(6) NOT NULL DEFAULT '000000' CHECK (foreground_color ~ '^[0-9a-fA-F]{6}$');
                                   ALTER TABLE badges ALTER COLUMN foreground_color DROP DEFAULT;
                                   """;

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);

        return true;
    }
}
