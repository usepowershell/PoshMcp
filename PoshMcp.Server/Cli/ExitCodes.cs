namespace PoshMcp;

/// <summary>
/// Shared process exit codes used across the server and subprocess helpers.
/// </summary>
internal static class ExitCodes
{
    internal const int Success = 0;
    internal const int ConfigError = 2;
    internal const int StartupError = 3;
    internal const int RuntimeError = 4;
}
