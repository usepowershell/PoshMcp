using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Collection("TransportSelectionTests")]
public class ProgramCliBuildCommandTests
{
    [Fact]
    public async Task BuildCommand_Help_IncludesSourceImageOptionsAndCustomDefaultDescription()
    {
        using var capture = new ConsoleCapture();

        var result = await Program.Main(new[] { "build", "--help" });

        Assert.Equal(0, result);
        Assert.Contains("--source-image", capture.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--source-tag", capture.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Default: custom", capture.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextWriter _originalOut;
        private readonly TextWriter _originalError;
        private readonly StringWriter _capturedOut;
        private readonly StringWriter _capturedError;

        public string StandardOutput => _capturedOut.ToString();
        public string StandardError => _capturedError.ToString();

        public ConsoleCapture()
        {
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _capturedOut = new StringWriter();
            _capturedError = new StringWriter();

            Console.SetOut(_capturedOut);
            Console.SetError(_capturedError);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            _capturedOut.Dispose();
            _capturedError.Dispose();
        }
    }
}
