using System.CommandLine;

namespace PoshMcp;

/// <summary>
/// Defines the complete CLI structure, options, and command hierarchy for PoshMcp.
/// All Option instances and Command declarations are created here and exposed as static properties
/// for use by SetHandler lambdas in Program.cs.
/// </summary>
public static class CliDefinition
{
    // Root-level options
    public static Option<bool>? EvaluateToolsOption { get; private set; }
    public static Option<bool>? VerboseOption { get; private set; }
    public static Option<bool>? DebugOption { get; private set; }
    public static Option<bool>? TraceOption { get; private set; }

    // Common options used by multiple commands
    public static Option<string?>? ConfigOption { get; private set; }
    public static Option<string?>? LogLevelOption { get; private set; }
    public static Option<string?>? TransportOption { get; private set; }
    public static Option<string?>? FormatOption { get; private set; }
    public static Option<string?>? LogFileOption { get; private set; }
    public static Option<bool>? ForceOption { get; private set; }
    public static Option<bool>? NonInteractiveOption { get; private set; }
    public static Option<string?>? SessionModeOption { get; private set; }
    public static Option<string?>? RuntimeModeOption { get; private set; }
    public static Option<string?>? UrlOption { get; private set; }
    public static Option<string?>? McpPathOption { get; private set; }

    // Configuration update options
    public static Option<string[]>? AddCommandOption { get; private set; }
    public static Option<string[]>? RemoveCommandOption { get; private set; }
    public static Option<string[]>? AddModuleOption { get; private set; }
    public static Option<string[]>? RemoveModuleOption { get; private set; }
    public static Option<string[]>? AddIncludePatternOption { get; private set; }
    public static Option<string[]>? RemoveIncludePatternOption { get; private set; }
    public static Option<string[]>? AddExcludePatternOption { get; private set; }
    public static Option<string[]>? RemoveExcludePatternOption { get; private set; }
    public static Option<string?>? EnableDynamicReloadToolsOption { get; private set; }
    public static Option<string?>? EnableConfigurationTroubleshootingToolOption { get; private set; }
    public static Option<string?>? EnableResultCachingOption { get; private set; }
    public static Option<string?>? UseDefaultDisplayPropertiesOption { get; private set; }
    public static Option<string?>? SetAuthEnabledOption { get; private set; }

    // Build command options
    public static Option<string?>? BuildModulesOption { get; private set; }
    public static Option<string?>? BuildTypeOption { get; private set; }
    public static Option<string?>? BuildTagOption { get; private set; }
    public static Option<string?>? BuildDockerFileOption { get; private set; }
    public static Option<string?>? BuildSourceImageOption { get; private set; }
    public static Option<string?>? BuildSourceTagOption { get; private set; }
    public static Option<bool>? BuildGenerateDockerfileOption { get; private set; }
    public static Option<string?>? BuildDockerfileOutputOption { get; private set; }
    public static Option<string?>? BuildAppSettingsOption { get; private set; }

    // Run command options
    public static Option<string?>? RunModeOption { get; private set; }
    public static Option<int?>? RunPortOption { get; private set; }
    public static Option<string?>? RunTagOption { get; private set; }
    public static Option<string?>? RunConfigOption { get; private set; }
    public static Option<string[]>? RunVolumeOption { get; private set; }
    public static Option<bool>? RunInteractiveOption { get; private set; }

    // Scaffold command options
    public static Option<string?>? ScaffoldProjectPathOption { get; private set; }

    // Commands
    public static Command? ServeCommand { get; private set; }
    public static Command? ListToolsCommand { get; private set; }
    public static Command? ValidateConfigCommand { get; private set; }
    public static Command? DoctorCommand { get; private set; }
    public static Command? CreateConfigCommand { get; private set; }
    public static Command? UpdateConfigCommand { get; private set; }
    public static Command? PsModulePathCommand { get; private set; }
    public static Command? BuildCommand { get; private set; }
    public static Command? RunCommand { get; private set; }
    public static Command? ScaffoldCommand { get; private set; }

    /// <summary>
    /// Builds and returns the fully-wired RootCommand with all options, commands, and hierarchy.
    /// </summary>
    public static RootCommand Build()
    {
        var rootCommand = new RootCommand("PowerShell MCP Server - Provides access to PowerShell commands via Model Context Protocol");

        // Initialize root-level options
        EvaluateToolsOption = new Option<bool>(
            aliases: new[] { "--evaluate-tools", "-e" },
            description: "Evaluate and list discovered PowerShell tools without starting the MCP server");

        VerboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose logging");

        DebugOption = new Option<bool>(
            aliases: new[] { "--debug", "-d" },
            description: "Enable debug logging");

        TraceOption = new Option<bool>(
            aliases: new[] { "--trace", "-t" },
            description: "Enable trace logging");

        // Initialize common options
        ConfigOption = new Option<string?>(
            aliases: new[] { "--config", "-c" },
            description: "Path to configuration file (defaults to appsettings.json resolution)");

        LogLevelOption = new Option<string?>(
            aliases: new[] { "--log-level" },
            description: "Log level: trace|debug|info|warn|error");

        TransportOption = new Option<string?>(
            aliases: new[] { "--transport" },
            description: "Server transport: stdio|sse|http (currently stdio only for this executable)");

        FormatOption = new Option<string?>(
            aliases: new[] { "--format" },
            description: "Output format: text|json");

        ForceOption = new Option<bool>(
            aliases: new[] { "--force", "-f" },
            description: "Overwrite an existing appsettings.json when creating defaults");

        NonInteractiveOption = new Option<bool>(
            aliases: new[] { "--non-interactive" },
            description: "Skip interactive advanced-configuration prompts during updates");

        // Advanced options
        AddCommandOption = new Option<string[]>(
            aliases: new[] { "--add-command" },
            description: "Add one or more command names to PowerShellConfiguration.CommandNames")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        RemoveCommandOption = new Option<string[]>(
            aliases: new[] { "--remove-command" },
            description: "Remove one or more command names from PowerShellConfiguration.CommandNames")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        AddModuleOption = new Option<string[]>(
            aliases: new[] { "--add-module" },
            description: "Add one or more module names to PowerShellConfiguration.Modules")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        RemoveModuleOption = new Option<string[]>(
            aliases: new[] { "--remove-module" },
            description: "Remove one or more module names from PowerShellConfiguration.Modules")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        AddIncludePatternOption = new Option<string[]>(
            aliases: new[] { "--add-include-pattern" },
            description: "Add one or more patterns to PowerShellConfiguration.IncludePatterns")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        RemoveIncludePatternOption = new Option<string[]>(
            aliases: new[] { "--remove-include-pattern" },
            description: "Remove one or more patterns from PowerShellConfiguration.IncludePatterns")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        AddExcludePatternOption = new Option<string[]>(
            aliases: new[] { "--add-exclude-pattern" },
            description: "Add one or more patterns to PowerShellConfiguration.ExcludePatterns")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        RemoveExcludePatternOption = new Option<string[]>(
            aliases: new[] { "--remove-exclude-pattern" },
            description: "Remove one or more patterns from PowerShellConfiguration.ExcludePatterns")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        EnableDynamicReloadToolsOption = new Option<string?>(
            aliases: new[] { "--enable-dynamic-reload-tools" },
            description: "Set PowerShellConfiguration.EnableDynamicReloadTools to true or false");

        EnableConfigurationTroubleshootingToolOption = new Option<string?>(
            aliases: new[] { "--enable-configuration-troubleshooting-tool" },
            description: "Set PowerShellConfiguration.EnableConfigurationTroubleshootingTool to true or false");

        EnableResultCachingOption = new Option<string?>(
            aliases: new[] { "--enable-result-caching" },
            description: "Set PowerShellConfiguration.Performance.EnableResultCaching to true or false");

        UseDefaultDisplayPropertiesOption = new Option<string?>(
            aliases: new[] { "--use-default-display-properties" },
            description: "Set PowerShellConfiguration.Performance.UseDefaultDisplayProperties to true or false");

        SetAuthEnabledOption = new Option<string?>(
            aliases: new[] { "--set-auth-enabled" },
            description: "Set Authentication.Enabled to true or false");

        SessionModeOption = new Option<string?>(
            aliases: new[] { "--session-mode" },
            description: "Session mode hint: stateful|stateless (reserved for hosted transports)");

        RuntimeModeOption = new Option<string?>(
            aliases: new[] { "--runtime-mode" },
            description: "PowerShell runtime mode: in-process|out-of-process");

        UrlOption = new Option<string?>(
            aliases: new[] { "--url" },
            description: "URL bind hint for hosted transports (reserved)");

        McpPathOption = new Option<string?>(
            aliases: new[] { "--mcp-path" },
            description: "MCP endpoint path hint for hosted transports (reserved)");

        // Serve command
        ServeCommand = new Command("serve", "Run the MCP server (stdio transport by default)");
        ServeCommand.AddOption(ConfigOption);
        ServeCommand.AddOption(LogLevelOption);
        ServeCommand.AddOption(TransportOption);
        ServeCommand.AddOption(SessionModeOption);
        ServeCommand.AddOption(RuntimeModeOption);
        ServeCommand.AddOption(UrlOption);
        ServeCommand.AddOption(McpPathOption);

        LogFileOption = new Option<string?>(
            aliases: new[] { "--log-file" },
            description: "Path to log file for stdio transport (suppresses console logging)");
        ServeCommand.AddOption(LogFileOption);

        // List tools command
        ListToolsCommand = new Command("list-tools", "Discover and list tools without starting the MCP server");
        ListToolsCommand.AddOption(ConfigOption);
        ListToolsCommand.AddOption(LogLevelOption);
        ListToolsCommand.AddOption(RuntimeModeOption);
        ListToolsCommand.AddOption(FormatOption);

        // Validate config command
        ValidateConfigCommand = new Command("validate-config", "Validate configuration and tool discovery");
        ValidateConfigCommand.AddOption(ConfigOption);
        ValidateConfigCommand.AddOption(LogLevelOption);
        ValidateConfigCommand.AddOption(RuntimeModeOption);
        ValidateConfigCommand.AddOption(FormatOption);

        // Doctor command
        DoctorCommand = new Command("doctor", "Run runtime and configuration diagnostics");
        DoctorCommand.AddOption(ConfigOption);
        DoctorCommand.AddOption(LogLevelOption);
        DoctorCommand.AddOption(TransportOption);
        DoctorCommand.AddOption(SessionModeOption);
        DoctorCommand.AddOption(RuntimeModeOption);
        DoctorCommand.AddOption(McpPathOption);
        DoctorCommand.AddOption(FormatOption);

        // Create config command
        CreateConfigCommand = new Command("create-config", "Create a default appsettings.json in the current directory");
        CreateConfigCommand.AddOption(ForceOption);
        CreateConfigCommand.AddOption(FormatOption);

        // Update config command
        UpdateConfigCommand = new Command("update-config", "Update settings in the active configuration file (same resolution rules as doctor)");
        UpdateConfigCommand.AddOption(ConfigOption);
        UpdateConfigCommand.AddOption(LogLevelOption);
        UpdateConfigCommand.AddOption(FormatOption);
        UpdateConfigCommand.AddOption(AddCommandOption);
        UpdateConfigCommand.AddOption(RemoveCommandOption);
        UpdateConfigCommand.AddOption(AddModuleOption);
        UpdateConfigCommand.AddOption(RemoveModuleOption);
        UpdateConfigCommand.AddOption(AddIncludePatternOption);
        UpdateConfigCommand.AddOption(RemoveIncludePatternOption);
        UpdateConfigCommand.AddOption(AddExcludePatternOption);
        UpdateConfigCommand.AddOption(RemoveExcludePatternOption);
        UpdateConfigCommand.AddOption(EnableDynamicReloadToolsOption);
        UpdateConfigCommand.AddOption(EnableConfigurationTroubleshootingToolOption);
        UpdateConfigCommand.AddOption(EnableResultCachingOption);
        UpdateConfigCommand.AddOption(UseDefaultDisplayPropertiesOption);
        UpdateConfigCommand.AddOption(SetAuthEnabledOption);
        UpdateConfigCommand.AddOption(RuntimeModeOption);
        UpdateConfigCommand.AddOption(NonInteractiveOption);

        // PSModulePath command
        PsModulePathCommand = new Command("psmodulepath", "Start a PowerShell runspace and report the value of $env:PSModulePath");
        PsModulePathCommand.AddOption(VerboseOption);
        PsModulePathCommand.AddOption(DebugOption);
        PsModulePathCommand.AddOption(TraceOption);

        // Build command
        BuildModulesOption = new Option<string?>(
            aliases: new[] { "--modules" },
            description: "Space-separated module names to pre-install in custom images (e.g., 'Pester Az.Accounts')");

        BuildTypeOption = new Option<string?>(
            aliases: new[] { "--type", "--base" },
            description: "Image type: 'custom' (derived from a source/base image) or 'base' (build local runtime source image). Default: custom");

        BuildTagOption = new Option<string?>(
            aliases: new[] { "--tag" },
            description: "Image tag name (default: poshmcp:latest)");

        BuildDockerFileOption = new Option<string?>(
            aliases: new[] { "--docker-file" },
            description: "Custom Dockerfile path for advanced users");

        BuildSourceImageOption = new Option<string?>(
            aliases: new[] { "--source-image" },
            description: "Source/base image repository or full reference for custom builds (default: ghcr.io/usepowershell/poshmcp/poshmcp:latest)");

        BuildSourceTagOption = new Option<string?>(
            aliases: new[] { "--source-tag" },
            description: "Tag for --source-image when a repository is provided (default: latest)");

        BuildGenerateDockerfileOption = new Option<bool>(
            aliases: new[] { "--generate-dockerfile" },
            description: "Write the generated Dockerfile to disk instead of building");

        BuildDockerfileOutputOption = new Option<string?>(
            aliases: new[] { "--dockerfile-output" },
            description: "Path for the generated Dockerfile (default: ./Dockerfile.generated)");

        BuildAppSettingsOption = new Option<string?>(
            aliases: new[] { "--appsettings" },
            description: "Path to a local appsettings.json to bundle into the image at /app/server/appsettings.json");

        BuildCommand = new Command("build", "Build a Docker image; defaults to creating a custom image from the published GHCR base image");
        BuildCommand.AddOption(BuildModulesOption);
        BuildCommand.AddOption(BuildTypeOption);
        BuildCommand.AddOption(BuildTagOption);
        BuildCommand.AddOption(BuildDockerFileOption);
        BuildCommand.AddOption(BuildSourceImageOption);
        BuildCommand.AddOption(BuildSourceTagOption);
        BuildCommand.AddOption(BuildGenerateDockerfileOption);
        BuildCommand.AddOption(BuildDockerfileOutputOption);
        BuildCommand.AddOption(BuildAppSettingsOption);

        // Run command
        RunModeOption = new Option<string?>(
            aliases: new[] { "--mode" },
            description: "Transport mode: 'http' or 'stdio' (default: http)");

        RunPortOption = new Option<int?>(
            aliases: new[] { "--port" },
            description: "Port number for HTTP mode (default: 8080)");

        RunTagOption = new Option<string?>(
            aliases: new[] { "--tag" },
            description: "Image tag to run (default: poshmcp:latest)");

        RunConfigOption = new Option<string?>(
            aliases: new[] { "--config" },
            description: "Config file path to mount into container");

        RunVolumeOption = new Option<string[]>(
            aliases: new[] { "--volume" },
            description: "Volume mount in format 'source:destination' (repeatable)")
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        RunInteractiveOption = new Option<bool>(
            aliases: new[] { "--interactive", "-it" },
            description: "Run in interactive mode with terminal");

        RunCommand = new Command("run", "Run PoshMcp in a Docker container");
        RunCommand.AddOption(RunModeOption);
        RunCommand.AddOption(RunPortOption);
        RunCommand.AddOption(RunTagOption);
        RunCommand.AddOption(RunConfigOption);
        RunCommand.AddOption(RunVolumeOption);
        RunCommand.AddOption(RunInteractiveOption);

        // Scaffold command
        ScaffoldProjectPathOption = new Option<string?>(
            aliases: new[] { "--project-path", "--path", "-p" },
            description: "Target project directory where infra/azure files will be scaffolded (default: current directory)");

        ScaffoldCommand = new Command("scaffold", "Scaffold embedded infrastructure artifacts into a target project");
        ScaffoldCommand.AddOption(ScaffoldProjectPathOption);
        ScaffoldCommand.AddOption(ForceOption);
        ScaffoldCommand.AddOption(FormatOption);

        // Add all commands to root
        rootCommand.AddCommand(ServeCommand);
        rootCommand.AddCommand(ListToolsCommand);
        rootCommand.AddCommand(ValidateConfigCommand);
        rootCommand.AddCommand(DoctorCommand);
        rootCommand.AddCommand(CreateConfigCommand);
        rootCommand.AddCommand(UpdateConfigCommand);
        rootCommand.AddOption(EvaluateToolsOption);
        rootCommand.AddOption(VerboseOption);
        rootCommand.AddOption(DebugOption);
        rootCommand.AddOption(TraceOption);
        rootCommand.AddCommand(PsModulePathCommand);
        rootCommand.AddCommand(BuildCommand);
        rootCommand.AddCommand(RunCommand);
        rootCommand.AddCommand(ScaffoldCommand);

        return rootCommand;
    }
}
