// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Threading;
using System.Threading.Tasks;

namespace T5S.BadgeButler.Api.Persistence;

public enum UpsertResult { Created, Updated, Unauthorized }

public enum DeleteResult { Success, NotFound, Unauthorized }

/// <summary>
/// Storage abstraction for badges, so a fast in-memory implementation can stand in for the real
/// Postgres-backed one in tests. Enforces the per-badge access key (Badge.AccessKey) itself,
/// rather than leaving that check to callers - the same rule (badge has no key -> anyone may
/// write; badge has a key -> the provided key must match, via fixed-time comparison) needs to
/// hold for every implementation, so it lives here once.
/// </summary>
public interface IBadgeStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<Badge?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Insert-or-replace. providedAccessKey is checked against the badge's current key if one is
    /// set (Unauthorized if it doesn't match), then unconditionally becomes the badge's new
    /// stored key (including null, clearing protection) - so a caller can set, change, or remove
    /// a badge's key just by what it sends next time, same as any other field on a full-replace
    /// write.
    /// </summary>
    Task<UpsertResult> UpsertAsync(
        string key,
        string label,
        string message,
        string color,
        BadgeMetrics metrics,
        string? providedAccessKey,
        CancellationToken ct = default);

    Task<DeleteResult> DeleteAsync(string key, string? providedAccessKey, CancellationToken ct = default);
}
