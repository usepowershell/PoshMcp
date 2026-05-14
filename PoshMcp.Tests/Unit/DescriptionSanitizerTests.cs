using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="DescriptionSanitizer"/>, the FR-540 normalizer used by the
/// in-process and out-of-process metadata pipelines. These tests pin behavior that the
/// FR-520 byte-equivalence guarantee depends on: any change here must hold across both
/// execution paths.
/// </summary>
[Trait("Category", "Unit")]
public class DescriptionSanitizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t\t")]
    [InlineData("\r\n\r\n")]
    public void Normalize_NullOrWhitespaceInput_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, DescriptionSanitizer.Normalize(input));
    }

    [Fact]
    public void Normalize_OuterWhitespace_IsTrimmed()
    {
        Assert.Equal("hello", DescriptionSanitizer.Normalize("   hello   "));
    }

    [Fact]
    public void Normalize_TabsBetweenWords_CollapseToSingleSpace()
    {
        Assert.Equal("hello world", DescriptionSanitizer.Normalize("hello\t\tworld"));
    }

    [Fact]
    public void Normalize_MultipleSpacesBetweenWords_CollapseToSingleSpace()
    {
        Assert.Equal("hello world", DescriptionSanitizer.Normalize("hello     world"));
    }

    [Fact]
    public void Normalize_MixedTabsAndSpaces_CollapseToSingleSpace()
    {
        Assert.Equal("a b c", DescriptionSanitizer.Normalize("a \t  b\t \t c"));
    }

    [Fact]
    public void Normalize_ParagraphBreak_IsPreserved()
    {
        var input = "First paragraph.\n\nSecond paragraph.";
        Assert.Equal("First paragraph.\n\nSecond paragraph.", DescriptionSanitizer.Normalize(input));
    }

    [Fact]
    public void Normalize_MultipleBlankLines_StillSeparateParagraphsByDoubleNewline()
    {
        // Three or more newlines still represent a paragraph boundary; the normalizer
        // must collapse them to exactly one blank line so parity with the OOP host holds.
        var input = "First.\n\n\n\nSecond.";
        Assert.Equal("First.\n\nSecond.", DescriptionSanitizer.Normalize(input));
    }

    [Fact]
    public void Normalize_WhitespaceWithinParagraph_IsCollapsed_WithoutDestroyingParagraphBreak()
    {
        var input = "First   \tparagraph.\n\nSecond\tparagraph.";
        Assert.Equal("First paragraph.\n\nSecond paragraph.", DescriptionSanitizer.Normalize(input));
    }

    [Fact]
    public void Normalize_Cc_ControlCharacters_AreStripped()
    {
        // \u0001 and \u001F are Cc control characters that must be removed entirely
        // (whitespace categories like \t, \r, \n collapse instead — covered above).
        var input = "alpha\u0001beta\u001Fgamma";
        Assert.Equal("alphabetagamma", DescriptionSanitizer.Normalize(input));
    }

    [Fact]
    public void Normalize_BellCharacter_IsStripped()
    {
        Assert.Equal("warning", DescriptionSanitizer.Normalize("warning\u0007"));
    }

    [Fact]
    public void Normalize_PrintableUnicode_IsPreserved()
    {
        // Smart quotes, en-dash, accented characters must survive normalization.
        var input = "café — résumé “quoted”";
        Assert.Equal("café — résumé “quoted”", DescriptionSanitizer.Normalize(input));
    }

    [Fact]
    public void TruncateAtWordBoundary_ShorterThanMax_ReturnsAsIs()
    {
        Assert.Equal("hello", DescriptionSanitizer.TruncateAtWordBoundary("hello", 100));
    }

    [Fact]
    public void TruncateAtWordBoundary_AppendsEllipsisCharacter()
    {
        var result = DescriptionSanitizer.TruncateAtWordBoundary("hello world from poshmcp", 12);
        Assert.EndsWith("\u2026", result);
        Assert.True(result.Length <= 12);
    }

    [Fact]
    public void TruncateAtWordBoundary_PrefersWordBoundary_OverHardCut()
    {
        // "hello world from poshmcp" — at maxLength 17 a word boundary exists at
        // "hello world from " (length 17 incl. trailing space). The truncator should
        // back up to a boundary and append the ellipsis instead of slicing inside "from".
        var result = DescriptionSanitizer.TruncateAtWordBoundary("hello world from poshmcp", 18);
        Assert.EndsWith("\u2026", result);
        // Either "hello world from\u2026" (16 chars + ellipsis) or "hello world\u2026" — both are
        // valid word-boundary truncations. What matters is that we did NOT cut mid-word
        // (the trailing word "poshmcp" must not appear partially).
        Assert.DoesNotContain("poshmc", result);
        Assert.True(result == "hello world from\u2026" || result == "hello world\u2026");
    }

    [Fact]
    public void TruncateAtWordBoundary_NoWordBoundaryAvailable_FallsBackToHardCut()
    {
        // A single long token longer than the limit must still be truncated; the only
        // available behavior is a hard cut + ellipsis. The result is allowed to consume
        // all of `maxLength` minus the ellipsis character.
        var result = DescriptionSanitizer.TruncateAtWordBoundary("supercalifragilistic", 10);
        Assert.EndsWith("\u2026", result);
        Assert.True(result.Length <= 10);
    }

    [Fact]
    public void TruncateAtWordBoundary_MaxLengthOne_ReturnsSingleEllipsis()
    {
        Assert.Equal("\u2026", DescriptionSanitizer.TruncateAtWordBoundary("anything at all", 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TruncateAtWordBoundary_NullOrEmpty_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, DescriptionSanitizer.TruncateAtWordBoundary(input ?? string.Empty, 50));
    }

    [Fact]
    public void Normalize_ParagraphSeparatorConstant_MatchesDoubleNewline()
    {
        // The FR-540 contract names this the "paragraph separator". Pin it so callers
        // building structured help (PowerShellHelpResolver.JoinMamlParagraphs) cannot
        // diverge from the sanitizer's expectation.
        Assert.Equal("\n\n", DescriptionSanitizer.ParagraphSeparator);
    }
}
