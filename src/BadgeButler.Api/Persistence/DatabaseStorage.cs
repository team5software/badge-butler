// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Threading;
using System.Threading.Tasks;

using T5S.BadgeButler.Api.Models;
using T5S.BadgeButler.Api.RequestDtos;

namespace T5S.BadgeButler.Api.Persistence;

public abstract class DatabaseStorage(string connectionString) : IApplicationStorage
{
    protected const int CurrentSchemaVersion = 3;
    protected readonly string ConnectionString = connectionString;

    public abstract Task<InitializationResult> InitializeAsync(AppearanceCalculator appearanceCalculator, CancellationToken cancellationToken = default);
    public abstract Task<Badge?> GetAsync(string key, CancellationToken ct = default);
    public abstract Task<UpsertResult> UpsertAsync(string key, string label, string message, BadgeAppearance appearance, string? providedAccessKey, CancellationToken ct = default);
    public abstract Task<DeleteResult> DeleteAsync(string key, string? providedAccessKey, CancellationToken ct = default);
}
