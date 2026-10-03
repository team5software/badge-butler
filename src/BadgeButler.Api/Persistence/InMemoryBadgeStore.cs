// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace T5S.BadgeButler.Api.Persistence;

/// <summary>Fast, non-persistent IBadgeStore for tests - no database, no I/O.</summary>
public sealed class InMemoryBadgeStore : IBadgeStore
{
    private readonly ConcurrentDictionary<string, Badge> badges = new();

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<Badge?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(badges.GetValueOrDefault(key));

    public Task<UpsertResult> UpsertAsync(
        string key,
        string label,
        string message,
        string color,
        BadgeMetrics metrics,
        string? providedAccessKey,
        CancellationToken ct = default)
    {
        Badge? existing = badges.GetValueOrDefault(key);
        if (existing is not null && !AccessKeyCheck.IsAuthorized(existing.AccessKey, providedAccessKey))
        {
            return Task.FromResult(UpsertResult.Unauthorized);
        }

        badges[key] = new Badge(
            key, label, message, color, providedAccessKey,
            metrics.LabelWidth, metrics.MessageWidth, metrics.LabelBaseX, metrics.MessageBaseX, metrics.FontHeight,
            DateTimeOffset.UtcNow);

        return Task.FromResult(existing is null ? UpsertResult.Created : UpsertResult.Updated);
    }

    public Task<DeleteResult> DeleteAsync(string key, string? providedAccessKey, CancellationToken ct = default)
    {
        if (!badges.TryGetValue(key, out Badge? existing))
        {
            return Task.FromResult(DeleteResult.NotFound);
        }

        if (!AccessKeyCheck.IsAuthorized(existing.AccessKey, providedAccessKey))
        {
            return Task.FromResult(DeleteResult.Unauthorized);
        }

        badges.TryRemove(key, out _);
        return Task.FromResult(DeleteResult.Success);
    }
}
