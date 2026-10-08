// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading;
using System.Threading.Tasks;

using Npgsql;

namespace T5S.BadgeButler.Api.Persistence.Migration;

public interface IMigration
{
    Task<bool> Create(NpgsqlConnection connection, CancellationToken cancellationToken = default);
    Task<bool> Up(NpgsqlConnection connection, CancellationToken cancellationToken = default);
}
