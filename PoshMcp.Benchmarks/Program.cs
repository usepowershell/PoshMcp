using BenchmarkDotNet.Running;
using PoshMcp.Benchmarks;

// BenchmarkSwitcher discovers all [Benchmark]-annotated types in this assembly.
// Run with:
//   dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter *
// Or filter to a specific scenario:
//   dotnet run --project PoshMcp.Benchmarks --configuration Release -- --filter *WarmInvoke*

// Disable spec-008 Application Insights / OpenTelemetry export during benchmark
// runs so latency numbers reflect executor cost, not telemetry overhead.
// The PoshMcp.Server defaults already have ApplicationInsights:Enabled = false,
// but we set the environment variable explicitly here in case a host inherits
// a configured value from the process environment.
Environment.SetEnvironmentVariable("ApplicationInsights__Enabled", "false");
Environment.SetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING", string.Empty);

BenchmarkSwitcher
    .FromAssembly(typeof(BenchmarkConfig).Assembly)
    .Run(args, new BenchmarkConfig());
