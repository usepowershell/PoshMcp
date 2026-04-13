using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace PoshMcp.Server.Authentication;

public class AuthenticationConfigurationValidator : IValidateOptions<AuthenticationConfiguration>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationConfiguration options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (options.Schemes.Count == 0)
            errors.Add("Authentication.Enabled is true but no schemes are configured.");

        foreach (var (schemeName, scheme) in options.Schemes)
        {
            if (scheme.Type == "JwtBearer" && string.IsNullOrEmpty(scheme.Authority))
                errors.Add($"Authentication.Schemes[{schemeName}].Authority is required for JwtBearer.");

            if (scheme.Type == "ApiKey" && scheme.Keys.Count == 0)
                errors.Add($"Authentication.Schemes[{schemeName}].Keys must have at least one key.");
        }

        if (options.ProtectedResource?.Resource is not null)
        {
            if (!Uri.TryCreate(options.ProtectedResource.Resource, UriKind.Absolute, out _))
                errors.Add("Authentication.ProtectedResource.Resource must be a valid absolute URI.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
