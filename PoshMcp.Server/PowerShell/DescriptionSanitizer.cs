using System;
using System.Globalization;
using System.Text;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Spec 010 FR-540 description-text normalizer. Pure helper; no I/O, no allocations beyond
/// the returned string.
/// </summary>
/// <remarks>
/// <para>
/// Applied to description text from any source (Get-Help synopsis, Get-Help description body,
/// Get-Help parameter description, etc.) before that text is surfaced to MCP clients.
/// Sanitization is what makes the path-parity guarantee in FR-520 deliverable across the
/// in-process console host and the OOP subprocess host with redirected I/O — different
/// hosts wrap and pad text differently, and this normalizer absorbs those differences.
/// </para>
/// <para>
/// The sequence applied (per FR-540) is:
/// </para>
/// <list type="number">
///   <item><description>Trim leading and trailing whitespace from the overall string.</description></item>
///   <item><description>Strip non-printable control characters (Unicode category <c>Cc</c>) other
///   than the paragraph separator <c>\n\n</c> produced by FR-500 step 2.</description></item>
///   <item><description>Within each paragraph (text between <c>\n\n</c> separators), collapse all
///   runs of whitespace — spaces, tabs, single <c>\n</c>, <c>\r</c>, and <c>\r\n</c> — to a
///   single space. Preserve the <c>\n\n</c> separators between paragraphs.</description></item>
///   <item><description>Re-trim each paragraph after collapse.</description></item>
/// </list>
/// </remarks>
public static class DescriptionSanitizer
{
    /// <summary>
    /// The paragraph separator preserved by FR-540: two consecutive line-feeds. Producers
    /// (Get-Help <c>.Description</c> body join, Get-Help parameter <c>description</c> join)
    /// emit this exact sequence between paragraphs.
    /// </summary>
    public const string ParagraphSeparator = "\n\n";

    /// <summary>
    /// Normalizes <paramref name="value"/> per spec 010 FR-540. Returns <see cref="string.Empty"/>
    /// when <paramref name="value"/> is <c>null</c>, empty, or contains only whitespace and
    /// strippable control characters. Never throws.
    /// </summary>
    /// <param name="value">Raw description text from any precedence source.</param>
    /// <returns>Normalized text safe for MCP serialization and byte-comparable across paths.</returns>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // FR-540 step 1: outer trim.
        var trimmed = value!.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // Split on the paragraph separator first so step 3 can run per-paragraph and
        // preserve "\n\n" between them. Empty paragraphs (created by 3+ newlines or edge
        // cases) collapse out of the result.
        var paragraphs = trimmed.Split(new[] { ParagraphSeparator }, StringSplitOptions.None);
        var normalizedParagraphs = new System.Collections.Generic.List<string>(paragraphs.Length);

        foreach (var paragraph in paragraphs)
        {
            var normalized = NormalizeParagraph(paragraph);
            if (normalized.Length > 0)
            {
                normalizedParagraphs.Add(normalized);
            }
        }

        return string.Join(ParagraphSeparator, normalizedParagraphs);
    }

    /// <summary>
    /// Truncates <paramref name="value"/> to at most <paramref name="maxLength"/> characters,
    /// preferring a word boundary, and appends an ellipsis (<c>…</c>, U+2026) when truncation
    /// occurred. Implements FR-541 / FR-542. The ellipsis counts toward the cap.
    /// </summary>
    /// <param name="value">Already-sanitized description text.</param>
    /// <param name="maxLength">Maximum allowed length including the ellipsis. Must be at
    /// least 1.</param>
    /// <returns><paramref name="value"/> unchanged if it already fits; otherwise truncated
    /// at the last whitespace at or before <c>maxLength - 1</c>, with <c>…</c> appended.</returns>
    public static string TruncateAtWordBoundary(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (maxLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Must be at least 1.");
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        // Reserve one character for the ellipsis.
        var cap = maxLength - 1;
        if (cap <= 0)
        {
            return "\u2026";
        }

        // Walk back from cap to the last whitespace; if none, hard-cut at cap.
        var cut = cap;
        while (cut > 0 && !char.IsWhiteSpace(value[cut]))
        {
            cut--;
        }

        if (cut == 0)
        {
            cut = cap; // No whitespace found; fall back to a hard cut.
        }

        return value.Substring(0, cut).TrimEnd() + "\u2026";
    }

    private static string NormalizeParagraph(string paragraph)
    {
        if (string.IsNullOrEmpty(paragraph))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(paragraph.Length);
        var inWhitespaceRun = false;

        foreach (var ch in paragraph)
        {
            // FR-540 step 2: strip Unicode category Cc (control characters). Whitespace
            // characters that are also category Cc (\t, \n, \r, etc.) are converted into
            // the single-space whitespace run handled below.
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Control)
            {
                if (IsWhitespaceControl(ch))
                {
                    if (!inWhitespaceRun && builder.Length > 0)
                    {
                        builder.Append(' ');
                    }
                    inWhitespaceRun = true;
                }
                // Non-whitespace control character: drop entirely.
                continue;
            }

            // FR-540 step 3: collapse non-control whitespace (spaces, NBSP-equivalents
            // PowerShell hosts sometimes inject) into a single ASCII space.
            if (char.IsWhiteSpace(ch))
            {
                if (!inWhitespaceRun && builder.Length > 0)
                {
                    builder.Append(' ');
                }
                inWhitespaceRun = true;
                continue;
            }

            builder.Append(ch);
            inWhitespaceRun = false;
        }

        // FR-540 step 4: trim trailing whitespace introduced by the collapse pass.
        while (builder.Length > 0 && builder[builder.Length - 1] == ' ')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    private static bool IsWhitespaceControl(char ch)
    {
        // ASCII whitespace control characters that should collapse into a space rather
        // than be stripped: HT, LF, VT, FF, CR.
        return ch == '\t' || ch == '\n' || ch == '\v' || ch == '\f' || ch == '\r';
    }
}
