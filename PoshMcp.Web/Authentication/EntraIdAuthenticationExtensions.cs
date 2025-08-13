using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Threading.Tasks;

namespace PoshMcp.Web.Authentication;

/// <summary>
/// Extension methods for configuring Entra ID authentication
/// </summary>
public static class EntraIdAuthenticationExtensions
{
    /// <summary>
    /// Adds Entra ID authentication services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddEntraIdAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind configuration
        var entraIdConfig = new EntraIdConfiguration();
        configuration.GetSection("EntraId").Bind(entraIdConfig);
        services.Configure<EntraIdConfiguration>(configuration.GetSection("EntraId"));

        // Only configure authentication if enabled
        if (!entraIdConfig.Enabled)
        {
            return services;
        }

        // Validate required configuration
        if (string.IsNullOrEmpty(entraIdConfig.TenantId) && string.IsNullOrEmpty(entraIdConfig.Authority))
        {
            throw new InvalidOperationException("Either TenantId or Authority must be specified when Entra ID authentication is enabled");
        }

        if (string.IsNullOrEmpty(entraIdConfig.ClientId) && string.IsNullOrEmpty(entraIdConfig.Audience))
        {
            throw new InvalidOperationException("Either ClientId or Audience must be specified when Entra ID authentication is enabled");
        }

        // Add authentication services
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = entraIdConfig.GetAuthority();
                options.Audience = entraIdConfig.GetAudience();
                options.RequireHttpsMetadata = entraIdConfig.RequireHttpsMetadata;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = entraIdConfig.ValidateIssuer,
                    ValidateAudience = entraIdConfig.ValidateAudience,
                    ValidateLifetime = entraIdConfig.ValidateLifetime,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(5) // Allow for clock skew
                };

                // Add logging for authentication events
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogWarning("Authentication failed: {Error}", context.Exception?.Message);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogDebug("Token validated for user: {User}", context.Principal?.Identity?.Name ?? "Unknown");
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogDebug("Authentication challenge issued: {Error}", context.Error ?? "No error");
                        return Task.CompletedTask;
                    }
                };
            });

        // Add authorization services
        services.AddAuthorization(options =>
        {
            // Default policy requires authentication when Entra ID is enabled
            options.DefaultPolicy = options.GetPolicy("EntraIdRequired") ?? 
                new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

            // Policy for MCP endpoints
            options.AddPolicy("McpEndpointAccess", policy =>
            {
                policy.RequireAuthenticatedUser();
                
                // Add scope requirements if specified
                if (entraIdConfig.RequiredScopes.Count > 0)
                {
                    foreach (var scope in entraIdConfig.RequiredScopes)
                    {
                        policy.RequireClaim("scp", scope);
                    }
                }
            });
        });

        return services;
    }
}