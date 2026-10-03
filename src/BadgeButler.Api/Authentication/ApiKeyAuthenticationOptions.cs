// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using Microsoft.AspNetCore.Authentication;

namespace T5S.BadgeButler.Api.Authentication;

public class ApiKeyAuthenticationOptions:AuthenticationSchemeOptions
{
    public const string DefaultSchema = "ApiKey";
    public const string HeaderName = "X-API-Key";
}
