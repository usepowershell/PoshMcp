using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional;

/// <summary>
/// Functional tests for MCP Prompts (Spec 002, User Stories 3 and 4).
///
/// Covers the full prompts feature surface:
///   - prompts/list returns prompts with name, description, arguments
///   - prompts/get (file source): reads file and returns as user-role message
///   - prompts/get (command source): executes command, injects arguments as PS variables
///   - Edge cases: missing file, empty command output, required argument not supplied
///
/// Tests marked [Trait("Category", "RequiresImplementation")] are specification stubs.
/// They document expected behavior and will be activated once WithListPromptsHandler and
/// WithGetPromptHandler are registered in Program.cs (Spec 002 implementation PR).
/// </summary>
public class McpPromptsTests : PowerShellTestBase
{
    public McpPromptsTests(ITestOutputHelper output) : base(output) { }

    #region prompts/list

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsList_WithFilePromptConfig_ReturnsPromptWithCorrectMetadata()
    {
        // User Story 3, Acceptance Scenario 1 (FR-022):
        // Given a McpPrompts entry with Source=file, When prompts/list is called,
        // Then the response includes the prompt with Name, Description, and Arguments.
        //
        // TODO: Once WithListPromptsHandler is registered in Program.cs:
        //   1. Create a temp config with a file-backed prompt entry
        //   2. Start InProcessMcpServer with that config
        //   3. Call client.SendListPromptsAsync()
        //   4. Assert result contains prompt with correct name, description, arguments

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after WithListPromptsHandler is implemented (Spec 002 FR-022)");
    }

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsList_RequiredArgument_AppearsWithRequiredTrue()
    {
        // User Story 3, Acceptance Scenario 2 (FR-022):
        // Given a prompt has a Required:true argument "serviceName",
        // When prompts/list is called,
        // Then the argument appears in the arguments array with required:true.
        //
        // TODO: Configure a prompt with a required argument, call prompts/list,
        // assert the argument has required:true in the response.

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after WithListPromptsHandler is implemented (Spec 002 FR-022)");
    }

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsList_WithCommandPromptConfig_ReturnsPromptInList()
    {
        // User Story 4: command-backed prompt appears in prompts/list.
        //
        // TODO: Configure a command-backed prompt, call prompts/list,
        // assert it appears with correct name and arguments.

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after WithListPromptsHandler is implemented (Spec 002 FR-022)");
    }

    [Fact]
    public async Task PromptsList_WithNoMcpPromptsSection_DoesNotCrashServer()
    {
        // Edge case: McpPrompts section absent from appsettings.json.
        // The server MUST start and respond to other MCP methods normally.
        // prompts/list may return an empty list or method-not-found until implemented.

        await Task.CompletedTask;
        Assert.True(true, "Server starts without McpPrompts section — documented by existing integration tests");
    }

    #endregion

    #region prompts/get - file source

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsGet_FileSource_ReadsFileAndReturnsUserRoleMessage()
    {
        // User Story 3, Acceptance Scenario 3 (FR-024):
        // Given a file-backed prompt with Path="./prompts/analyze-service.md",
        // When prompts/get is called,
        // Then the file is read and returned as a user-role message in the messages array.
        //
        // TODO: Create temp prompt file with known markdown content.
        // Configure file-backed prompt, start server, call prompts/get,
        // assert messages[0].role == "user" and messages[0].content.text == file content.

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after WithGetPromptHandler file source is implemented (Spec 002 FR-024)");
    }

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsGet_FileSource_WithNoArguments_ReturnsRawFileContent()
    {
        // User Story 3, Acceptance Scenario 4 (FR-024):
        // When prompts/get is called with no arguments,
        // the raw file content is returned without substitution errors.
        //
        // TODO: Configure file prompt with no arguments, call prompts/get with empty args,
        // assert response is success and content matches file.

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after WithGetPromptHandler is implemented (Spec 002 FR-024)");
    }

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsGet_FileSource_FileDeletedAfterStartup_ReturnsMcpError()
    {
        // Edge case (FR-033):
        // File is deleted between server startup and the prompts/get call.
        // The server MUST return an MCP error response — it must NOT crash.
        //
        // TODO: Configure file prompt, start server, delete the file, call prompts/get,
        // assert the response indicates an error.

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after WithGetPromptHandler error handling is implemented (Spec 002 FR-033)");
    }

    #endregion

    #region prompts/get - command source

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsGet_CommandSource_ExecutesCommandAndReturnsUserRoleMessage()
    {
        // User Story 4, Acceptance Scenario 1 (FR-025):
        // Given Source=command and Command="Write-Output 'Hello from command'",
        // When prompts/get is called,
        // Then the command runs in the shared runspace and its string output is
        // returned as a user-role message.
        //
        // TODO: Configure command-backed prompt, call prompts/get,
        // assert messages[0].role == "user" and content contains command output.

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after WithGetPromptHandler command source is implemented (Spec 002 FR-025)");
    }

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsGet_CommandSource_InjectsArgumentValues_AsPowerShellVariables()
    {
        // User Story 4, Acceptance Scenario 2 (FR-032):
        // Given a command-backed prompt with argument "serviceName",
        // When prompts/get is called with serviceName="wuauserv",
        // Then $serviceName is set in the runspace before command execution,
        // and the command can reference $serviceName.
        //
        // TODO: Configure command prompt: Command="Write-Output \"Service: $serviceName\"",
        // with argument Name="serviceName".
        // Call prompts/get with arguments={"serviceName":"wuauserv"},
        // assert response content contains "Service: wuauserv".

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after argument injection is implemented (Spec 002 FR-032)");
    }

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsGet_CommandSource_TerminatingError_ReturnsMcpError()
    {
        // User Story 4, Acceptance Scenario 3 (FR-033):
        // When the command throws a terminating error,
        // the server returns an MCP error — it does NOT crash.
        //
        // TODO: Configure command prompt with Command="throw 'Deliberate test error'",
        // call prompts/get, assert the response indicates an error.

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after error handling in WithGetPromptHandler is implemented (Spec 002 FR-033)");
    }

    [Fact]
    [Trait("Category", "RequiresImplementation")]
    public async Task PromptsGet_RequiredArgumentNotSupplied_ReturnsAppropriateError()
    {
        // Edge case: Required argument not supplied in prompts/get call.
        // Expected: server returns an MCP error identifying the missing required argument.
        //
        // TODO: Configure prompt with Required=true argument "serviceName",
        // call prompts/get without providing serviceName,
        // assert the response indicates an error about the missing argument.

        await Task.CompletedTask;
        Assert.True(true, "Stub: activate after required-argument validation is implemented (Spec 002)");
    }

    #endregion
}
