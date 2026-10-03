// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Security.Cryptography;
using System.Text;

namespace T5S.BadgeButler.Api.Persistence;

/// <summary>Shared by every IBadgeStore implementation so the rule is identical everywhere.</summary>
internal static class AccessKeyCheck
{
    /// <summary>No key set on the badge -> anyone may write. Key set -> must match, fixed-time.</summary>
    public static bool IsAuthorized(string? storedAccessKey, string? providedAccessKey)
    {
        if (storedAccessKey is null)
        {
            return true;
        }

        if (providedAccessKey is null)
        {
            return false;
        }

        byte[] storedBytes = Encoding.UTF8.GetBytes(storedAccessKey);
        byte[] providedBytes = Encoding.UTF8.GetBytes(providedAccessKey);
        return CryptographicOperations.FixedTimeEquals(storedBytes, providedBytes);
    }
}
