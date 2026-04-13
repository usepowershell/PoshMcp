using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoshMcp.Server.Authentication;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var keyValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var apiKey = keyValues.FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!Options.Keys.TryGetValue(apiKey, out var keyDef))
        {
            Logger.LogWarning("Invalid API key presented");
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "api-key-user"),
            new(ClaimTypes.AuthenticationMethod, "ApiKey")
        };

        // Add scope claims
        foreach (var scope in keyDef.Scopes)
            claims.Add(new Claim("scp", scope));

        // Add role claims
        foreach (var role in keyDef.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
