using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoshMcp.Server.Authentication;

public static class AuthenticationServiceExtensions
{
    public static IServiceCollection AddPoshMcpAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AuthenticationConfiguration>()
            .Configure(opts => configuration.GetSection("Authentication").Bind(opts))
            .ValidateOnStart();

        var authConfig = configuration
            .GetSection("Authentication")
            .Get<AuthenticationConfiguration>();

        if (authConfig is null || !authConfig.Enabled)
            return services;

        // Required by the /token proxy endpoint for forwarding token exchanges to Entra.
        if (authConfig.OAuthProxy is { Enabled: true })
            services.AddHttpClient();

        var authBuilder = services.AddAuthentication(authConfig.DefaultScheme);

        foreach (var (name, scheme) in authConfig.Schemes)
        {
            switch (scheme.Type)
            {
                case "JwtBearer":
                    authBuilder.AddJwtBearer(name, options =>
                    {
                        options.Authority = scheme.Authority;
                        options.Audience = scheme.Audience;
                        options.RequireHttpsMetadata = scheme.RequireHttpsMetadata;
                        if (scheme.ValidIssuers.Count > 0)
                        {
                            options.TokenValidationParameters.ValidIssuers = scheme.ValidIssuers;
                        }
                        options.TokenValidationParameters.ValidateIssuer = scheme.ValidIssuers.Count > 0;

                        // Warn when Authority is Entra v1 but ValidIssuers references v2
                        if (!string.IsNullOrEmpty(options.Authority) &&
                            options.Authority.Contains("login.microsoftonline.com") &&
                            !options.Authority.TrimEnd('/').EndsWith("/v2.0") &&
                            options.TokenValidationParameters.ValidIssuers?.Any(i => i.Contains("/v2.0")) == true)
                        {
                            Console.Error.WriteLine(
                                $"[PoshMcp WARNING] JwtBearer scheme '{name}': Authority '{options.Authority}' uses the " +
                                $"Entra v1.0 OIDC endpoint but ValidIssuers contains a v2.0 issuer. " +
                                $"Access tokens obtained via the v2.0 endpoint will fail signature validation. " +
                                $"Consider setting Authority to '{options.Authority.TrimEnd('/')}/v2.0'.");
                        }
                        options.TokenValidationParameters.ValidateAudience = !string.IsNullOrEmpty(scheme.Audience);

                        // One-time startup diagnostic: log Authority and ValidAudiences so we can
                        // confirm the JWT config matches what the token issuer will produce.
                        Console.Error.WriteLine(
                            $"[PoshMcp JWT] Scheme '{name}': Authority='{options.Authority}', " +
                            $"ValidAudiences='{string.Join(",", options.TokenValidationParameters.ValidAudiences ?? [])}'");

                        // RFC 9728: inject resource_metadata into WWW-Authenticate so
                        // clients (e.g. VS Code) can discover the PRM and find the real
                        // authorization server instead of falling back to treating this
                        // server as the AS.
                        options.Events = new JwtBearerEvents
                        {
                            OnMessageReceived = context =>
                            {
                                var hasToken = !string.IsNullOrEmpty(
                                    context.Request.Headers.Authorization.ToString());
                                context.HttpContext.RequestServices
                                    .GetRequiredService<ILogger<JwtBearerHandler>>()
                                    .LogInformation(
                                        "JWT OnMessageReceived: HasBearerToken={HasToken}, Path={Path}",
                                        hasToken, context.Request.Path);
                                return Task.CompletedTask;
                            },

                            OnTokenValidated = context =>
                            {
                                var claims = context.Principal?.Claims
                                    .Where(c => c.Type is "aud" or "scp" or "iss" or "sub" or "appid")
                                    .Select(c => $"{c.Type}={c.Value}");
                                context.HttpContext.RequestServices
                                    .GetRequiredService<ILogger<JwtBearerHandler>>()
                                    .LogInformation(
                                        "JWT OnTokenValidated: Claims={Claims}",
                                        string.Join(", ", claims ?? []));
                                return Task.CompletedTask;
                            },

                            OnAuthenticationFailed = context =>
                            {
                                context.HttpContext.RequestServices
                                    .GetRequiredService<ILogger<JwtBearerHandler>>()
                                    .LogWarning(
                                        "JWT OnAuthenticationFailed: {ExceptionType}: {Message}",
                                        context.Exception.GetType().Name,
                                        context.Exception.Message);
                                return Task.CompletedTask;
                            },

                            OnChallenge = context =>
                            {
                                var logger = context.HttpContext.RequestServices
                                    .GetRequiredService<ILogger<JwtBearerHandler>>();
                                logger.LogWarning(
                                    "JWT OnChallenge: Error={Error}, ErrorDescription={ErrorDescription}",
                                    context.Error ?? "(none)", context.ErrorDescription ?? "(none)");

                                var cfg = context.HttpContext.RequestServices
                                    .GetRequiredService<IOptions<AuthenticationConfiguration>>();

                                // Fire for any configured ProtectedResource (not just when Resource is
                                // non-null) so that result=none (no token at all) also triggers the
                                // RFC 9728 discovery chain and VS Code opens the browser for sign-in.
                                if (cfg.Value.ProtectedResource is not null)
                                {
                                    var req = context.HttpContext.Request;
                                    // Honour X-Forwarded-Proto/Host headers set by reverse proxies
                                    // (e.g. Azure Container Apps) so the URL uses https://, not http://.
                                    var scheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? req.Scheme;
                                    var host = req.Headers["X-Forwarded-Host"].FirstOrDefault()
                                               ?? req.Host.ToUriComponent();
                                    var metadataUrl = $"{scheme}://{host}/.well-known/oauth-protected-resource";

                                    // Suppress the default challenge so we control the header.
                                    context.HandleResponse();
                                    context.Response.StatusCode = 401;
                                    context.Response.Headers.WWWAuthenticate =
                                        $"Bearer resource_metadata=\"{metadataUrl}\"";
                                }

                                return Task.CompletedTask;
                            }
                        };
                    });
                    break;

                case "ApiKey":
                    authBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                        name, options =>
                        {
                            options.HeaderName = scheme.HeaderName;
                            options.Keys = scheme.Keys;
                        });
                    break;

                default:
                    // Unknown scheme type — skip; validator should have caught this
                    break;
            }
        }

        services.AddAuthorization(options =>
        {
            var scopeClaim = authConfig.Schemes.TryGetValue(authConfig.DefaultScheme, out var defaultScheme)
                ? defaultScheme.ClaimsMapping?.ScopeClaim ?? "scp"
                : "scp";

            options.AddPolicy("McpAccess", policy =>
            {
                if (authConfig.DefaultPolicy.RequireAuthentication)
                    policy.RequireAuthenticatedUser();

                if (authConfig.DefaultPolicy.RequiredScopes.Count > 0)
                    policy.RequireClaim(scopeClaim, authConfig.DefaultPolicy.RequiredScopes.ToArray());

                if (authConfig.DefaultPolicy.RequiredRoles.Count > 0)
                    policy.RequireRole(authConfig.DefaultPolicy.RequiredRoles.ToArray());
            });
        });

        return services;
    }
}
