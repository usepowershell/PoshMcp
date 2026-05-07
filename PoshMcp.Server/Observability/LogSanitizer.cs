using System;

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
/// content. <see cref="Scrub(string?)"/> replaces every CR, LF, and other ASCII
/// control character (excluding TAB) with a visible escape sequence
/// (<c>\\r</c>, <c>\\n</c>, <c>\\t</c>, or <c>\\xNN</c>) so the original
/// information is preserved without being interpreted as a line break.
/// </para>
/// <para>
/// Inputs longer than <see cref="MaxLength"/> are truncated and suffixed with
/// <c>"…(truncated)"</c> to bound log volume. Null inputs return the literal
/// string <c>"&lt;null&gt;"</c>.
/// </para>
/// <para>
/// Apply this helper at the call site for every untrusted value flowing into a
/// log statement. CodeQL's <c>cs/log-forging</c> taint analysis tracks call-site
/// sinks, so call-site scrubbing is what closes the alerts.
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

    /// <summary>
    /// Returns a copy of <paramref name="value"/> safe to embed in a log
    /// statement. CR/LF and other ASCII control characters are replaced with
    /// visible escape sequences, and the result is truncated to
    /// <see cref="MaxLength"/> characters.
    /// </summary>
    /// <param name="value">User-controlled value to sanitize. May be null.</param>
    /// <returns>A sanitized, log-safe string. Never returns null.</returns>
    public static string Scrub(string? value)
    {
        if (value is null)
        {
            return NullPlaceholder;
        }

        if (value.Length == 0)
        {
            return value;
        }

        // Fast path: if the string contains no characters that need scrubbing
        // and is within the length limit, return it unchanged.
        var needsScrub = false;
        for (var i = 0; i < value.Length; i++)
        {
            if (RequiresEscape(value[i]))
            {
                needsScrub = true;
                break;
            }
        }

        if (!needsScrub && value.Length <= MaxLength)
        {
            return value;
        }

        var capacity = Math.Min(value.Length, MaxLength) + TruncationSuffix.Length;
        var builder = new System.Text.StringBuilder(capacity);

        var limit = Math.Min(value.Length, MaxLength);
        for (var i = 0; i < limit; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20 || c == 0x7F)
                    {
                        builder.Append("\\x");
                        builder.Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }

        if (value.Length > MaxLength)
        {
            builder.Append(TruncationSuffix);
        }

        return builder.ToString();
    }

    private static bool RequiresEscape(char c)
    {
        // Treat all C0 controls (0x00-0x1F) and DEL (0x7F) as requiring escape.
        // TAB (0x09) is escaped to "\t" because mixed tabs in single-line logs
        // can still confuse parsers; CR/LF are the primary forging vectors.
        return c < 0x20 || c == 0x7F;
    }
}
