// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Security.Cryptography;
using System.Text;

namespace T5S.BadgeButler.Api.Persistence;

internal static class AccessKeyCheck
{
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
