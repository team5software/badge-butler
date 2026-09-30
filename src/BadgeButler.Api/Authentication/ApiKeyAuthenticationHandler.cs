// Copyright (c) Daniel Baufeld.
// Licensed under the MIT License.
// See LICENSE file in the project root for full license terms.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace T5S.BadgeButler.Api.Authentication;

public class ApiKeyAuthenticationHandler(IOptionsMonitor<ApiKeyAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? expectedApiKey = configuration["Auth:ApiKey"];
        expectedApiKey = string.IsNullOrWhiteSpace(expectedApiKey) ? null : expectedApiKey;

        if (expectedApiKey is null)
        {
            return Task.FromResult(AuthenticateResult.Success(GetStaticTicket()));
        }

        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out StringValues providedApiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        byte[] providedBytes=Encoding.UTF8.GetBytes(providedApiKey.ToString());
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
        }

        return Task.FromResult(AuthenticateResult.Success(GetStaticTicket()));
    }

    private AuthenticationTicket GetStaticTicket()
    {
        Claim[] claims = new[] { new Claim(ClaimTypes.Name, "static-client"), new Claim("client_id", "static-client"), };
        ClaimsIdentity identity = new ClaimsIdentity(claims, Scheme.Name);
        ClaimsPrincipal principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, Scheme.Name);
    }
}
