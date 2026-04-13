using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;

namespace PoshMcp.Server.Authentication;

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-API-Key";
    public Dictionary<string, ApiKeyDefinition> Keys { get; set; } = new();
}
