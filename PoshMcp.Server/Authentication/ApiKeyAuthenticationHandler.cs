using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoshMcp.Server.Metrics;

namespace PoshMcp.Server.Authentication;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    McpMetrics metrics)
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
            Logger.LogWarning("Invalid API key presented from {RemoteIp}", Context.Connection.RemoteIpAddress);
            metrics.AuthAttempts.Add(1,
                new KeyValuePair<string, object?>("scheme", "ApiKey"),
                new KeyValuePair<string, object?>("result", "failure"));
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var keyName = Options.Keys
            .FirstOrDefault(k => k.Key == apiKey).Key ?? "unknown";
        var maskedKeyName = keyName.Length > 4
            ? keyName[..4] + "****"
            : "****";

        Logger.LogDebug("API key authentication succeeded for {KeyName}", maskedKeyName);
        metrics.AuthAttempts.Add(1,
            new KeyValuePair<string, object?>("scheme", "ApiKey"),
            new KeyValuePair<string, object?>("result", "success"));

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

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;

        // Add WWW-Authenticate header pointing to resource metadata if configured
        var authConfig = Context.RequestServices
            .GetRequiredService<IOptions<AuthenticationConfiguration>>();

        if (authConfig.Value.ProtectedResource?.Resource is not null)
        {
            var metadataUrl = $"{authConfig.Value.ProtectedResource.Resource}/.well-known/oauth-protected-resource";
            Response.Headers["WWW-Authenticate"] =
                $"Bearer resource_metadata=\"{metadataUrl}\"";
        }
        else
        {
            Response.Headers["WWW-Authenticate"] = "ApiKey";
        }

        await base.HandleChallengeAsync(properties);
    }
}

