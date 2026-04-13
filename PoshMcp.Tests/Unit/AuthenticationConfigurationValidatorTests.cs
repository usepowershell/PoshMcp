using System.Collections.Generic;
using Microsoft.Extensions.Options;
using PoshMcp.Server.Authentication;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class AuthenticationConfigurationValidatorTests
{
    private readonly AuthenticationConfigurationValidator _validator = new();

    [Fact]
    public void Validate_WhenDisabled_ReturnsSuccess()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = false,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>()
        };

        var result = _validator.Validate(null, config);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_WhenEnabled_NoSchemes_ReturnsFail()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>()
        };

        var result = _validator.Validate(null, config);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("no schemes"));
    }

    [Fact]
    public void Validate_JwtBearer_MissingAuthority_ReturnsFail()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>
            {
                ["Bearer"] = new AuthSchemeConfiguration { Type = "JwtBearer", Authority = null }
            }
        };

        var result = _validator.Validate(null, config);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Authority") && f.Contains("JwtBearer"));
    }

    [Fact]
    public void Validate_JwtBearer_WithAuthority_ReturnsSuccess()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>
            {
                ["Bearer"] = new AuthSchemeConfiguration
                {
                    Type = "JwtBearer",
                    Authority = "https://login.microsoftonline.com/tenant",
                    Audience = "api://my-app"
                }
            }
        };

        var result = _validator.Validate(null, config);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_ApiKey_NoKeys_ReturnsFail()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>
            {
                ["ApiKey"] = new AuthSchemeConfiguration
                {
                    Type = "ApiKey",
                    Keys = new Dictionary<string, ApiKeyDefinition>()
                }
            }
        };

        var result = _validator.Validate(null, config);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("Keys") && f.Contains("at least one key"));
    }

    [Fact]
    public void Validate_ApiKey_WithKeys_ReturnsSuccess()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>
            {
                ["ApiKey"] = new AuthSchemeConfiguration
                {
                    Type = "ApiKey",
                    Keys = new Dictionary<string, ApiKeyDefinition>
                    {
                        ["my-key"] = new ApiKeyDefinition { Scopes = new List<string> { "tools:read" } }
                    }
                }
            }
        };

        var result = _validator.Validate(null, config);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_ProtectedResource_InvalidUri_ReturnsFail()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>
            {
                ["Bearer"] = new AuthSchemeConfiguration
                {
                    Type = "JwtBearer",
                    Authority = "https://login.example.com/tenant"
                }
            },
            ProtectedResource = new ProtectedResourceConfiguration { Resource = "not-a-uri" }
        };

        var result = _validator.Validate(null, config);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("ProtectedResource.Resource") && f.Contains("valid absolute URI"));
    }

    [Fact]
    public void Validate_ProtectedResource_ValidUri_ReturnsSuccess()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>
            {
                ["Bearer"] = new AuthSchemeConfiguration
                {
                    Type = "JwtBearer",
                    Authority = "https://login.example.com/tenant"
                }
            },
            ProtectedResource = new ProtectedResourceConfiguration { Resource = "https://api.example.com/mcp" }
        };

        var result = _validator.Validate(null, config);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_MultipleErrors_ReturnsAllErrors()
    {
        var config = new AuthenticationConfiguration
        {
            Enabled = true,
            Schemes = new Dictionary<string, AuthSchemeConfiguration>
            {
                ["Bearer"] = new AuthSchemeConfiguration { Type = "JwtBearer", Authority = null },
                ["ApiKey"] = new AuthSchemeConfiguration
                {
                    Type = "ApiKey",
                    Keys = new Dictionary<string, ApiKeyDefinition>()
                }
            }
        };

        var result = _validator.Validate(null, config);

        Assert.True(result.Failed);
        var failures = new List<string>(result.Failures);
        Assert.True(failures.Count >= 2, $"Expected at least 2 errors, got {failures.Count}: {string.Join("; ", failures)}");
        Assert.Contains(failures, f => f.Contains("Authority") && f.Contains("JwtBearer"));
        Assert.Contains(failures, f => f.Contains("Keys") && f.Contains("at least one key"));
    }
}
