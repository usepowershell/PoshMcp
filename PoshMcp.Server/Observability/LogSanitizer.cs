using System.Text.RegularExpressions;

namespace PoshMcp.Server.Observability;

/// <summary>
/// Helpers for scrubbing user-controlled values before they are written to log
/// statements, mitigating CWE-117 (log forging / log injection).
/// </summary>
/// <remarks>
/// <para>
/// Untrusted input that contains CR (<c>\r</c>) or LF (<c>\n</c>) can be used to
/// inject forged log entries when written verbatim to a line-oriented log sink.
/// Other ASCII control characters can corrupt log viewers or be used to hide
/// content. <see cref="Scrub(string?)"/> replaces every CR, LF, TAB, and other
/// ASCII control character with a visible escape sequence
/// (<c>\\r</c>, <c>\\n</c>, <c>\\t</c>, or <c>\\xNN</c>) so the original
/// information is preserved without being interpreted as a line break.
/// </para>
/// <para>
/// Inputs longer than <see cref="MaxLength"/> are truncated and suffixed with
/// <c>"…(truncated)"</c> to bound log volume. Null inputs return the literal
/// string <c>"&lt;null&gt;"</c>.
/// </para>
/// <para>
/// The implementation uses <see cref="Regex.Replace(string,MatchEvaluator)"/>,
/// which CodeQL's <c>cs/log-forging</c> <c>StringReplaceSanitizer</c> recognises
/// as a taint barrier, so every call site that uses the return value is
/// automatically clean in the CodeQL taint graph.  When no control characters
/// are present the .NET runtime returns the original string reference
/// (zero-allocation fast path).
/// </para>
/// </remarks>
public static class LogSanitizer
{
    /// <summary>
    /// Maximum length of a sanitized log value before truncation.
    /// </summary>
    public const int MaxLength = 2048;

    private const string NullPlaceholder = "<null>";
    private const string TruncationSuffix = "…(truncated)";

    // CR+LF pair must appear before standalone CR/LF so the pair maps to \r\n,
    // not \r followed by \n. TAB (0x09), other C0 controls, and DEL (0x7F).
    private static readonly Regex _unsafeChars = new(
        "\r\n|\r|\n|\t|[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns a log-safe copy of <paramref name="value"/>. CR/LF, TAB, and
    /// other ASCII control characters are replaced with visible escape
    /// sequences, and the result is truncated to <see cref="MaxLength"/>
    /// characters.
    /// </summary>
    /// <param name="value">User-controlled value to sanitize. May be null.</param>
    /// <returns>A sanitized, log-safe string. Never returns null.</returns>
    public static string Scrub(string? value)
    {
        if (value is null) return NullPlaceholder;
        if (value.Length == 0) return string.Empty;
        if (value.Length > MaxLength)
            value = value.Substring(0, MaxLength) + TruncationSuffix;

        // Regex.Replace is recognised by CodeQL cs/log-forging as a
        // StringReplaceSanitizer, breaking the taint chain from user input.
        // When no control characters are present the runtime returns the
        // original string reference — preserving zero-allocation fast-path
        // semantics for clean tool names and GUIDs.
        return _unsafeChars.Replace(value, static m => m.Value switch
        {
            "\r\n" => "\\r\\n",
            "\r" => "\\r",
            "\n" => "\\n",
            "\t" => "\\t",
            _ => $"\\x{(int)m.Value[0]:X2}"
        });
    }
}
