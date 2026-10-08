// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Threading;
using System.Threading.Tasks;

using T5S.BadgeButler.Api.Models;
using T5S.BadgeButler.Api.RequestDtos;

namespace T5S.BadgeButler.Api.Persistence;

public enum InitializationResult { Success, Failed }

public enum UpsertResult { Created, Updated, Unauthorized }

public enum DeleteResult { Success, NotFound, Unauthorized }

public interface IApplicationStorage
{
    Task<InitializationResult> InitializeAsync(Func<BadgeWrite, BadgeAppearance> calculateAppearance, CancellationToken cancellationToken = default);

    Task<Badge?> GetAsync(string key, CancellationToken ct = default);

    Task<UpsertResult> UpsertAsync(
        string key,
        string label,
        string message,
        BadgeAppearance appearance,
        string? providedAccessKey,
        CancellationToken ct = default);

    Task<DeleteResult> DeleteAsync(string key, string? providedAccessKey, CancellationToken ct = default);
}
