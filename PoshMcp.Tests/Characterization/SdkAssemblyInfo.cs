using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace PoshMcp.Tests.Characterization;

/// <summary>
/// Runtime-detected provenance for the ModelContextProtocol SDK assembly that the
/// measured server process actually loads. Replaces the previously hardcoded
/// <c>"ModelContextProtocol 1.4.1"</c> string so a Phase 0 (v1) artifact is proven to
/// carry a v1 binary and a Phase 4 (current) artifact a v2 binary — the SHA-256 and
/// major version make swapped or same-version pairings machine-detectable.
/// </summary>
internal sealed class SdkAssemblyDescriptor
{
    /// <summary>Simple assembly name, e.g. <c>ModelContextProtocol</c>.</summary>
    [JsonPropertyName("assemblyName")]
    public string AssemblyName { get; set; } = "";

    /// <summary>
    /// <see cref="AssemblyInformationalVersionAttribute"/> value (package/product version,
    /// e.g. <c>1.4.1</c> or <c>2.0.0+abcdef</c>). Preferred source of the semantic version.
    /// </summary>
    [JsonPropertyName("informationalVersion")]
    public string InformationalVersion { get; set; } = "";

    /// <summary>Win32 file version, e.g. <c>2.0.0.0</c>. Secondary evidence.</summary>
    [JsonPropertyName("fileVersion")]
    public string FileVersion { get; set; } = "";

    /// <summary>Parsed leading major version (1 for 1.4.1, 2 for 2.0.0). Zero if undetectable.</summary>
    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }

    /// <summary>Absolute path of the resolved SDK DLL on disk.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>Lower-case hex SHA-256 of the SDK DLL bytes.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    /// <summary>Human-readable display, e.g. <c>ModelContextProtocol 2.0.0</c>.</summary>
    [JsonPropertyName("packageDisplay")]
    public string PackageDisplay { get; set; } = "";
}

/// <summary>
/// Detects the ModelContextProtocol SDK assembly that sits next to the server binary the
/// characterization/Phase 4 tests actually launch (see
/// <see cref="CharacterizationHttpServer.ResolveServerDllPath"/>). Detection reads the
/// assembly's informational/file version and a SHA-256 of the DLL so provenance is derived
/// from the real artifact rather than a hardcoded label.
/// </summary>
internal static class SdkAssemblyInfo
{
    internal const string SdkAssemblyFileName = "ModelContextProtocol.dll";

    /// <summary>
    /// Detects the SDK descriptor for the ModelContextProtocol DLL located in the same
    /// output directory as the measured server binary. Honors <c>POSHMCP_SERVER_DLL</c>
    /// (set for the Phase 0 same-job step) exactly as the server launcher does.
    /// </summary>
    internal static SdkAssemblyDescriptor DetectFromMeasuredServer()
    {
        var serverDll = CharacterizationHttpServer.ResolveServerDllPath();
        var serverDir = System.IO.Path.GetDirectoryName(serverDll)
            ?? throw new InvalidOperationException(
                $"Could not determine directory of server DLL '{serverDll}' for SDK detection.");
        var sdkPath = System.IO.Path.Combine(serverDir, SdkAssemblyFileName);
        return DetectFromFile(sdkPath);
    }

    /// <summary>
    /// Detects the SDK descriptor for a specific ModelContextProtocol DLL path.
    /// Throws <see cref="FileNotFoundException"/> when the DLL is absent so a missing
    /// binary can never masquerade as a valid provenance record.
    /// </summary>
    internal static SdkAssemblyDescriptor DetectFromFile(string sdkPath)
    {
        if (string.IsNullOrEmpty(sdkPath))
            throw new ArgumentException("SDK DLL path is null or empty.", nameof(sdkPath));

        if (!File.Exists(sdkPath))
            throw new FileNotFoundException(
                $"ModelContextProtocol SDK assembly not found at '{sdkPath}'. " +
                "SDK provenance requires the real DLL next to the measured server binary; " +
                "a hardcoded version string is not accepted.",
                sdkPath);

        var assemblyName = AssemblyName.GetAssemblyName(sdkPath);
        var fileVersionInfo = FileVersionInfo.GetVersionInfo(sdkPath);

        // Prefer AssemblyInformationalVersion (the NuGet package version) via metadata load,
        // falling back to the Win32 product/file version when unavailable.
        var informational = ReadInformationalVersion(sdkPath)
            ?? fileVersionInfo.ProductVersion
            ?? assemblyName.Version?.ToString()
            ?? "";

        var fileVersion = fileVersionInfo.FileVersion
            ?? assemblyName.Version?.ToString()
            ?? "";

        var major = ParseMajor(informational);
        if (major == 0) major = ParseMajor(fileVersion);
        if (major == 0 && assemblyName.Version is not null) major = assemblyName.Version.Major;

        var sha256 = ComputeSha256(sdkPath);
        var cleanVersion = CleanVersion(informational, fileVersion, assemblyName.Version);

        return new SdkAssemblyDescriptor
        {
            AssemblyName = assemblyName.Name ?? "ModelContextProtocol",
            InformationalVersion = informational,
            FileVersion = fileVersion,
            MajorVersion = major,
            Path = System.IO.Path.GetFullPath(sdkPath),
            Sha256 = sha256,
            PackageDisplay = $"{assemblyName.Name ?? "ModelContextProtocol"} {cleanVersion}",
        };
    }

    private static string? ReadInformationalVersion(string sdkPath)
    {
        try
        {
            // Metadata-only load so we can read the attribute without executing the assembly
            // and without pinning it into the test's load context.
            var asm = Assembly.LoadFile(System.IO.Path.GetFullPath(sdkPath));
            var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attr?.InformationalVersion;
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException)
        {
            // Fall back to FileVersionInfo — do not fail detection on load quirks.
            return null;
        }
    }

    private static int ParseMajor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 0;

        // Strip a leading 'v' and any build metadata / pre-release suffix.
        var s = version.Trim().TrimStart('v', 'V');
        var dot = s.IndexOf('.');
        var token = dot >= 0 ? s[..dot] : s;

        // Guard against pre-release/build suffixes directly on the major (e.g. "2-preview").
        var cut = token.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) token = token[..cut];

        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) && major > 0
            ? major
            : 0;
    }

    private static string CleanVersion(string informational, string fileVersion, Version? asmVersion)
    {
        var source = !string.IsNullOrWhiteSpace(informational) ? informational
            : !string.IsNullOrWhiteSpace(fileVersion) ? fileVersion
            : asmVersion?.ToString() ?? "unknown";

        // Drop build metadata after '+' (e.g. "2.0.0+sha" -> "2.0.0").
        var plus = source.IndexOf('+');
        return plus >= 0 ? source[..plus] : source;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
