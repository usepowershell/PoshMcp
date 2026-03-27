using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests for Azure Container App deployment with custom image layers.
/// Tests the complete flow: base image → custom image → Azure deployment.
/// </summary>
/// <remarks>
/// These tests require:
/// - Docker Desktop running
/// - Azure CLI authenticated (az login)
/// - Azure subscription with permissions to create resources
/// 
/// Configuration (choose one):
/// 
/// Option 1: .env file (recommended)
/// - Copy .env.example to .env.test
/// - Set AZURE_SUBSCRIPTION_ID and optional values
/// - Tests automatically load configuration
/// 
/// Option 2: Environment variables
/// - Set AZURE_SUBSCRIPTION_ID (required)
/// - Set AZURE_RESOURCE_GROUP (optional, will create if not set)
/// - Set AZURE_LOCATION (optional, default: eastus)
/// - Set AZURE_CONTAINER_REGISTRY (optional, will create if not set)
/// 
/// Priority: System environment > .env.test > .env.local > .env
/// </remarks>
public class AzureDeploymentIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _testId;
    private readonly string _baseImageName;
    private readonly string _customImageName;
    private readonly string _resourceGroupName;
    private readonly string _location;
    private readonly string _containerRegistryName;
    private readonly string _subscriptionId;
    private readonly bool _skipTests;

    public AzureDeploymentIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Load .env file if it exists (supports .env, .env.test, or .env.local)
        LoadEnvironmentFile();
        
        _testId = $"poshmcp-test-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        _baseImageName = $"poshmcp-base:{_testId}";
        _customImageName = $"poshmcp-azure:{_testId}";
        _location = Environment.GetEnvironmentVariable("AZURE_LOCATION") ?? "eastus";
        _subscriptionId = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID") ?? "";
        
        // Use existing resource group or create a test-specific one
        _resourceGroupName = Environment.GetEnvironmentVariable("AZURE_RESOURCE_GROUP") 
            ?? $"rg-{_testId}";
        
        // Use existing ACR or create a test-specific one
        _containerRegistryName = Environment.GetEnvironmentVariable("AZURE_CONTAINER_REGISTRY") 
            ?? $"acr{_testId.Replace("-", "")}".Substring(0, Math.Min(50, $"acr{_testId.Replace("-", "")}".Length)).ToLowerInvariant();

        // Skip tests if Azure credentials are not configured
        _skipTests = string.IsNullOrEmpty(_subscriptionId);
        
        if (_skipTests)
        {
            _output.WriteLine("⚠️  Skipping Azure deployment tests - AZURE_SUBSCRIPTION_ID not set");
        }
        else
        {
            _output.WriteLine($"🧪 Test Configuration:");
            _output.WriteLine($"   Subscription: {_subscriptionId}");
            _output.WriteLine($"   Resource Group: {_resourceGroupName}");
            _output.WriteLine($"   Location: {_location}");
            _output.WriteLine($"   ACR: {_containerRegistryName}");
            _output.WriteLine($"   Base Image: {_baseImageName}");
            _output.WriteLine($"   Custom Image: {_customImageName}");
        }
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_skipTests) return;

        // Cleanup: Remove local images
        _output.WriteLine("🧹 Cleaning up Docker images...");
        await RunCommandAsync("docker", $"rmi {_baseImageName} {_customImageName} --force", ignoreErrors: true);
        
        // Note: We don't auto-delete Azure resources to allow manual inspection
        // To cleanup Azure resources, run:
        _output.WriteLine($"💡 To cleanup Azure resources, run:");
        _output.WriteLine($"   az group delete --name {_resourceGroupName} --yes --no-wait");
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    [Trait("Speed", "Slow")]
    [Trait("Requires", "Docker")]
    public async Task BuildBaseImage_ShouldSucceed()
    {
        if (_skipTests)
        {
            _output.WriteLine("⏭️  Test skipped - Azure credentials not configured");
            return;
        }

        // Arrange
        var dockerfilePath = GetRepositoryRoot();
        _output.WriteLine($"📦 Building base image from: {dockerfilePath}");

        // Act
        var (exitCode, output, error) = await RunCommandAsync(
            "docker",
            $"build -t {_baseImageName} .",
            workingDirectory: dockerfilePath
        );

        // Assert
        _output.WriteLine($"📋 Build output:\n{output}");
        if (!string.IsNullOrEmpty(error))
        {
            _output.WriteLine($"⚠️  Build stderr:\n{error}");
        }

        Assert.Equal(0, exitCode);
        
        // Verify image exists
        var (verifyExit, verifyOutput, _) = await RunCommandAsync("docker", $"images {_baseImageName} --format json");
        Assert.Equal(0, verifyExit);
        Assert.Contains(_baseImageName.Split(':')[0], verifyOutput);
        
        _output.WriteLine($"✅ Base image built successfully");
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    [Trait("Category", "Azure")]
    [Trait("Speed", "Slow")]
    [Trait("Requires", "Docker")]
    [Trait("Requires", "BaseImage")]
    public async Task BuildCustomAzureImage_FromBaseImage_ShouldSucceed()
    {
        if (_skipTests)
        {
            _output.WriteLine("⏭️  Test skipped - Azure credentials not configured");
            return;
        }

        // Arrange - Ensure base image exists (build it first)
        await BuildBaseImage_ShouldSucceed();
        
        var repoRoot = GetRepositoryRoot();
        var dockerfilePath = Path.Combine(repoRoot, "examples", "Dockerfile.azure");
        _output.WriteLine($"📦 Building custom Azure image from: {dockerfilePath}");
        
        // Update the FROM line temporarily to use our test base image
        var dockerfileContent = await File.ReadAllTextAsync(dockerfilePath);
        var testDockerfileName = $"Dockerfile.azure.test.{_testId}";
        var testDockerfilePath = Path.Combine(repoRoot, testDockerfileName);
        var updatedContent = dockerfileContent.Replace("FROM poshmcp:latest", $"FROM {_baseImageName}");
        await File.WriteAllTextAsync(testDockerfilePath, updatedContent);

        try
        {
            // Act
            var (exitCode, output, error) = await RunCommandAsync(
                "docker",
                $"build -f {testDockerfileName} -t {_customImageName} .",
                workingDirectory: repoRoot
            );

            // Assert
            _output.WriteLine($"📋 Build output:\n{output}");
            if (!string.IsNullOrEmpty(error))
            {
                _output.WriteLine($"⚠️  Build stderr:\n{error}");
            }

            Assert.Equal(0, exitCode);
            
            // Verify image exists
            var (verifyExit, verifyOutput, _) = await RunCommandAsync("docker", $"images {_customImageName} --format json");
            Assert.Equal(0, verifyExit);
            Assert.Contains(_customImageName.Split(':')[0], verifyOutput);
            
            // Verify Az modules are installed
            var (inspectExit, inspectOutput, _) = await RunCommandAsync(
                "docker",
                $"run --rm {_customImageName} pwsh -NoProfile -Command \"Get-Module -ListAvailable Az.Accounts | Select-Object -ExpandProperty Name\""
            );
            Assert.Equal(0, inspectExit);
            Assert.Contains("Az.Accounts", inspectOutput);
            
            _output.WriteLine($"✅ Custom Azure image built successfully with Az modules");
        }
        finally
        {
            // Cleanup test Dockerfile
            if (File.Exists(testDockerfilePath))
            {
                File.Delete(testDockerfilePath);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Azure")]
    [Trait("Category", "Docker")]
    [Trait("Speed", "VerySlow")]
    [Trait("Cost", "Expensive")]
    [Trait("Requires", "Docker")]
    [Trait("Requires", "AzureCLI")]
    [Trait("Requires", "AzureCredentials")]
    public async Task DeployToAzure_CompleteFlow_ShouldSucceed()
    {
        if (_skipTests)
        {
            _output.WriteLine("⏭️  Test skipped - Azure credentials not configured");
            return;
        }

        // This test orchestrates the complete deployment flow:
        // 1. Build base image
        // 2. Build custom Azure image from base
        // 3. Create Azure Container Registry (if needed)
        // 4. Push custom image to ACR
        // 5. Deploy Container App Environment
        // 6. Deploy Container App with the custom image
        // 7. Verify deployment health

        try
        {
            // Step 1: Build base image
            _output.WriteLine("\n📦 Step 1: Building base image...");
            await BuildBaseImage_ShouldSucceed();

            // Step 2: Build custom Azure image
            _output.WriteLine("\n📦 Step 2: Building custom Azure image...");
            await BuildCustomAzureImage_FromBaseImage_ShouldSucceed();

            // Step 3: Ensure resource group exists
            _output.WriteLine($"\n🏗️  Step 3: Ensuring resource group '{_resourceGroupName}' exists...");
            var (rgExistsExit, _, _) = await RunCommandAsync(
                "az",
                $"group show --name {_resourceGroupName} --subscription {_subscriptionId}",
                ignoreErrors: true
            );

            if (rgExistsExit != 0)
            {
                _output.WriteLine($"   Creating resource group...");
                var (createRgExit, createRgOut, createRgErr) = await RunCommandAsync(
                    "az",
                    $"group create --name {_resourceGroupName} --location {_location} --subscription {_subscriptionId}"
                );
                Assert.Equal(0, createRgExit);
                _output.WriteLine($"   ✅ Resource group created");
            }
            else
            {
                _output.WriteLine($"   ✅ Resource group already exists");
            }

            // Step 4: Create Azure Container Registry
            _output.WriteLine($"\n🐳 Step 4: Creating Azure Container Registry '{_containerRegistryName}'...");
            var (acrCreateExit, acrCreateOut, _) = await RunCommandAsync(
                "az",
                $"acr create --resource-group {_resourceGroupName} --name {_containerRegistryName} --sku Basic --admin-enabled true --subscription {_subscriptionId}",
                ignoreErrors: true
            );

            if (acrCreateExit != 0)
            {
                _output.WriteLine($"   ⚠️  ACR may already exist or creation failed, continuing...");
            }
            else
            {
                _output.WriteLine($"   ✅ ACR created");
            }

            // Get ACR login server
            var (acrShowExit, acrShowOut, _) = await RunCommandAsync(
                "az",
                $"acr show --name {_containerRegistryName} --resource-group {_resourceGroupName} --query loginServer -o tsv --subscription {_subscriptionId}"
            );
            Assert.Equal(0, acrShowExit);
            var acrLoginServer = acrShowOut.Trim();
            _output.WriteLine($"   ACR Login Server: {acrLoginServer}");

            // Step 5: Push image to ACR
            _output.WriteLine($"\n⬆️  Step 5: Pushing image to ACR...");
            
            // Tag image for ACR
            var acrImageName = $"{acrLoginServer}/poshmcp-azure:{_testId}";
            var (tagExit, _, _) = await RunCommandAsync("docker", $"tag {_customImageName} {acrImageName}");
            Assert.Equal(0, tagExit);
            _output.WriteLine($"   Tagged: {acrImageName}");

            // Login to ACR
            var (loginExit, _, _) = await RunCommandAsync("az", $"acr login --name {_containerRegistryName} --subscription {_subscriptionId}");
            Assert.Equal(0, loginExit);

            // Push image
            var (pushExit, pushOut, pushErr) = await RunCommandAsync("docker", $"push {acrImageName}");
            Assert.Equal(0, pushExit);
            _output.WriteLine($"   ✅ Image pushed to ACR");

            // Step 6: Deploy with Bicep
            _output.WriteLine($"\n🚀 Step 6: Deploying to Azure Container Apps...");
            var repoRoot = GetRepositoryRoot();
            var bicepPath = Path.Combine(repoRoot, "infrastructure", "azure", "main.bicep");
            
            var deploymentName = $"deploy-{_testId}";
            var containerAppName = $"app-{_testId}";
            var environmentName = $"env-{_testId}";
            var location = Environment.GetEnvironmentVariable("AZURE_LOCATION") ?? "eastus";

            var (deployExit, deployOut, deployErr) = await RunCommandAsync(
                "az",
                $"deployment sub create " +
                $"--name {deploymentName} " +
                $"--location {location} " +
                $"--template-file \"{bicepPath}\" " +
                $"--parameters resourceGroupName={_resourceGroupName} " +
                $"--parameters location={location} " +
                $"--parameters containerAppName={containerAppName} " +
                $"--parameters environmentName={environmentName} " +
                $"--parameters containerImage={acrImageName} " +
                $"--parameters containerRegistryServer={acrLoginServer} " +
                $"--subscription {_subscriptionId}",
                workingDirectory: repoRoot
            );

            _output.WriteLine($"📋 Deployment output:\n{deployOut}");
            if (!string.IsNullOrEmpty(deployErr))
            {
                _output.WriteLine($"⚠️  Deployment stderr:\n{deployErr}");
            }

            Assert.Equal(0, deployExit);
            _output.WriteLine($"   ✅ Container App deployed");

            // Step 7: Verify deployment
            _output.WriteLine($"\n✅ Step 7: Verifying deployment...");
            var (healthExit, healthOut, _) = await RunCommandAsync(
                "az",
                $"containerapp show --name {containerAppName} --resource-group {_resourceGroupName} --query properties.runningStatus -o tsv --subscription {_subscriptionId}"
            );
            
            Assert.Equal(0, healthExit);
            var runningStatus = healthOut.Trim();
            _output.WriteLine($"   Container App Status: {runningStatus}");
            
            // Get the FQDN
            var (fqdnExit, fqdnOut, _) = await RunCommandAsync(
                "az",
                $"containerapp show --name {containerAppName} --resource-group {_resourceGroupName} --query properties.configuration.ingress.fqdn -o tsv --subscription {_subscriptionId}"
            );

            if (fqdnExit == 0 && !string.IsNullOrEmpty(fqdnOut.Trim()))
            {
                var fqdn = fqdnOut.Trim();
                _output.WriteLine($"   📍 Application URL: https://{fqdn}");
                _output.WriteLine($"   💡 Test with: curl https://{fqdn}/health");
            }

            _output.WriteLine($"\n✅✅✅ Complete integration test passed!");
            _output.WriteLine($"\n📋 Summary:");
            _output.WriteLine($"   ✅ Base image built: {_baseImageName}");
            _output.WriteLine($"   ✅ Custom Azure image built: {_customImageName}");
            _output.WriteLine($"   ✅ Image pushed to ACR: {acrImageName}");
            _output.WriteLine($"   ✅ Container App deployed: {containerAppName}");
            _output.WriteLine($"   ✅ Container App status: {runningStatus}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ Integration test failed: {ex.Message}");
            _output.WriteLine($"   Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private string GetRepositoryRoot()
    {
        // Navigate up from the test project directory to repo root
        var currentDir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(currentDir, "PoshMcp.sln")))
        {
            var parent = Directory.GetParent(currentDir);
            if (parent == null)
            {
                throw new InvalidOperationException("Could not find repository root (PoshMcp.sln)");
            }
            currentDir = parent.FullName;
        }
        return currentDir;
    }

    private async Task<(int exitCode, string output, string error)> RunCommandAsync(
        string command,
        string arguments,
        string? workingDirectory = null,
        bool ignoreErrors = false)
    {
        _output.WriteLine($"🔧 Running: {command} {arguments}");
        
        var processStartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = processStartInfo };
        
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync();

        var exitCode = process.ExitCode;
        var output = outputBuilder.ToString();
        var error = errorBuilder.ToString();

        if (exitCode != 0 && !ignoreErrors)
        {
            _output.WriteLine($"❌ Command failed with exit code {exitCode}");
            _output.WriteLine($"   Output: {output}");
            _output.WriteLine($"   Error: {error}");
        }

        return (exitCode, output, error);
    }

    private void LoadEnvironmentFile()
    {
        // Look for .env files in priority order: .env.test, .env.local, .env
        var repoRoot = GetRepositoryRootForEnv();
        var envFiles = new[] { ".env.test", ".env.local", ".env" };
        
        foreach (var envFileName in envFiles)
        {
            var envFilePath = Path.Combine(repoRoot, envFileName);
            if (File.Exists(envFilePath))
            {
                _output.WriteLine($"📄 Loading environment from: {envFileName}");
                
                try
                {
                    var lines = File.ReadAllLines(envFilePath);
                    foreach (var line in lines)
                    {
                        // Skip empty lines and comments
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                            continue;
                        
                        // Parse KEY=VALUE format
                        var parts = trimmed.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var value = parts[1].Trim();
                            
                            // Remove surrounding quotes if present
                            if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                                (value.StartsWith("'") && value.EndsWith("'")))
                            {
                                value = value.Substring(1, value.Length - 2);
                            }
                            
                            // Only set if not already set (environment variables take precedence)
                            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                            {
                                Environment.SetEnvironmentVariable(key, value);
                                _output.WriteLine($"   ✓ Set {key}");
                            }
                            else
                            {
                                _output.WriteLine($"   ⊘ Skipped {key} (already set)");
                            }
                        }
                    }
                    
                    _output.WriteLine($"   ✅ Loaded environment from {envFileName}");
                    return; // Stop after first file found
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"   ⚠️  Failed to load {envFileName}: {ex.Message}");
                }
            }
        }
        
        _output.WriteLine("ℹ️  No .env file found - using system environment variables only");
    }

    private string GetRepositoryRootForEnv()
    {
        // Start from current directory and walk up to find repo root
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null)
        {
            if (File.Exists(Path.Combine(currentDir, "PoshMcp.sln")))
            {
                return currentDir;
            }
            
            var parent = Directory.GetParent(currentDir);
            currentDir = parent?.FullName;
        }
        
        // Fallback to current directory
        return Directory.GetCurrentDirectory();
    }
}
