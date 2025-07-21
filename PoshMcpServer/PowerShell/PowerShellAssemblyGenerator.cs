using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PoshMcp.PowerShell;

/// <summary>
/// Instance-based PowerShell dynamic assembly generator for better test isolation
/// </summary>
public class PowerShellAssemblyGenerator
{
    private readonly IPowerShellRunspace _powerShellRunspace;
    private Assembly? _generatedAssembly;
    private Type? _generatedType;
    private object? _generatedInstance;
    private readonly object _lock = new object();

    public PowerShellAssemblyGenerator(IPowerShellRunspace powerShellRunspace)
    {
        _powerShellRunspace = powerShellRunspace ?? throw new ArgumentNullException(nameof(powerShellRunspace));
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
                logger.LogInformation("Returning cached generated assembly");
                return _generatedAssembly;
            }

            logger.LogInformation("Generating new in-memory assembly for PowerShell commands");

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

            logger.LogInformation($"Successfully generated assembly with {commandList.Count} command methods");
            return _generatedAssembly;
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
        var methodName = PowerShellDynamicAssemblyGenerator.SanitizeMethodName(commandInfo.Name, parameterSet.Name);

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

        // Add CancellationToken parameter
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
        il.Emit(OpCodes.Ldc_I4, parameters.Count);
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
        il.Emit(OpCodes.Ldc_I4, parameters.Count);
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

        // Call ExecutePowerShellCommandTyped
        il.Emit(OpCodes.Ldstr, commandInfo.Name); // command name
        il.Emit(OpCodes.Ldloc, parameterInfoArrayLocal); // parameter info
        il.Emit(OpCodes.Ldloc, parametersLocal); // parameter values
        il.Emit(OpCodes.Ldarg, parameters.Count + 1); // CancellationToken (last parameter)
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, runspaceField); // runspace
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, loggerField); // logger
        il.Emit(OpCodes.Call, executeMethod);

        il.Emit(OpCodes.Ret);
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
        try
        {
            logger.LogInformation($"Executing PowerShell command: {commandName} with {parameterValues?.Length ?? 0} parameters");

            // Log parameter details
            if (parameterInfos != null && parameterValues != null)
            {
                for (int i = 0; i < parameterInfos.Length && i < parameterValues.Length; i++)
                {
                    var paramInfo = parameterInfos[i];
                    var paramValue = parameterValues[i];
                    logger.LogDebug($"Parameter {i}: {paramInfo.Name} = {paramValue} (Type: {paramInfo.Type.Name})");
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

                    // Add parameters
                    for (int i = 0; i < (parameterInfos?.Length ?? 0) && i < (parameterValues?.Length ?? 0); i++)
                    {
                        var paramInfo = parameterInfos![i];
                        var paramValue = parameterValues![i];

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
                                    logger.LogDebug($"Added switch parameter: {paramInfo.Name}");
                                }
                            }
                            else
                            {
                                ps.AddParameter(paramInfo.Name, convertedValue);
                                logger.LogDebug($"Added parameter {paramInfo.Name} ({convertedValue?.GetType().Name}): {convertedValue}");
                            }
                        }
                        else if (paramInfo.IsMandatory)
                        {
                            throw new ArgumentException($"Mandatory parameter '{paramInfo.Name}' cannot be null");
                        }
                    }

                    // Execute the command and pipe to Tee-Object to cache results, then continue to ConvertTo-Json
                    // This caches the output in $LastCommandOutput variable for later retrieval
                    ps.AddCommand("Tee-Object")
                      .AddParameter("Variable", "LastCommandOutput");

                    Collection<PSObject> results;
                    try
                    {
                        results = ps.Invoke();
                    }
                    catch (CommandNotFoundException cmdEx)
                    {
                        logger.LogWarning($"PowerShell command {commandName} not found: {cmdEx.Message}");
                        ps.Commands.Clear();
                        return Task.FromResult($"{{\"error\": \"The term '{commandName}' is not recognized as a name of a cmdlet, function, script file, or executable program.\"}}");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"PowerShell command {commandName} execution failed: {ex.Message}");
                        ps.Commands.Clear();
                        return Task.FromResult($"{{\"error\": \"Command execution failed: {ex.Message}\"}}");
                    }

                    // Handle errors
                    if (ps.HadErrors)
                    {
                        var errors = ps.Streams.Error.ReadAll();
                        var errorMessage = string.Join("; ", errors.Select(e => e.ToString()));
                        logger.LogWarning($"PowerShell command {commandName} had errors: {errorMessage}");
                        ps.Commands.Clear();
                        return Task.FromResult($"{{\"error\": \"Command completed with errors: {errorMessage}\"}}");
                    }

                    // Convert results to JSON using PowerShell's ConvertTo-Json
                    if (results.Count == 0)
                    {
                        ps.Commands.Clear();
                        return Task.FromResult("[]");
                    }

                    // Use PowerShell's ConvertTo-Json to serialize the results
                    ps.Commands.Clear();
                    ps.AddCommand("ConvertTo-Json")
                      .AddParameter("InputObject", results.ToArray())
                      .AddParameter("Depth", 10) // Handle nested objects up to 10 levels deep
                      .AddParameter("Compress", true); // Compact JSON output

                    Collection<PSObject> jsonResults;
                    try
                    {
                        jsonResults = ps.Invoke();
                        ps.Commands.Clear();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning($"Failed to convert PowerShell results to JSON: {ex.Message}");
                        ps.Commands.Clear();
                        return Task.FromResult($"{{\"error\": \"Failed to serialize results to JSON: {ex.Message}\"}}");
                    }

                    if (jsonResults.Count > 0)
                    {
                        var jsonOutput = jsonResults[0]?.ToString() ?? "null";
                        logger.LogInformation($"Command {commandName} completed successfully, returned JSON: {jsonOutput.Length} characters");
                        return Task.FromResult(jsonOutput);
                    }
                    else
                    {
                        return Task.FromResult("null");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Unexpected error in PowerShell operation: {ex.Message}");
                    ps.Commands.Clear();
                    return Task.FromResult($"{{\"error\": \"Unexpected error: {ex.Message}\"}}");
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Error executing PowerShell command {commandName}");
            throw new InvalidOperationException($"Error executing {commandName}: {ex.Message}", ex);
        }
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
    /// Common method to invoke PowerShell commands with error handling
    /// </summary>
    private static async Task<Collection<PSObject>?> InvokePowerShellSafe(
        System.Management.Automation.PowerShell ps,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        try
        {
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
        ps.AddCommand("ConvertTo-Json")
          .AddParameter("InputObject", results.ToArray())
          .AddParameter("Depth", 10)
          .AddParameter("Compress", true);

        var jsonResults = await InvokePowerShellSafe(ps, logger, $"convert {operationName} to JSON", cancellationToken);
        if (jsonResults == null) return null;

        if (jsonResults.Count > 0)
        {
            var jsonOutput = jsonResults[0]?.ToString();
            logger.LogInformation($"{operationName} completed: {jsonOutput?.Length ?? 0} characters");
            return jsonOutput;
        }

        return "[]";
    }
    /// <summary>
    /// Retrieves the cached output from the last executed PowerShell command
    /// </summary>
    /// <param name="runspace">The PowerShell runspace to query</param>
    /// <param name="logger">Logger instance</param>
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

                var results = await InvokePowerShellSafe(ps, logger, "retrieve cached command output", cancellationToken);
                if (results == null) return null;

                // Handle errors
                if (HandlePowerShellErrors(ps, logger, "retrieving cached output"))
                    return null;

                // Convert results to JSON if there's cached data
                if (results.Count == 0 || results[0]?.BaseObject == null)
                {
                    logger.LogInformation("No cached command output available");
                    ps.Commands.Clear();
                    return null;
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
                var results = await InvokePowerShellSafe(ps, logger, "sort cached command output", cancellationToken);
                if (results == null) return null;

                // Handle errors
                if (HandlePowerShellErrors(ps, logger, "sorting cached output"))
                    return null;

                // Convert results to JSON if there's data
                if (results.Count == 0)
                {
                    logger.LogInformation("No cached command output available for sorting");
                    ps.Commands.Clear();
                    return "[]";
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
                var results = await InvokePowerShellSafe(ps, logger, "filter cached command output", cancellationToken);
                if (results == null) return null;

                // Handle errors
                if (HandlePowerShellErrors(ps, logger, "filtering cached output"))
                    return null;

                // Convert results to JSON if there's data
                if (results.Count == 0)
                {
                    logger.LogInformation("No cached command output available for filtering or filter returned no results");
                    ps.Commands.Clear();
                    return "[]";
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
                var results = await InvokePowerShellSafe(ps, logger, "group cached command output", cancellationToken);
                if (results == null) return null;

                // Handle errors
                if (HandlePowerShellErrors(ps, logger, "grouping cached output"))
                    return null;

                // Convert results to JSON if there's data
                if (results.Count == 0)
                {
                    logger.LogInformation("No cached command output available for grouping");
                    ps.Commands.Clear();
                    return "[]";
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
