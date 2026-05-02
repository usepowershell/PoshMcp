using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PoshMcp.Server.Authentication;

public static class AuthenticationServiceExtensions
{
    public static IServiceCollection AddPoshMcpAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authConfig = configuration
            .GetSection("Authentication")
            .Get<AuthenticationConfiguration>();

        if (authConfig is null || !authConfig.Enabled)
            return services;

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
                        options.TokenValidationParameters.ValidateAudience = !string.IsNullOrEmpty(scheme.Audience);

                        // RFC 9728: inject resource_metadata into WWW-Authenticate so
                        // clients (e.g. VS Code) can discover the PRM and find the real
                        // authorization server instead of falling back to treating this
                        // server as the AS.
                        options.Events = new JwtBearerEvents
                        {
                            OnChallenge = context =>
                            {
                                var cfg = context.HttpContext.RequestServices
                                    .GetRequiredService<IOptions<AuthenticationConfiguration>>();

                                if (cfg.Value.ProtectedResource?.Resource is not null)
                                {
                                    var req = context.HttpContext.Request;
                                    var metadataUrl = $"{req.Scheme}://{req.Host}/.well-known/oauth-protected-resource";

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
