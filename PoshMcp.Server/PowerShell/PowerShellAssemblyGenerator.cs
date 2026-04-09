using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.Metrics;
using PoshMcp.Server.Observability;

namespace PoshMcp.Server.PowerShell;

/// <summary>
/// Instance-based PowerShell dynamic assembly generator for better test isolation
/// </summary>
public class PowerShellAssemblyGenerator
{
    private const int FrameworkParameterCount = 3;
    private const string AllPropertiesParameterName = "_AllProperties";
    private const string MaxResultsParameterName = "_MaxResults";
    private const string RequestedPropertiesParameterName = "_RequestedProperties";

    public Assembly? GeneratedAssembly => _generatedAssembly;

    /// <summary>
    /// Gets the PowerShell runspace associated with this assembly generator.
    /// Internal visibility allows McpToolFactoryV2 to access the runspace for session-aware scenarios,
    /// where different runspace implementations (SingletonPowerShellRunspace vs SessionAwarePowerShellRunspace)
    /// need to be inspected to ensure proper tool generation and execution isolation.
    /// </summary>
    internal IPowerShellRunspace PowerShellRunspace => _powerShellRunspace;
    private readonly IPowerShellRunspace _powerShellRunspace;
    private Assembly? _generatedAssembly;
    private Type? _generatedType;
    private object? _generatedInstance;
    private readonly object _lock = new object();

    // Static metrics instance for instrumentation
    private static McpMetrics? _metrics;

    // Static configuration and runtime state for caching resolution
    private static PowerShellConfiguration? _powerShellConfig;
    private static RuntimeCachingState? _runtimeCachingState;

    public static string SanitizeMethodName(string commandName, string? parameterSetName = null)
    {
        return SanitizeMethodName_Internal(commandName, parameterSetName);
    }

    private static string CamelCaseToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Use a regex to insert underscores before uppercase letters
        var result = System.Text.RegularExpressions.Regex.Replace(input, "([a-z0-9])([A-Z])", "$1_$2");
        // replace - with _
        result = result.Replace("-", "_");
        // remove duplicate underscores
        result = System.Text.RegularExpressions.Regex.Replace(result, "_+", "_");
        return result.ToLowerInvariant();
    }

    /// <summary>
    /// Sanitizes a command name to make it a valid C# method name
    /// </summary>
    private static string SanitizeMethodName_Internal(string commandName, string? parameterSetName = null)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return "UnnamedCommand";
        }

        // Convert CamelCase to snake_case
        var sanitized = CamelCaseToSnakeCase(commandName);

        // Ensure it doesn't start with a digit
        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        if (!(string.IsNullOrWhiteSpace(parameterSetName) || parameterSetName == "__AllParameterSets"))
        {
            sanitized += "_" + CamelCaseToSnakeCase(parameterSetName);
        }

        // Ensure it's not empty after sanitization
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "UnnamedCommand";
        }

        return sanitized;
    }

    public PowerShellAssemblyGenerator(IPowerShellRunspace powerShellRunspace)
    {
        _powerShellRunspace = powerShellRunspace ?? throw new ArgumentNullException(nameof(powerShellRunspace));
    }

    /// <summary>
    /// Sets the metrics instance for OpenTelemetry instrumentation
    /// </summary>
    /// <param name="metrics">McpMetrics instance</param>
    public static void SetMetrics(McpMetrics metrics)
    {
        _metrics = metrics;
    }

    /// <summary>
    /// Sets the PowerShell configuration for caching resolution.
    /// </summary>
    public static void SetConfiguration(PowerShellConfiguration config)
    {
        _powerShellConfig = config;
    }

    /// <summary>
    /// Sets the runtime caching state for dynamic override resolution.
    /// </summary>
    public static void SetRuntimeCachingState(RuntimeCachingState state)
    {
        _runtimeCachingState = state;
    }

    /// <summary>
    /// Resolves whether result caching is enabled for the given command using a 5-layer priority:
    /// runtime per-function > runtime global > config per-function > config global > default (false).
    /// </summary>
    public static bool ResolveCachingSetting(string commandName)
    {
        // Layers 1 & 2: Runtime overrides (per-function takes priority over global)
        var runtimeResolved = _runtimeCachingState?.Resolve(commandName);
        if (runtimeResolved.HasValue)
        {
            return runtimeResolved.Value;
        }

        // Layer 3: Config per-function override
        if (_powerShellConfig?.FunctionOverrides.TryGetValue(commandName, out var funcOverride) == true
            && funcOverride.EnableResultCaching.HasValue)
        {
            return funcOverride.EnableResultCaching.Value;
        }

        // Layer 4: Config global; Layer 5: default false
        return _powerShellConfig?.Performance.EnableResultCaching ?? false;
    }

    /// <summary>
    /// Generates or retrieves the cached in-memory assembly containing PowerShell command methods
    /// </summary>
    /// <param name="commands">List of PowerShell commands to generate methods for</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>The generated assembly</returns>
    public Assembly GenerateAssembly(IEnumerable<CommandInfo> commands, ILogger logger)
    {
        lock (_lock)
        {
            if (_generatedAssembly != null)
            {
                logger.LogInformationWithCorrelation("Returning cached generated assembly");
                return _generatedAssembly;
            }

            using (OperationContext.BeginOperation("GenerateAssembly"))
            {
                logger.LogInformationWithCorrelation("Generating new in-memory assembly for PowerShell commands");

                // Create a dynamic assembly
                var assemblyName = new AssemblyName($"PowerShellCommandsAssembly_{Guid.NewGuid():N}");
                var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                    assemblyName,
                    AssemblyBuilderAccess.Run);

                // Create a module
                var moduleBuilder = assemblyBuilder.DefineDynamicModule("PowerShellCommandsModule");

                // Create a class to hold all the methods
                var typeBuilder = moduleBuilder.DefineType(
                    "PowerShellCommands",
                    TypeAttributes.Public | TypeAttributes.Class);

                // Add a logger field
                var loggerField = typeBuilder.DefineField(
                    "_logger",
                    typeof(ILogger),
                    FieldAttributes.Private | FieldAttributes.InitOnly);

                // Add a runspace field
                var runspaceField = typeBuilder.DefineField(
                    "_runspace",
                    typeof(IPowerShellRunspace),
                    FieldAttributes.Private | FieldAttributes.InitOnly);

                // Generate constructor
                GenerateConstructor(typeBuilder, loggerField, runspaceField);

                // Generate methods for each command
                var commandList = commands.ToList();
                foreach (var command in commandList)
                {
                    foreach (var parameterSet in command.ParameterSets)
                    {
                        try
                        {
                            GenerateMethodForCommand(typeBuilder, command, loggerField, runspaceField, logger, parameterSet);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, $"Failed to generate method for command {command.Name}: {ex.Message}");
                        }
                    }
                }

                // Generate utility methods for cached data operations
                try
                {
                    GenerateGetLastCommandOutputMethod(typeBuilder, loggerField, runspaceField);
                    GenerateSortLastCommandOutputMethod(typeBuilder, loggerField, runspaceField);
                    GenerateFilterLastCommandOutputMethod(typeBuilder, loggerField, runspaceField);
                    GenerateGroupLastCommandOutputMethod(typeBuilder, loggerField, runspaceField);
                    logger.LogDebug("Generated utility methods for cached data operations");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Failed to generate utility methods: {ex.Message}");
                }

                // Create the type
                _generatedType = typeBuilder.CreateType();
                _generatedAssembly = _generatedType.Assembly;

                logger.LogInformationWithCorrelation($"Successfully generated assembly with {commandList.Count} command methods");
                return _generatedAssembly;
            }
        }
    }

    /// <summary>
    /// Gets an instance of the generated PowerShell commands class
    /// </summary>
    /// <param name="logger">Logger instance to inject</param>
    /// <returns>Instance of the generated class</returns>
    public object GetGeneratedInstance(ILogger logger)
    {
        lock (_lock)
        {
            if (_generatedInstance != null && _generatedType != null)
            {
                return _generatedInstance;
            }

            if (_generatedType == null)
            {
                throw new InvalidOperationException("Assembly has not been generated yet. Call GenerateAssembly first.");
            }

            // Create instance with logger and runspace
            _generatedInstance = Activator.CreateInstance(_generatedType, logger, _powerShellRunspace)!;
            return _generatedInstance;
        }
    }

    /// <summary>
    /// Gets all generated methods from the assembly
    /// </summary>
    /// <returns>Dictionary mapping command names to their generated methods</returns>
    public Dictionary<string, MethodInfo> GetGeneratedMethods()
    {
        lock (_lock)
        {
            if (_generatedType == null)
            {
                throw new InvalidOperationException("Assembly has not been generated yet. Call GenerateAssembly first.");
            }

            var methods = new Dictionary<string, MethodInfo>();
            var allMethods = _generatedType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in allMethods)
            {
                // Skip constructor and inherited methods
                if (method.Name != ".ctor" && method.DeclaringType == _generatedType)
                {
                    methods[method.Name] = method;
                }
            }

            return methods;
        }
    }

    /// <summary>
    /// Clears the cached assembly and instance
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _generatedAssembly = null;
            _generatedType = null;
            _generatedInstance = null;
        }
    }

    // The rest of the methods remain the same but are now instance methods...
    // I'll continue with the key changes for the constructor and method generation

    private static void GenerateConstructor(TypeBuilder typeBuilder, FieldBuilder loggerField, FieldBuilder runspaceField)
    {
        var constructorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(ILogger), typeof(IPowerShellRunspace) });

        var il = constructorBuilder.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

        // Set logger field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, loggerField);

        // Set runspace field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, runspaceField);

        il.Emit(OpCodes.Ret);
    }

    private static void GenerateMethodForCommand(TypeBuilder typeBuilder, CommandInfo commandInfo, FieldBuilder loggerField, FieldBuilder runspaceField, ILogger logger, CommandParameterSetInfo parameterSet)
    {
        // Get command parameters for this specific parameter set (excluding common parameters) and order by position
        var parameters = commandInfo.Parameters
            .Where(p => !PowerShellParameterUtils.IsCommonParameter(p.Key))
            .Where(p =>
            {
                // Check if this parameter belongs to the current parameter set
                var paramAttrs = p.Value.Attributes.OfType<ParameterAttribute>();
                return paramAttrs.Any(attr =>
                    string.IsNullOrEmpty(attr.ParameterSetName) ||
                    attr.ParameterSetName == parameterSet.Name ||
                    attr.ParameterSetName == "__AllParameterSets");
            })
            .OrderBy(p =>
            {
                // Get the position from the ParameterAttribute for this parameter set
                var paramAttr = p.Value.Attributes.OfType<ParameterAttribute>()
                    .FirstOrDefault(attr =>
                        string.IsNullOrEmpty(attr.ParameterSetName) ||
                        attr.ParameterSetName == parameterSet.Name ||
                        attr.ParameterSetName == "__AllParameterSets");
                return paramAttr?.Position ?? int.MaxValue; // Parameters without position go to the end
            })
            .ThenBy(p => p.Key) // Secondary sort by name for stable ordering
            .ToList();

        // Create method name (sanitized) - append parameter set name unless it's __AllParameterSets
        var methodName = PowerShellAssemblyGenerator.SanitizeMethodName(commandInfo.Name, parameterSet.Name);

        logger.LogDebug($"Generating method '{methodName}' for command '{commandInfo.Name}' parameter set '{parameterSet.Name}' with {parameters.Count} parameters");

        // Build parameter types array
        var parameterTypes = new List<Type>();
        var parameterNames = new List<string>();
        var parameterMandatory = new List<bool>();

        foreach (var param in parameters)
        {
            var paramType = param.Value.ParameterType;
            var isMandatory = param.Value.Attributes.OfType<ParameterAttribute>().Any(attr => attr.Mandatory);

            // Make non-mandatory value types nullable
            if (!isMandatory && paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null)
            {
                paramType = typeof(Nullable<>).MakeGenericType(paramType);
            }

            parameterTypes.Add(paramType);
            parameterNames.Add(SanitizeParameterName(param.Key));
            parameterMandatory.Add(isMandatory);
        }

        // Add PoshMcp framework parameters (underscore prefix = not passed to PowerShell)
        parameterTypes.Add(typeof(bool?));
        parameterNames.Add("_AllProperties");
        parameterMandatory.Add(false);

        parameterTypes.Add(typeof(int?));
        parameterNames.Add("_MaxResults");
        parameterMandatory.Add(false);

        parameterTypes.Add(typeof(string[]));
        parameterNames.Add("_RequestedProperties");
        parameterMandatory.Add(false);

        // Add CancellationToken parameter (always last)
        parameterTypes.Add(typeof(CancellationToken));
        parameterNames.Add("cancellationToken");
        parameterMandatory.Add(false); // CancellationToken is not mandatory

        // Define the method
        var methodBuilder = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            typeof(Task<string>),
            parameterTypes.ToArray());

        // Set parameter names and default values
        for (int i = 0; i < parameterNames.Count; i++)
        {
            ParameterBuilder paramBuilder;

            // Set default values for non-mandatory parameters
            if (!parameterMandatory[i])
            {
                if (i == parameterNames.Count - 1) // CancellationToken
                {
                    // CancellationToken with default
                    var paramAttributes = ParameterAttributes.Optional | ParameterAttributes.HasDefault;
                    paramBuilder = methodBuilder.DefineParameter(i + 1, paramAttributes, parameterNames[i]);
                }
                else
                {
                    var paramType = parameterTypes[i];
                    var paramAttributes = ParameterAttributes.Optional | ParameterAttributes.HasDefault;
                    paramBuilder = methodBuilder.DefineParameter(i + 1, paramAttributes, parameterNames[i]);

                    try
                    {
                        // Set appropriate default value based on type
                        if (paramType == typeof(string))
                        {
                            paramBuilder.SetConstant(null);
                        }
                        else if (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) != null)
                        {
                            // Nullable value type - default to null
                            paramBuilder.SetConstant(null);
                        }
                        else if (paramType.IsValueType)
                        {
                            // For non-nullable value types, we'll handle defaults in the method body
                            // since SetConstant doesn't work well with all value types in IL generation
                            if (paramType == typeof(bool))
                            {
                                paramBuilder.SetConstant(false);
                            }
                            else if (paramType == typeof(int))
                            {
                                paramBuilder.SetConstant(0);
                            }
                            else if (paramType == typeof(long))
                            {
                                paramBuilder.SetConstant(0L);
                            }
                            else if (paramType == typeof(double))
                            {
                                paramBuilder.SetConstant(0.0);
                            }
                            // For other value types, leave without constant (will be handled as nullable)
                        }
                        else if (paramType.IsArray || !paramType.IsValueType)
                        {
                            // Reference types and arrays default to null
                            paramBuilder.SetConstant(null);
                        }
                    }
                    catch (Exception)
                    {
                        // If SetConstant fails for any reason, just mark as optional without constant
                        // The method will handle null/default values appropriately
                    }
                }
            }
            else
            {
                // Mandatory parameter
                paramBuilder = methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, parameterNames[i]);
            }
        }

        // Generate method body
        GenerateMethodBody(methodBuilder, commandInfo, parameters, loggerField, runspaceField);
    }

    private static void GenerateMethodBody(MethodBuilder methodBuilder, CommandInfo commandInfo, List<KeyValuePair<string, ParameterMetadata>> parameters, FieldBuilder loggerField, FieldBuilder runspaceField)
    {
        var il = methodBuilder.GetILGenerator();

        // Get the ExecutePowerShellCommandTyped method to call
        var executeMethod = typeof(PowerShellAssemblyGenerator).GetMethod(
            nameof(ExecutePowerShellCommandTyped),
            BindingFlags.Static | BindingFlags.Public)!;

        // Create array to hold parameter information
        var parameterInfoArrayLocal = il.DeclareLocal(typeof(PowerShellParameterInfo[]));
        var parametersLocal = il.DeclareLocal(typeof(object[]));

        // Create parameter info array
        il.Emit(OpCodes.Ldc_I4, parameters.Count + FrameworkParameterCount);
        il.Emit(OpCodes.Newarr, typeof(PowerShellParameterInfo));
        il.Emit(OpCodes.Stloc, parameterInfoArrayLocal);

        // Populate parameter info array
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];

            il.Emit(OpCodes.Ldloc, parameterInfoArrayLocal);
            il.Emit(OpCodes.Ldc_I4, i);

            // Create PowerShellParameterInfo instance
            il.Emit(OpCodes.Ldstr, param.Key); // name
            il.Emit(OpCodes.Ldtoken, param.Value.ParameterType); // type
            il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);

            // Check if mandatory
            var isMandatory = param.Value.Attributes.OfType<ParameterAttribute>().Any(attr => attr.Mandatory);
            if (isMandatory)
                il.Emit(OpCodes.Ldc_I4_1);
            else
                il.Emit(OpCodes.Ldc_I4_0);

            il.Emit(OpCodes.Newobj, typeof(PowerShellParameterInfo).GetConstructor(new[] { typeof(string), typeof(Type), typeof(bool) })!);
            il.Emit(OpCodes.Stelem_Ref);
        }

        // Add framework parameter metadata (_AllProperties, _MaxResults, _RequestedProperties)
        EmitFrameworkParameterInfo(il, parameterInfoArrayLocal, parameters.Count, AllPropertiesParameterName, typeof(bool?));
        EmitFrameworkParameterInfo(il, parameterInfoArrayLocal, parameters.Count + 1, MaxResultsParameterName, typeof(int?));
        EmitFrameworkParameterInfo(il, parameterInfoArrayLocal, parameters.Count + 2, RequestedPropertiesParameterName, typeof(string[]));

        // Create method parameter types array for boxing
        var methodParameterTypes = new List<Type>();
        foreach (var param in parameters)
        {
            var paramType = param.Value.ParameterType;
            var isMandatory = param.Value.Attributes.OfType<ParameterAttribute>().Any(attr => attr.Mandatory);

            // Make non-mandatory value types nullable
            if (!isMandatory && paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null)
            {
                paramType = typeof(Nullable<>).MakeGenericType(paramType);
            }

            methodParameterTypes.Add(paramType);
        }

        // Create object array for parameter values
        il.Emit(OpCodes.Ldc_I4, parameters.Count + FrameworkParameterCount);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, parametersLocal);

        // Populate parameter values array
        for (int i = 0; i < parameters.Count; i++)
        {
            il.Emit(OpCodes.Ldloc, parametersLocal);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldarg, i + 1); // +1 because arg 0 is 'this'

            // Box value types using the actual method parameter type
            var methodParamType = methodParameterTypes[i];
            if (methodParamType.IsValueType)
            {
                il.Emit(OpCodes.Box, methodParamType);
            }

            il.Emit(OpCodes.Stelem_Ref);
        }

        // Populate framework parameter values from generated method args
        EmitFrameworkParameterValue(il, parametersLocal, parameters.Count, parameters.Count + 1, typeof(bool?));
        EmitFrameworkParameterValue(il, parametersLocal, parameters.Count + 1, parameters.Count + 2, typeof(int?));
        // _RequestedProperties is already a reference type (string[])
        il.Emit(OpCodes.Ldloc, parametersLocal);
        il.Emit(OpCodes.Ldc_I4, parameters.Count + 2);
        il.Emit(OpCodes.Ldarg, parameters.Count + 3);
        il.Emit(OpCodes.Stelem_Ref);

        // Call ExecutePowerShellCommandTyped
        il.Emit(OpCodes.Ldstr, commandInfo.Name); // command name
        il.Emit(OpCodes.Ldloc, parameterInfoArrayLocal); // parameter info
        il.Emit(OpCodes.Ldloc, parametersLocal); // parameter values
        il.Emit(OpCodes.Ldarg, parameters.Count + FrameworkParameterCount + 1); // CancellationToken (last parameter)
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, runspaceField); // runspace
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, loggerField); // logger
        il.Emit(OpCodes.Call, executeMethod);

        il.Emit(OpCodes.Ret);
    }

    private static void EmitFrameworkParameterInfo(ILGenerator il, LocalBuilder parameterInfoArrayLocal, int index, string name, Type type)
    {
        il.Emit(OpCodes.Ldloc, parameterInfoArrayLocal);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Ldtoken, type);
        il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(PowerShellParameterInfo).GetConstructor(new[] { typeof(string), typeof(Type), typeof(bool) })!);
        il.Emit(OpCodes.Stelem_Ref);
    }

    private static void EmitFrameworkParameterValue(ILGenerator il, LocalBuilder parametersLocal, int arrayIndex, int argumentIndex, Type parameterType)
    {
        il.Emit(OpCodes.Ldloc, parametersLocal);
        il.Emit(OpCodes.Ldc_I4, arrayIndex);
        il.Emit(OpCodes.Ldarg, argumentIndex);
        il.Emit(OpCodes.Box, parameterType);
        il.Emit(OpCodes.Stelem_Ref);
    }

    /// <summary>
    /// Static method called by generated IL code to execute PowerShell commands
    /// </summary>
    public static async Task<string> ExecutePowerShellCommandTyped(
        string commandName,
        PowerShellParameterInfo[] parameterInfos,
        object[] parameterValues,
        CancellationToken cancellationToken,
        IPowerShellRunspace runspace,
        ILogger logger)
    {
        var stopwatch = Stopwatch.StartNew();
        var status = "success";
        var errorType = "";

        try
        {
            using (OperationContext.BeginOperation(commandName))
            using (logger.BeginCorrelationScope())
            {
                var invocationId = OperationContext.CorrelationId;
                var parameterCount = parameterValues?.Length ?? 0;

                logger.LogInformation(
                    "Tool invocation received: ToolName={ToolName}, InvocationId={InvocationId}, ParameterCount={ParameterCount}",
                    commandName,
                    invocationId,
                    parameterCount);

                logger.LogDebug(
                    "Tool invocation stage: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                    "request_received",
                    commandName,
                    invocationId,
                    stopwatch.ElapsedMilliseconds);

                var parameterSummary = FormatParameterSummary(parameterInfos, parameterValues);
                logger.LogDebug(
                    "Tool invocation stage: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, ParameterSummary={ParameterSummary}, ElapsedMs={ElapsedMs}",
                    "tool_resolved",
                    commandName,
                    invocationId,
                    parameterSummary,
                    stopwatch.ElapsedMilliseconds);

                var frameworkOptions = ResolveFrameworkExecutionOptions(parameterInfos, parameterValues, commandName, logger);

                // Record tool invocation start
                _metrics?.ToolInvocationTotal.Add(1,
                    new TagList {
                        { "tool_name", commandName },
                        { "status", "started" },
                        { "correlation_id", OperationContext.CorrelationId }
                    });

                // Log parameter details
                if (parameterInfos != null && parameterValues != null)
                {
                    for (int i = 0; i < parameterInfos.Length && i < parameterValues.Length; i++)
                    {
                        var paramInfo = parameterInfos[i];
                        var paramValue = parameterValues[i];
                        logger.LogDebug(
                            "Tool parameter detail: ToolName={ToolName}, InvocationId={InvocationId}, Index={Index}, Name={ParameterName}, Type={ParameterType}, Value={ParameterValue}",
                            commandName,
                            invocationId,
                            i,
                            paramInfo.Name,
                            paramInfo.Type.Name,
                            paramValue);
                    }
                }

                return await runspace.ExecuteThreadSafeAsync<string>(ps =>
                {
                    try
                    {
                        // Ensure the PowerShell instance is in a clean state
                        if (ps.InvocationStateInfo.State != PSInvocationState.NotStarted &&
                            ps.InvocationStateInfo.State != PSInvocationState.Completed)
                        {
                            // If PowerShell is in an invalid state, stop it first
                            if (ps.InvocationStateInfo.State == PSInvocationState.Running)
                            {
                                ps.Stop();
                            }
                        }

                        ps.Commands.Clear();
                        ps.AddCommand(commandName);

                        logger.LogDebug(
                            "Tool invocation stage: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                            "pipeline_initialized",
                            commandName,
                            invocationId,
                            stopwatch.ElapsedMilliseconds);

                        // Add parameters
                        for (int i = 0; i < (parameterInfos?.Length ?? 0) && i < (parameterValues?.Length ?? 0); i++)
                        {
                            var paramInfo = parameterInfos![i];
                            var paramValue = parameterValues![i];

                            if (paramInfo.Name.StartsWith("_", StringComparison.Ordinal))
                            {
                                // Framework parameters are consumed by PoshMcp and must not be passed to PowerShell.
                                continue;
                            }

                            if (paramValue != null)
                            {
                                // Convert parameter value to the expected type
                                var convertedValue = PowerShellParameterUtils.ConvertParameterValue(
                                    paramValue, paramInfo.Type, paramInfo.Name, logger);

                                if (convertedValue is SwitchParameter switchParam)
                                {
                                    if (switchParam.IsPresent)
                                    {
                                        ps.AddParameter(paramInfo.Name);
                                        logger.LogDebug(
                                            "Bound switch parameter: ToolName={ToolName}, InvocationId={InvocationId}, ParameterName={ParameterName}, IsPresent={IsPresent}",
                                            commandName,
                                            invocationId,
                                            paramInfo.Name,
                                            switchParam.IsPresent);
                                    }
                                }
                                else
                                {
                                    ps.AddParameter(paramInfo.Name, convertedValue);
                                    logger.LogDebug(
                                        "Bound parameter: ToolName={ToolName}, InvocationId={InvocationId}, ParameterName={ParameterName}, ValueType={ValueType}, Value={Value}",
                                        commandName,
                                        invocationId,
                                        paramInfo.Name,
                                        convertedValue?.GetType().Name,
                                        convertedValue);
                                }
                            }
                            else if (paramInfo.IsMandatory)
                            {
                                throw new ArgumentException($"Mandatory parameter '{paramInfo.Name}' cannot be null");
                            }
                        }

                        logger.LogDebug(
                            "Tool invocation stage: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                            "parameters_bound_normalized",
                            commandName,
                            invocationId,
                            stopwatch.ElapsedMilliseconds);

                        // Build a single Select-Object stage so _MaxResults and property filtering compose consistently.
                        var selectedProperties = ResolveSelectedProperties(commandName, frameworkOptions);
                        var shouldApplyPropertySelection = selectedProperties != null;
                        var shouldApplyMaxResults = frameworkOptions.MaxResults.HasValue && frameworkOptions.MaxResults.Value > 0;

                        if (shouldApplyPropertySelection || shouldApplyMaxResults)
                        {
                            ps.AddCommand("Select-Object");

                            if (shouldApplyPropertySelection)
                            {
                                ps.AddParameter("Property", selectedProperties!.ToArray());
                            }

                            if (shouldApplyMaxResults)
                            {
                                ps.AddParameter("First", frameworkOptions.MaxResults!.Value);
                            }

                            logger.LogInformation(
                                "Result shaping decision: ToolName={ToolName}, InvocationId={InvocationId}, ApplyPropertySelection={ApplyPropertySelection}, SelectedPropertyCount={SelectedPropertyCount}, MaxResults={MaxResults}",
                                commandName,
                                invocationId,
                                shouldApplyPropertySelection,
                                selectedProperties?.Count ?? 0,
                                frameworkOptions.MaxResults);
                        }

                        // Conditionally pipe to Tee-Object to cache results in $LastCommandOutput
                        bool enableCaching = ResolveCachingSetting(commandName);
                        logger.LogInformation(
                            "Result caching decision: ToolName={ToolName}, EnableCaching={EnableCaching}, InvocationId={InvocationId}",
                            commandName,
                            enableCaching,
                            invocationId);
                        if (enableCaching)
                        {
                            ps.AddCommand("Tee-Object")
                              .AddParameter("Variable", "LastCommandOutput");
                        }

                        logger.LogInformation(
                            "PowerShell pipeline starting: ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                            commandName,
                            invocationId,
                            stopwatch.ElapsedMilliseconds);

                        Collection<PSObject> results;
                        try
                        {
                            var safeResults = InvokePowerShellSafe(ps, logger, $"executing {commandName}");
                            results = safeResults ?? new Collection<PSObject>();

                            logger.LogInformation(
                                "PowerShell pipeline completed: ToolName={ToolName}, InvocationId={InvocationId}, ResultCount={ResultCount}, ElapsedMs={ElapsedMs}",
                                commandName,
                                invocationId,
                                results.Count,
                                stopwatch.ElapsedMilliseconds);
                        }
                        catch (CommandNotFoundException cmdEx)
                        {
                            status = "error";
                            errorType = "command_not_found";
                            logger.LogWarning(
                                cmdEx,
                                "PowerShell command not found: ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                                commandName,
                                invocationId,
                                stopwatch.ElapsedMilliseconds);
                            ps.Commands.Clear();
                            return Task.FromResult($"{{\"error\": \"The term '{commandName}' is not recognized as a name of a cmdlet, function, script file, or executable program.\"}}");
                        }
                        catch (Exception ex)
                        {
                            status = "error";
                            errorType = "execution_failed";
                            logger.LogWarning(
                                ex,
                                "PowerShell pipeline failed: ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                                commandName,
                                invocationId,
                                stopwatch.ElapsedMilliseconds);
                            ps.Commands.Clear();
                            return Task.FromResult($"{{\"error\": \"Command execution failed: {ex.Message}\"}}");
                        }

                        // Handle errors
                        if (ps.HadErrors)
                        {
                            status = "error";
                            errorType = "powershell_errors";
                            var errors = ps.Streams.Error.ReadAll();
                            var errorMessage = string.Join("; ", errors.Select(e => e.ToString()));
                            logger.LogWarning(
                                "PowerShell command completed with errors: ToolName={ToolName}, InvocationId={InvocationId}, ErrorCount={ErrorCount}, Errors={Errors}, ElapsedMs={ElapsedMs}",
                                commandName,
                                invocationId,
                                errors.Count,
                                errorMessage,
                                stopwatch.ElapsedMilliseconds);
                            ps.Commands.Clear();
                            return Task.FromResult($"{{\"error\": \"Command completed with errors: {errorMessage}\"}}");
                        }

                        // Convert results to JSON using System.Text.Json (much faster than PowerShell's ConvertTo-Json)
                        if (results.Count == 0)
                        {
                            logger.LogDebug(
                                "Tool invocation stage: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                                "result_shaping_empty",
                                commandName,
                                invocationId,
                                stopwatch.ElapsedMilliseconds);
                            ps.Commands.Clear();
                            return Task.FromResult("[]");
                        }

                        try
                        {
                            logger.LogDebug(
                                "Tool invocation stage: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, ResultCount={ResultCount}, ElapsedMs={ElapsedMs}",
                                "result_shaping_started",
                                commandName,
                                invocationId,
                                results.Count,
                                stopwatch.ElapsedMilliseconds);

                            ps.Commands.Clear();

                            // Serialize directly using System.Text.Json with custom PSObject converter
                            var resultsArray = results.ToArray();
                            var jsonOutput = JsonSerializer.Serialize(resultsArray, PowerShellJsonOptions.Options);

                            logger.LogDebug(
                                "Tool invocation stage: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, JsonLength={JsonLength}, ElapsedMs={ElapsedMs}",
                                "result_shaping_completed",
                                commandName,
                                invocationId,
                                jsonOutput.Length,
                                stopwatch.ElapsedMilliseconds);
                            return Task.FromResult(jsonOutput);
                        }
                        catch (Exception ex)
                        {
                            status = "error";
                            errorType = "json_serialization_failed";
                            logger.LogWarning(
                                ex,
                                "Result shaping failed: ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                                commandName,
                                invocationId,
                                stopwatch.ElapsedMilliseconds);
                            ps.Commands.Clear();
                            return Task.FromResult($"{{\"error\": \"Failed to serialize results to JSON: {ex.Message}\"}}");
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        status = "cancelled";
                        errorType = "operation_cancelled";
                        logger.LogWarning(
                            ex,
                            "Tool invocation cancelled: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                            "pipeline_execution",
                            commandName,
                            invocationId,
                            stopwatch.ElapsedMilliseconds);
                        ps.Commands.Clear();
                        throw;
                    }
                    catch (TimeoutException ex)
                    {
                        status = "timeout";
                        errorType = "timeout";
                        logger.LogWarning(
                            ex,
                            "Tool invocation timed out: Stage={Stage}, ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                            "pipeline_execution",
                            commandName,
                            invocationId,
                            stopwatch.ElapsedMilliseconds);
                        ps.Commands.Clear();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        status = "error";
                        errorType = "unexpected_error";
                        logger.LogWarning(
                            ex,
                            "Unexpected error during tool invocation: ToolName={ToolName}, InvocationId={InvocationId}, ElapsedMs={ElapsedMs}",
                            commandName,
                            invocationId,
                            stopwatch.ElapsedMilliseconds);
                        ps.Commands.Clear();
                        return Task.FromResult($"{{\"error\": \"Unexpected error: {ex.Message}\"}}");
                    }
                });
            }
        }
        catch (OperationCanceledException ex)
        {
            status = "cancelled";
            errorType = "operation_cancelled";
            logger.LogWarningWithCorrelation(
                "Tool invocation cancelled before completion: ToolName={ToolName}, ElapsedMs={ElapsedMs}, CancellationRequested={CancellationRequested}",
                commandName,
                stopwatch.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
            throw new InvalidOperationException($"Execution cancelled for {commandName}: {ex.Message}", ex);
        }
        catch (TimeoutException ex)
        {
            status = "timeout";
            errorType = "timeout";
            logger.LogWarningWithCorrelation(
                "Tool invocation timeout: ToolName={ToolName}, ElapsedMs={ElapsedMs}",
                commandName,
                stopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException($"Execution timeout for {commandName}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            status = "error";
            errorType = "critical_error";
            logger.LogErrorWithCorrelation(
                ex,
                "Error executing PowerShell command: ToolName={ToolName}, ElapsedMs={ElapsedMs}",
                commandName,
                stopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException($"Error executing {commandName}: {ex.Message}", ex);
        }
        finally
        {
            // Record metrics for the completed operation
            stopwatch.Stop();

            if (status == "success")
            {
                logger.LogInformation(
                    "Tool invocation completed: ToolName={ToolName}, Status={Status}, ElapsedMs={ElapsedMs}",
                    commandName,
                    status,
                    stopwatch.ElapsedMilliseconds);
            }
            else if (status == "cancelled" || status == "timeout")
            {
                logger.LogWarning(
                    "Tool invocation did not complete normally: ToolName={ToolName}, Status={Status}, ErrorType={ErrorType}, ElapsedMs={ElapsedMs}",
                    commandName,
                    status,
                    errorType,
                    stopwatch.ElapsedMilliseconds);
            }
            else if (status == "error" && errorType != "critical_error")
            {
                logger.LogWarning(
                    "Tool invocation completed with handled error response: ToolName={ToolName}, Status={Status}, ErrorType={ErrorType}, ElapsedMs={ElapsedMs}",
                    commandName,
                    status,
                    errorType,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogError(
                    "Tool invocation failed: ToolName={ToolName}, Status={Status}, ErrorType={ErrorType}, ElapsedMs={ElapsedMs}",
                    commandName,
                    status,
                    errorType,
                    stopwatch.ElapsedMilliseconds);
            }

            _metrics?.ToolInvocationTotal.Add(1,
                new TagList {
                    { "tool_name", commandName },
                    { "status", status },
                    { "correlation_id", OperationContext.CorrelationId }
                });

            _metrics?.ToolExecutionDurationSeconds.Record(stopwatch.Elapsed.TotalSeconds,
                new TagList {
                    { "tool_name", commandName },
                    { "correlation_id", OperationContext.CorrelationId }
                });

            if (status == "error")
            {
                _metrics?.ToolExecutionErrorsTotal.Add(1,
                    new TagList {
                        { "tool_name", commandName },
                        { "error_type", errorType },
                        { "correlation_id", OperationContext.CorrelationId }
                    });
            }

            // Record usage metrics
            _metrics?.ToolUsageTotal.Add(1,
                new TagList {
                    { "tool_name", commandName },
                    { "correlation_id", OperationContext.CorrelationId }
                });
        }
    }

    private static FrameworkExecutionOptions ResolveFrameworkExecutionOptions(
        PowerShellParameterInfo[]? parameterInfos,
        object[]? parameterValues,
        string commandName,
        ILogger logger)
    {
        var allProperties = GetFrameworkParameterValue<bool?>(parameterInfos, parameterValues, AllPropertiesParameterName);
        var maxResults = GetFrameworkParameterValue<int?>(parameterInfos, parameterValues, MaxResultsParameterName);
        var requestedProperties = GetFrameworkParameterValue<string[]>(parameterInfos, parameterValues, RequestedPropertiesParameterName)
            ?.Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (maxResults.HasValue && maxResults.Value <= 0)
        {
            logger.LogWarning(
                "Ignoring non-positive _MaxResults value for command {CommandName}: {MaxResults}",
                commandName,
                maxResults.Value);
            maxResults = null;
        }

        return new FrameworkExecutionOptions(allProperties == true, maxResults, requestedProperties);
    }

    private static IReadOnlyList<string>? ResolveSelectedProperties(string commandName, FrameworkExecutionOptions options)
    {
        if (options.AllProperties)
        {
            return null;
        }

        if (options.RequestedProperties is { Length: > 0 })
        {
            return options.RequestedProperties;
        }

        if (_powerShellConfig?.FunctionOverrides.TryGetValue(commandName, out var functionOverride) == true
            && functionOverride.DefaultProperties != null)
        {
            return functionOverride.DefaultProperties;
        }

        var useDefaultDisplayProperties = (_powerShellConfig?.FunctionOverrides.TryGetValue(commandName, out functionOverride) == true
            ? functionOverride.UseDefaultDisplayProperties
            : null)
            ?? _powerShellConfig?.Performance.UseDefaultDisplayProperties
            ?? true;

        if (!useDefaultDisplayProperties)
        {
            return null;
        }

        // Best-effort discovery. Null means no property set exists, so fall back to all properties.
        return PropertySetDiscovery.DiscoverDefaultDisplayProperties(commandName);
    }

    private static T? GetFrameworkParameterValue<T>(PowerShellParameterInfo[]? parameterInfos, object[]? parameterValues, string parameterName)
    {
        if (parameterInfos == null || parameterValues == null)
        {
            return default;
        }

        var limit = Math.Min(parameterInfos.Length, parameterValues.Length);
        for (int i = 0; i < limit; i++)
        {
            if (!string.Equals(parameterInfos[i].Name, parameterName, StringComparison.Ordinal))
            {
                continue;
            }

            var value = parameterValues[i];
            if (value == null)
            {
                return default;
            }

            if (value is T typed)
            {
                return typed;
            }

            return (T?)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        }

        return default;
    }

    private sealed record FrameworkExecutionOptions(
        bool AllProperties,
        int? MaxResults,
        string[]? RequestedProperties);

    private static string FormatParameterSummary(PowerShellParameterInfo[]? parameterInfos, object[]? parameterValues)
    {
        if (parameterInfos == null || parameterValues == null || parameterInfos.Length == 0 || parameterValues.Length == 0)
        {
            return "(none)";
        }

        var items = new List<string>();
        var length = Math.Min(parameterInfos.Length, parameterValues.Length);

        for (int i = 0; i < length; i++)
        {
            var name = parameterInfos[i].Name;
            var value = parameterValues[i];

            if (IsSensitiveParameter(name))
            {
                items.Add($"{name}=***");
                continue;
            }

            var displayValue = value?.ToString() ?? "<null>";
            if (displayValue.Length > 120)
            {
                displayValue = displayValue.Substring(0, 120) + "...";
            }

            items.Add($"{name}={displayValue}");
        }

        return items.Count == 0 ? "(none)" : string.Join(", ", items);
    }

    private static bool IsSensitiveParameter(string parameterName)
    {
        return parameterName.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
               parameterName.Contains("key", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Common error handling for PowerShell operations
    /// </summary>
    private static bool HandlePowerShellErrors(System.Management.Automation.PowerShell ps, ILogger logger, string operationName)
    {
        if (ps.HadErrors)
        {
            var errors = ps.Streams.Error.ReadAll();
            var errorMessage = string.Join("; ", errors.Select(e => e.ToString()));
            logger.LogWarning($"Error {operationName}: {errorMessage}");
            ps.Commands.Clear();
            return true; // Has errors
        }
        return false; // No errors
    }

    /// <summary>
    /// Common method to invoke PowerShell commands with error handling (synchronous version)
    /// </summary>
    private static Collection<PSObject>? InvokePowerShellSafe(
        System.Management.Automation.PowerShell ps,
        ILogger logger,
        string operationName)
    {
        try
        {
            // Check if pipeline contains commands before invoking

            if (ps.Commands.Commands.Count == 0)
            {
                logger.LogWarning($"Cannot {operationName}: PowerShell pipeline contains no commands");
                return new Collection<PSObject>();
            }
            else
            {
                string firstCommand = ps.Commands.Commands[0].CommandText;
                // test to see if the first command is a valid powershell command

            }

            return ps.Invoke();
        }
        catch (CommandNotFoundException cmdEx)
        {
            logger.LogWarning($"Command not found during {operationName}: {cmdEx.Message}");
            ps.Commands.Clear();
            throw; // Re-throw to let the calling code handle this specific case
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to {OperationName}", operationName);
            ps.Commands.Clear();
            throw;
        }
    }

    /// <summary>
    /// Common method to invoke PowerShell commands with error handling
    /// </summary>
    private static async Task<Collection<PSObject>?> InvokePowerShellSafeAsync(
        System.Management.Automation.PowerShell ps,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if pipeline contains commands before invoking
            if (ps.Commands.Commands.Count == 0)
            {
                logger.LogWarning($"Cannot {operationName}: PowerShell pipeline contains no commands");
                return new Collection<PSObject>();
            }

            return await Task.Run(() => ps.Invoke(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to {operationName}: {ex.Message}");
            ps.Commands.Clear();
            return null;
        }
    }

    /// <summary>
    /// Common method to convert PowerShell results to JSON
    /// </summary>
    private static async Task<string?> ConvertToJson(
        System.Management.Automation.PowerShell ps,
        Collection<PSObject> results,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ps.Commands.Clear();

        try
        {
            var normalizedResults = PowerShellObjectSerializer.FlattenPSObjects(results.ToArray());
            var jsonOutput = JsonSerializer.Serialize(normalizedResults, PowerShellJsonOptions.Options);
            logger.LogInformation($"{operationName} completed: {jsonOutput.Length} characters");
            return jsonOutput;
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to serialize {operationName} to JSON: {ex.Message}");
            return null;
        }
    }
    /// <summary>
    /// Retrieves the cached output from the last executed PowerShell command
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON-serialized cached command output, or null if no cache exists</returns>
    public static async Task<string?> GetLastCommandOutput(
        IPowerShellRunspace runspace,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Retrieving cached output from last PowerShell command");

            return await runspace.ExecuteThreadSafeAsync<string?>(async ps =>
            {
                ps.Commands.Clear();

                // Check if the LastCommandOutput variable exists and has content
                ps.AddScript("if (Get-Variable -Name 'LastCommandOutput' -ErrorAction SilentlyContinue) { $LastCommandOutput } else { $null }");

                var results = await InvokePowerShellSafeAsync(ps, logger, "retrieve cached command output", cancellationToken);
                if (results == null) return null;

                // Handle errors
                if (HandlePowerShellErrors(ps, logger, "retrieving cached output"))
                    return null;

                // Convert results to JSON if there's cached data
                if (results.Count == 0 || results[0]?.BaseObject == null)
                {
                    logger.LogInformation("No cached command output available — result caching may be disabled");
                    ps.Commands.Clear();
                    return "{\"error\": \"Result caching is disabled for this session. Enable it via configuration or the set-result-caching tool to use this feature.\"}";
                }

                return await ConvertToJson(ps, results, logger, "cached output retrieval", cancellationToken);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving cached PowerShell command output");
            return null;
        }
    }

    /// <summary>
    /// Sorts the cached output from the last executed PowerShell command using Sort-Object
    /// </summary>
    /// <param name="runspace">The PowerShell runspace to query</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="property">Property name to sort by (optional)</param>
    /// <param name="descending">Whether to sort in descending order</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON-serialized sorted cached command output, or null if no cache exists</returns>
    public static async Task<string?> SortLastCommandOutput(
        IPowerShellRunspace runspace,
        ILogger logger,
        string? property = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation($"Sorting cached output from last PowerShell command{(property != null ? $" by property '{property}'" : "")}{(descending ? " (descending)" : "")}");

            return await runspace.ExecuteThreadSafeAsync<string?>(async ps =>
            {
                // Build a pipeline to sort with Sort-Object and convert to JSON
                ps.Commands.Clear();
                ps.AddCommand("Get-Variable")
                  .AddParameter("Name", "LastCommandOutput")
                  .AddParameter("ErrorAction", "SilentlyContinue");
                ps.AddCommand("Select-Object")
                  .AddParameter("ExpandProperty", "Value");
                ps.AddCommand("Sort-Object");

                // Add property parameter if specified
                if (!string.IsNullOrWhiteSpace(property))
                {
                    ps.AddParameter("Property", property);
                }

                // Add descending parameter if requested
                if (descending)
                {
                    ps.AddParameter("Descending");
                }

                // Execute the sorting pipeline
                var results = await InvokePowerShellSafeAsync(ps, logger, "sort cached command output", cancellationToken);
                if (results == null) return null;

                // Handle errors
                if (HandlePowerShellErrors(ps, logger, "sorting cached output"))
                    return null;

                // Convert results to JSON if there's data
                if (results.Count == 0)
                {
                    logger.LogInformation("No cached command output available for sorting — result caching may be disabled");
                    ps.Commands.Clear();
                    return "{\"error\": \"Result caching is disabled for this session. Enable it via configuration or the set-result-caching tool to use this feature.\"}";
                }

                return await ConvertToJson(ps, results, logger, "sorted cached output", cancellationToken);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sorting cached PowerShell command output");
            return null;
        }
    }

    /// <summary>
    /// Filters the cached output from the last executed PowerShell command using Where-Object
    /// </summary>
    /// <param name="runspace">PowerShell runspace</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="filterScript">PowerShell filter script block (e.g., "$_.Name -like 'dot*'")</param>
    /// <param name="updateCache">If true, stores the filtered results back to LastCommandOutput variable</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON-serialized filtered cached command output, or null if no cache exists</returns>
    public static async Task<string?> FilterLastCommandOutput(
        IPowerShellRunspace runspace,
        ILogger logger,
        string? filterScript = null,
        bool updateCache = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filterScript))
        {
            logger.LogWarning("Filter script is required for filtering cached output");
            return null;
        }

        try
        {
            logger.LogInformation($"Filtering cached output from last PowerShell command with script: {filterScript}{(updateCache ? " (updating cache)" : "")}");

            return await runspace.ExecuteThreadSafeAsync<string?>(async ps =>
            {
                // Build a pipeline to filter with Where-Object
                ps.Commands.Clear();
                ps.AddCommand("Get-Variable")
                  .AddParameter("Name", "LastCommandOutput")
                  .AddParameter("ErrorAction", "SilentlyContinue");
                ps.AddCommand("Select-Object")
                  .AddParameter("ExpandProperty", "Value");
                ps.AddCommand("Where-Object");

                // Add the filter script block
                try
                {
                    var scriptBlock = ScriptBlock.Create(filterScript);
                    ps.AddParameter("FilterScript", scriptBlock);
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Invalid filter script: {ex.Message}");
                    ps.Commands.Clear();
                    return null;
                }

                // If updateCache is true, add Tee-Object to store results back to cache
                if (updateCache)
                {
                    ps.AddCommand("Tee-Object")
                      .AddParameter("Variable", "LastCommandOutput");
                }

                // Execute the filtering pipeline
                var results = await InvokePowerShellSafeAsync(ps, logger, "filter cached command output", cancellationToken);
                if (results == null) return null;

                // Handle errors
                if (HandlePowerShellErrors(ps, logger, "filtering cached output"))
                    return null;

                // Convert results to JSON if there's data
                if (results.Count == 0)
                {
                    logger.LogInformation("No cached command output available for filtering — result caching may be disabled or filter matched nothing");
                    ps.Commands.Clear();
                    return "{\"error\": \"Result caching is disabled for this session. Enable it via configuration or the set-result-caching tool to use this feature.\"}";
                }

                return await ConvertToJson(ps, results, logger, "filtered cached output", cancellationToken);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error filtering cached PowerShell command output");
            return null;
        }
    }

    /// <summary>
    /// Groups the cached output from the last executed PowerShell command using Group-Object
    /// </summary>
    /// <param name="runspace">PowerShell runspace</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="property">Property name to group by (required)</param>
    /// <param name="noElement">If true, excludes the grouped objects from the result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON-serialized grouped cached command output, or null if no cache exists</returns>
    public static async Task<string?> GroupLastCommandOutput(
        IPowerShellRunspace runspace,
        ILogger logger,
        string? property = null,
        bool noElement = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(property))
        {
            logger.LogWarning("Property is required for grouping cached output");
            return null;
        }

        try
        {
            logger.LogInformation($"Grouping cached output from last PowerShell command by property '{property}'{(noElement ? " (no elements)" : "")}");

            return await runspace.ExecuteThreadSafeAsync<string?>(async ps =>
            {
                // Build a pipeline to group with Group-Object and convert to JSON
                ps.Commands.Clear();
                ps.AddCommand("Get-Variable")
                  .AddParameter("Name", "LastCommandOutput")
                  .AddParameter("ErrorAction", "SilentlyContinue");
                ps.AddCommand("Select-Object")
                  .AddParameter("ExpandProperty", "Value");
                ps.AddCommand("Group-Object");

                // Add property parameter
                ps.AddParameter("Property", property);

                // Add NoElement parameter if requested
                if (noElement)
                {
                    ps.AddParameter("NoElement");
                }

                // Execute the grouping pipeline
                var results = await InvokePowerShellSafeAsync(ps, logger, "group cached command output", cancellationToken);
                if (results == null) return null;

                // Handle errors
                if (HandlePowerShellErrors(ps, logger, "grouping cached output"))
                    return null;

                // Convert results to JSON if there's data
                if (results.Count == 0)
                {
                    logger.LogInformation("No cached command output available for grouping — result caching may be disabled");
                    ps.Commands.Clear();
                    return "{\"error\": \"Result caching is disabled for this session. Enable it via configuration or the set-result-caching tool to use this feature.\"}";
                }

                return await ConvertToJson(ps, results, logger, "grouped cached output", cancellationToken);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error grouping cached PowerShell command output");
            return null;
        }
    }

    private static string SanitizeParameterName(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return "param";
        }

        // Replace invalid characters with underscores
        var sanitized = System.Text.RegularExpressions.Regex.Replace(parameterName, @"[^\w]", "_");

        // Ensure it doesn't start with a digit
        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        // Ensure it's not empty after sanitization
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "param";
        }

        // Ensure it's not a C# keyword
        var keywords = new HashSet<string> { "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while" };

        if (keywords.Contains(sanitized.ToLower()))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    /// <summary>
    /// Generates a method to retrieve cached command output
    /// </summary>
    private static void GenerateGetLastCommandOutputMethod(TypeBuilder typeBuilder, FieldBuilder loggerField, FieldBuilder runspaceField)
    {
        var methodBuilder = typeBuilder.DefineMethod(
            "get_last_command_output",
            MethodAttributes.Public,
            typeof(Task<string>),
            new[] { typeof(CancellationToken) });

        // Set parameter name
        methodBuilder.DefineParameter(1, ParameterAttributes.None, "cancellationToken");

        // Generate method body
        var il = methodBuilder.GetILGenerator();

        // Get the GetLastCommandOutput method to call
        var getLastOutputMethod = typeof(PowerShellAssemblyGenerator).GetMethod(
            nameof(GetLastCommandOutput),
            BindingFlags.Static | BindingFlags.Public)!;

        // Call PowerShellAssemblyGenerator.GetLastCommandOutput
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, runspaceField); // runspace
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, loggerField); // logger
        il.Emit(OpCodes.Ldarg_1); // cancellationToken
        il.Emit(OpCodes.Call, getLastOutputMethod);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Generates a method to sort cached command output
    /// </summary>
    private static void GenerateSortLastCommandOutputMethod(TypeBuilder typeBuilder, FieldBuilder loggerField, FieldBuilder runspaceField)
    {
        var methodBuilder = typeBuilder.DefineMethod(
            "sort_last_command_output",
            MethodAttributes.Public,
            typeof(Task<string>),
            new[] { typeof(string), typeof(bool), typeof(CancellationToken) });

        // Set parameter names
        methodBuilder.DefineParameter(1, ParameterAttributes.None, "property");
        methodBuilder.DefineParameter(2, ParameterAttributes.None, "descending");
        methodBuilder.DefineParameter(3, ParameterAttributes.None, "cancellationToken");

        // Generate method body
        var il = methodBuilder.GetILGenerator();

        // Get the SortLastCommandOutput method to call
        var sortLastOutputMethod = typeof(PowerShellAssemblyGenerator).GetMethod(
            nameof(SortLastCommandOutput),
            BindingFlags.Static | BindingFlags.Public)!;

        // Call PowerShellAssemblyGenerator.SortLastCommandOutput
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, runspaceField); // runspace
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, loggerField); // logger
        il.Emit(OpCodes.Ldarg_1); // property
        il.Emit(OpCodes.Ldarg_2); // descending
        il.Emit(OpCodes.Ldarg_3); // cancellationToken
        il.Emit(OpCodes.Call, sortLastOutputMethod);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Generates a method to filter cached command output using Where-Object
    /// </summary>
    /// <summary>
    /// Generates a method to filter cached command output using Where-Object
    /// </summary>
    private static void GenerateFilterLastCommandOutputMethod(TypeBuilder typeBuilder, FieldBuilder loggerField, FieldBuilder runspaceField)
    {
        var methodBuilder = typeBuilder.DefineMethod(
            "filter_last_command_output",
            MethodAttributes.Public,
            typeof(Task<string>),
            new[] { typeof(string), typeof(bool), typeof(CancellationToken) });

        // Set parameter names with defaults for optional parameters
        methodBuilder.DefineParameter(1, ParameterAttributes.None, "filterScript");

        var updateCacheParam = methodBuilder.DefineParameter(2, ParameterAttributes.Optional | ParameterAttributes.HasDefault, "updateCache");
        updateCacheParam.SetConstant(false); // Default value for updateCache

        var cancellationTokenParam = methodBuilder.DefineParameter(3, ParameterAttributes.Optional | ParameterAttributes.HasDefault, "cancellationToken");

        // Generate method body
        var il = methodBuilder.GetILGenerator();

        // Get the FilterLastCommandOutput method to call
        var filterLastOutputMethod = typeof(PowerShellAssemblyGenerator).GetMethod(
            nameof(FilterLastCommandOutput),
            BindingFlags.Static | BindingFlags.Public)!;

        // Call PowerShellAssemblyGenerator.FilterLastCommandOutput
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, runspaceField); // runspace
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, loggerField); // logger
        il.Emit(OpCodes.Ldarg_1); // filterScript
        il.Emit(OpCodes.Ldarg_2); // updateCache
        il.Emit(OpCodes.Ldarg_3); // cancellationToken
        il.Emit(OpCodes.Call, filterLastOutputMethod);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Generates a method to group cached command output using Group-Object
    /// </summary>
    private static void GenerateGroupLastCommandOutputMethod(TypeBuilder typeBuilder, FieldBuilder loggerField, FieldBuilder runspaceField)
    {
        var methodBuilder = typeBuilder.DefineMethod(
            "group_last_command_output",
            MethodAttributes.Public,
            typeof(Task<string>),
            new[] { typeof(string), typeof(bool), typeof(CancellationToken) });

        // Set parameter names
        methodBuilder.DefineParameter(1, ParameterAttributes.None, "property");
        methodBuilder.DefineParameter(2, ParameterAttributes.None, "noElement");
        methodBuilder.DefineParameter(3, ParameterAttributes.None, "cancellationToken");

        // Generate method body
        var il = methodBuilder.GetILGenerator();

        // Get the GroupLastCommandOutput method to call
        var groupLastOutputMethod = typeof(PowerShellAssemblyGenerator).GetMethod(
            nameof(GroupLastCommandOutput),
            BindingFlags.Static | BindingFlags.Public)!;

        // Call PowerShellAssemblyGenerator.GroupLastCommandOutput
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, runspaceField); // runspace
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, loggerField); // logger
        il.Emit(OpCodes.Ldarg_1); // property
        il.Emit(OpCodes.Ldarg_2); // noElement
        il.Emit(OpCodes.Ldarg_3); // cancellationToken
        il.Emit(OpCodes.Call, groupLastOutputMethod);

        il.Emit(OpCodes.Ret);
    }
}
