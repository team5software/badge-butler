// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using T5S.BadgeButler.Api.Models;
using T5S.BadgeButler.Api.RequestDtos;

namespace T5S.BadgeButler.Api.Persistence;

public sealed class InMemoryStorage : IApplicationStorage
{
    private readonly ConcurrentDictionary<string, Badge> _badges = new();

    public Task<InitializationResult> InitializeAsync(AppearanceCalculator appearanceCalculator, CancellationToken cancellationToken = default) => Task.FromResult(InitializationResult.Success);

    public Task<Badge?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_badges.GetValueOrDefault(key));

    public Task<UpsertResult> UpsertAsync(
        string key,
        string label,
        string message,
        BadgeAppearance appearance,
        string? providedAccessKey,
        CancellationToken ct = default)
    {
        Badge? existing = _badges.GetValueOrDefault(key);
        if (existing is not null && !AccessKeyCheck.IsAuthorized(existing.AccessKey, providedAccessKey))
        {
            return Task.FromResult(UpsertResult.Unauthorized);
        }

        _badges[key] = new Badge(
            key,
            label,
            message,
            appearance.MessageBackgroundHex,
            appearance.MessageForegroundHex,
            providedAccessKey,
            appearance.LabelWidth,
            appearance.MessageWidth,
            DateTimeOffset.UtcNow);

        return Task.FromResult(existing is null ? UpsertResult.Created : UpsertResult.Updated);
    }

    public Task<DeleteResult> DeleteAsync(string key, string? providedAccessKey, CancellationToken ct = default)
    {
        if (!_badges.TryGetValue(key, out Badge? existing))
        {
            return Task.FromResult(DeleteResult.NotFound);
        }

        if (!AccessKeyCheck.IsAuthorized(existing.AccessKey, providedAccessKey))
        {
            return Task.FromResult(DeleteResult.Unauthorized);
        }

        _badges.TryRemove(key, out _);
        return Task.FromResult(DeleteResult.Success);
    }
}
