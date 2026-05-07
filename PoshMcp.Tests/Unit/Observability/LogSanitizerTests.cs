using PoshMcp.Server.Observability;
using Xunit;

namespace PoshMcp.Tests.Unit.Observability;

public class LogSanitizerTests
{
    [Fact]
    public void Scrub_Null_ReturnsPlaceholder()
    {
        Assert.Equal("<null>", LogSanitizer.Scrub(null));
    }

    [Fact]
    public void Scrub_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LogSanitizer.Scrub(string.Empty));
    }

    [Fact]
    public void Scrub_PlainAscii_ReturnsUnchanged()
    {
        const string input = "Get-Process -Name pwsh";
        Assert.Same(input, LogSanitizer.Scrub(input));
    }

    [Fact]
    public void Scrub_CrLf_ReplacedWithVisibleEscapes()
    {
        var sanitized = LogSanitizer.Scrub("line1\r\nline2");
        Assert.Equal("line1\\r\\nline2", sanitized);
    }

    [Fact]
    public void Scrub_ForgedLogLine_StaysOnSingleLine()
    {
        // Classic log-forging payload: attacker tries to inject a fake log entry.
        var forged = "real-tool\n2026-05-06 ERROR Forged entry: admin logged in";
        var sanitized = LogSanitizer.Scrub(forged);
        Assert.DoesNotContain('\n', sanitized);
        Assert.DoesNotContain('\r', sanitized);
        Assert.Contains("\\n", sanitized);
    }

    [Fact]
    public void Scrub_OtherControlCharacters_EscapedAsHex()
    {
        // ESC (0x1B) and BEL (0x07) — non-CR/LF controls must still be escaped.
        var sanitized = LogSanitizer.Scrub("alert\u0007escape\u001Bend");
        Assert.Equal("alert\\x07escape\\x1Bend", sanitized);
    }

    [Fact]
    public void Scrub_Tab_EscapedAsBackslashT()
    {
        Assert.Equal("a\\tb", LogSanitizer.Scrub("a\tb"));
    }

    [Fact]
    public void Scrub_LongerThanMaxLength_Truncated()
    {
        var input = new string('a', LogSanitizer.MaxLength + 100);
        var sanitized = LogSanitizer.Scrub(input);
        Assert.StartsWith(new string('a', LogSanitizer.MaxLength), sanitized);
        Assert.EndsWith("…(truncated)", sanitized);
    }

    [Fact]
    public void Scrub_AtMaxLengthWithoutControls_ReturnsUnchanged()
    {
        var input = new string('x', LogSanitizer.MaxLength);
        Assert.Same(input, LogSanitizer.Scrub(input));
    }
}
