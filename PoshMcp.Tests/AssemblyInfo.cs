using Xunit;

// Many tests create PowerShell runspaces and child server processes.
// Running tests in parallel can cause nondeterministic host aborts.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
