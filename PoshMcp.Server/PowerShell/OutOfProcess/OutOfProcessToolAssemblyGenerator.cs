using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Generates a dynamic assembly of MCP tool methods that delegate execution
/// to the out-of-process command executor instead of the in-process runspace.
///
/// Mirrors PowerShellAssemblyGenerator but generates IL that calls
/// ICommandExecutor.InvokeAsync() instead of the in-process runspace.
/// </summary>
public class OutOfProcessToolAssemblyGenerator
{
    private const int FrameworkParameterCount = 3;

    private readonly ICommandExecutor _commandExecutor;
    private readonly object _lock = new object();
    private Type? _generatedType;
    private object? _generatedInstance;

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while"
    };

    public OutOfProcessToolAssemblyGenerator(ICommandExecutor commandExecutor)
    {
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
    }

    public void GenerateAssembly(IReadOnlyList<RemoteToolSchema> schemas, ILogger logger)
    {
        lock (_lock)
        {
            if (_generatedType != null)
            {
                logger.LogInformation("Returning cached generated OOP assembly");
                return;
            }

            logger.LogInformation("Generating OOP dynamic assembly for {Count} remote command schemas", schemas.Count);

            var assemblyName = new AssemblyName($"OutOfProcessCommands_{Guid.NewGuid():N}");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule("OutOfProcessCommandsModule");

            var typeBuilder = moduleBuilder.DefineType(
                "OutOfProcessPowerShellCommands",
                TypeAttributes.Public | TypeAttributes.Class);

            var executorField = typeBuilder.DefineField(
                "_executor",
                typeof(ICommandExecutor),
                FieldAttributes.Private | FieldAttributes.InitOnly);

            var loggerField = typeBuilder.DefineField(
                "_logger",
                typeof(ILogger),
                FieldAttributes.Private | FieldAttributes.InitOnly);

            GenerateConstructor(typeBuilder, loggerField, executorField);

            foreach (var schema in schemas)
            {
                try
                {
                    GenerateMethodForSchema(typeBuilder, schema, executorField);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to generate OOP method for {CommandName}: {Error}", schema.Name, ex.Message);
                }
            }

            _generatedType = typeBuilder.CreateType();
            logger.LogInformation("Generated OOP assembly with {Count} command methods", schemas.Count);
        }
    }

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

            _generatedInstance = Activator.CreateInstance(_generatedType, logger, _commandExecutor)!;
            return _generatedInstance;
        }
    }

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
                if (method.Name != ".ctor" && method.DeclaringType == _generatedType)
                {
                    methods[method.Name] = method;
                }
            }

            return methods;
        }
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            _generatedType = null;
            _generatedInstance = null;
        }
    }

    /// <summary>
    /// Static method called by generated IL to execute a remote command.
    /// Builds a parameter dictionary from the name/value arrays and delegates
    /// to ICommandExecutor.InvokeAsync.
    /// </summary>
    public static Task<string> ExecuteRemoteCommandAsync(
        string commandName,
        string[] parameterNames,
        object?[] parameterValues,
        ICommandExecutor executor,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?>();
        for (int i = 0; i < parameterNames.Length; i++)
        {
            // Skip framework parameters — they are server-side only
            if (parameterNames[i].StartsWith("_", StringComparison.Ordinal))
                continue;

            if (parameterValues[i] != null)
            {
                parameters[parameterNames[i]] = parameterValues[i];
            }
        }

        return executor.InvokeAsync(commandName, parameters, cancellationToken);
    }

    private static void GenerateConstructor(
        TypeBuilder typeBuilder,
        FieldBuilder loggerField,
        FieldBuilder executorField)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(ILogger), typeof(ICommandExecutor) });

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

        // _logger = arg1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, loggerField);

        // _executor = arg2
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, executorField);

        il.Emit(OpCodes.Ret);
    }

    private static Type ResolveParameterType(RemoteParameterSchema param)
    {
        var baseType = param.TypeName switch
        {
            "System.String" => typeof(string),
            "System.Int32" => typeof(int),
            "System.Int64" => typeof(long),
            "System.Boolean" => typeof(bool),
            "System.Double" => typeof(double),
            "System.Management.Automation.SwitchParameter" => typeof(bool),
            _ => typeof(string) // arrays, complex objects → string (JSON)
        };

        // Make value types nullable for non-mandatory parameters
        if (!param.IsMandatory && baseType.IsValueType)
        {
            return typeof(Nullable<>).MakeGenericType(baseType);
        }

        return baseType;
    }

    private static string SanitizeParameterName(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            return "param";

        var sanitized = Regex.Replace(parameterName, @"[^\w]", "_");

        if (char.IsDigit(sanitized[0]))
            sanitized = "_" + sanitized;

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "param";

        if (CSharpKeywords.Contains(sanitized))
            sanitized = "_" + sanitized;

        return sanitized;
    }

    private static void GenerateMethodForSchema(
        TypeBuilder typeBuilder,
        RemoteToolSchema schema,
        FieldBuilder executorField)
    {
        var methodName = PowerShellAssemblyGenerator.SanitizeMethodName(schema.Name, schema.ParameterSetName);

        // Build parameter lists: command params + framework params + CancellationToken
        var paramTypes = new List<Type>();
        var paramCSharpNames = new List<string>();
        // Original PowerShell names for the dictionary keys in the static helper
        var paramOriginalNames = new List<string>();
        var paramIsMandatory = new List<bool>();
        int commandParamCount = schema.Parameters.Count;

        foreach (var param in schema.Parameters)
        {
            paramTypes.Add(ResolveParameterType(param));
            paramCSharpNames.Add(SanitizeParameterName(param.Name));
            paramOriginalNames.Add(param.Name);
            paramIsMandatory.Add(param.IsMandatory);
        }

        // Framework parameters (underscore prefix = not passed to PowerShell)
        paramTypes.Add(typeof(bool?));
        paramCSharpNames.Add("_AllProperties");
        paramOriginalNames.Add("_AllProperties");
        paramIsMandatory.Add(false);

        paramTypes.Add(typeof(int?));
        paramCSharpNames.Add("_MaxResults");
        paramOriginalNames.Add("_MaxResults");
        paramIsMandatory.Add(false);

        paramTypes.Add(typeof(string[]));
        paramCSharpNames.Add("_RequestedProperties");
        paramOriginalNames.Add("_RequestedProperties");
        paramIsMandatory.Add(false);

        // CancellationToken (always last)
        paramTypes.Add(typeof(CancellationToken));
        paramCSharpNames.Add("cancellationToken");
        paramOriginalNames.Add("cancellationToken");
        paramIsMandatory.Add(false);

        // Define the method
        var methodBuilder = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            typeof(Task<string>),
            paramTypes.ToArray());

        // Set parameter names and default values
        for (int i = 0; i < paramCSharpNames.Count; i++)
        {
            if (!paramIsMandatory[i])
            {
                var paramAttributes = ParameterAttributes.Optional | ParameterAttributes.HasDefault;
                var paramBuilder = methodBuilder.DefineParameter(i + 1, paramAttributes, paramCSharpNames[i]);

                try
                {
                    var pType = paramTypes[i];
                    if (pType == typeof(CancellationToken))
                    {
                        // CancellationToken default handled by caller
                    }
                    else if (!pType.IsValueType || pType.IsArray)
                    {
                        paramBuilder.SetConstant(null);
                    }
                    else if (Nullable.GetUnderlyingType(pType) != null)
                    {
                        paramBuilder.SetConstant(null);
                    }
                    else if (pType == typeof(bool))
                    {
                        paramBuilder.SetConstant(false);
                    }
                    else if (pType == typeof(int))
                    {
                        paramBuilder.SetConstant(0);
                    }
                    else if (pType == typeof(long))
                    {
                        paramBuilder.SetConstant(0L);
                    }
                    else if (pType == typeof(double))
                    {
                        paramBuilder.SetConstant(0.0);
                    }
                }
                catch
                {
                    // If SetConstant fails, leave as optional without constant
                }
            }
            else
            {
                methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, paramCSharpNames[i]);
            }
        }

        // Generate method body
        GenerateMethodBody(methodBuilder, schema, paramTypes, paramOriginalNames, commandParamCount, executorField);
    }

    private static void GenerateMethodBody(
        MethodBuilder methodBuilder,
        RemoteToolSchema schema,
        List<Type> paramTypes,
        List<string> paramOriginalNames,
        int commandParamCount,
        FieldBuilder executorField)
    {
        var il = methodBuilder.GetILGenerator();

        var executeMethod = typeof(OutOfProcessToolAssemblyGenerator).GetMethod(
            nameof(ExecuteRemoteCommandAsync),
            BindingFlags.Static | BindingFlags.Public)!;

        // Number of entries in name/value arrays = command params + framework params
        int helperParamCount = commandParamCount + FrameworkParameterCount;

        // --- Create string[] parameterNames with original PowerShell names ---
        var namesLocal = il.DeclareLocal(typeof(string[]));
        il.Emit(OpCodes.Ldc_I4, helperParamCount);
        il.Emit(OpCodes.Newarr, typeof(string));
        il.Emit(OpCodes.Stloc, namesLocal);

        for (int i = 0; i < helperParamCount; i++)
        {
            il.Emit(OpCodes.Ldloc, namesLocal);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldstr, paramOriginalNames[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        // --- Create object[] parameterValues (boxed) ---
        var valuesLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Ldc_I4, helperParamCount);
        il.Emit(OpCodes.Newarr, typeof(object));
        il.Emit(OpCodes.Stloc, valuesLocal);

        for (int i = 0; i < helperParamCount; i++)
        {
            il.Emit(OpCodes.Ldloc, valuesLocal);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldarg, i + 1); // +1 because arg 0 is 'this'

            // Box value types for storage in object[]
            if (paramTypes[i].IsValueType)
            {
                il.Emit(OpCodes.Box, paramTypes[i]);
            }

            il.Emit(OpCodes.Stelem_Ref);
        }

        // Call ExecuteRemoteCommandAsync(commandName, names, values, executor, cancellationToken)
        il.Emit(OpCodes.Ldstr, schema.Name);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, executorField);
        // CancellationToken is the last method parameter: arg index = helperParamCount + 1
        il.Emit(OpCodes.Ldarg, helperParamCount + 1);
        il.Emit(OpCodes.Call, executeMethod);

        il.Emit(OpCodes.Ret);
    }
}
