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

/// <summary>
/// How stored badge appearance is computed, passed in so storage never depends on the renderer.
/// Fingerprint changes whenever Calculate would produce different values for the same input
/// (see BadgeSvgRenderer.AppearanceFingerprint); persistent storage recalculates every badge
/// with Calculate when the fingerprint it last stored differs.
/// </summary>
public sealed record AppearanceCalculator(string Fingerprint, Func<BadgeWrite, BadgeAppearance> Calculate);

public interface IApplicationStorage
{
    Task<InitializationResult> InitializeAsync(AppearanceCalculator appearanceCalculator, CancellationToken cancellationToken = default);

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
