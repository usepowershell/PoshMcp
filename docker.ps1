<#
.SYNOPSIS
    Build and run PoshMcp Server in Docker using two-tier architecture.

.DESCRIPTION
    This script manages Docker images for PoshMcp following a two-tier architecture:
    
    1. Base image (poshmcp:latest) - Core MCP server without customizations
    2. Custom images - User-specific layers with modules, config, startup scripts
    
    Commands:
    - build / build-base: Build the base PoshMcp image
    - build-custom: Build a custom image from the base using templates
    - run: Run containers using docker-compose
    - stop: Stop running containers
    - logs: Show container logs
    - clean: Remove containers and images

.PARAMETER Command
    The Docker operation to perform.
    Valid values: build, build-base, build-custom, run, stop, logs, clean

.PARAMETER Mode
    Run mode for containers (used with 'run' command).
    Valid values: web, http (HTTP server), stdio (stdio MCP server)
    Default: web

.PARAMETER Template
    Template to use when building custom images (used with 'build-custom' command).
    Valid values:
    - user: Basic customization with Pester and PSScriptAnalyzer
    - azure: Azure-enabled with Az modules and Managed Identity
    - custom: Advanced multi-stage build pattern
    Default: user

.PARAMETER Tag
    Custom tag for the Docker image. If not specified, uses 'latest' for base
    images or the template name for custom images.

.EXAMPLE
    .\docker.ps1 build
    Build the base PoshMcp image (poshmcp:latest)

.EXAMPLE
    .\docker.ps1 build-custom -Template azure
    Build an Azure-enabled custom image with Az modules pre-installed

.EXAMPLE
    .\docker.ps1 run web
    Run the base image as an HTTP web server on port 8080

.EXAMPLE
    .\docker.ps1 build-custom -Template user -Tag dev
    Build a custom image using the 'user' template with tag 'poshmcp-dev:latest'

.EXAMPLE
    Get-Help .\docker.ps1 -Detailed
    Show detailed help with examples

.NOTES
    Two-Tier Architecture Flow:
    1. Build base:    .\docker.ps1 build-base
    2. Build custom:  .\docker.ps1 build-custom -Template azure
    3. Result:        poshmcp:latest → poshmcp-azure:latest
    
    For more information:
    - Base Dockerfile:      ./Dockerfile
    - Example Dockerfiles:  ./examples/Dockerfile.*
    - Module installation:  ./install-modules.ps1
    - Integration tests:    ./docs/QUICKSTART-AZURE-INTEGRATION-TEST.md

.LINK
    https://github.com/yourusername/poshmcp
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('build', 'build-base', 'build-custom', 'run', 'stop', 'logs', 'clean')]
    [string]$Command,
    
    [Parameter(Position = 1)]
    [string]$Mode = 'web',
    
    [Parameter()]
    [ValidateSet('user', 'azure', 'custom')]
    [string]$Template = 'user',
    
    [Parameter()]
    [string]$Tag
)

# Enable Information stream for interactive use
$InformationPreference = 'Continue'

$BaseImageName = "poshmcp"
$BaseImageTag = $Tag ?? "latest"

switch ($Command) {
    { $_ -in 'build', 'build-base' } {
        Write-Information "Building base Docker image..." -Tags 'Status'
        Write-Information "This image contains the core PoshMcp server without customizations" -Tags 'Status'
        
        $imageTag = "${BaseImageName}:${BaseImageTag}"
        
        Write-Information "Running: docker build -t $imageTag ." -Tags 'Status'
        docker build -t $imageTag .
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "✅ Base Docker image built successfully: $imageTag" -ForegroundColor Green
            Write-Host ""
            Write-Host "📦 Next Steps:" -ForegroundColor Cyan
            Write-Host "   1. Build a custom image:" -ForegroundColor White
            Write-Host "      .\docker.ps1 build-custom -Template user    # Basic customization" -ForegroundColor Gray
            Write-Host "      .\docker.ps1 build-custom -Template azure   # Azure-enabled" -ForegroundColor Gray
            Write-Host ""
            Write-Host "   2. Or run the base image directly:" -ForegroundColor White
            Write-Host "      .\docker.ps1 run web" -ForegroundColor Gray
            Write-Host ""
            Write-Host "   3. See examples:" -ForegroundColor White
            Write-Host "      ls examples\Dockerfile.*" -ForegroundColor Gray
        } else {
            Write-Host ""
            Write-Host "❌ Docker build failed" -ForegroundColor Red
            exit 1
        }
    }
    
    'build-custom' {
        # Ensure base image exists
        $baseImageCheck = docker images "${BaseImageName}:latest" --format "{{.Repository}}"
        if (-not $baseImageCheck) {
            Write-Warning "Base image '${BaseImageName}:latest' not found. Building it first..."
            docker build -t "${BaseImageName}:latest" .
            if ($LASTEXITCODE -ne 0) {
                Write-Host "❌ Failed to build base image" -ForegroundColor Red
                exit 1
            }
        }
        
        Write-Information "Building custom Docker image from template: $Template" -Tags 'Status'
        
        $dockerfilePath = "examples\Dockerfile.$Template"
        if (-not (Test-Path $dockerfilePath)) {
            Write-Host "❌ Dockerfile not found: $dockerfilePath" -ForegroundColor Red
            Write-Host "   Available templates: user, azure, custom" -ForegroundColor Yellow
            exit 1
        }
        
        $customTag = $Tag ?? $Template
        $customImageTag = "${BaseImageName}-${customTag}:latest"
        
        Write-Information "Using Dockerfile: $dockerfilePath" -Tags 'Status'
        Write-Information "Running: docker build -f $dockerfilePath -t $customImageTag ." -Tags 'Status'
        docker build -f $dockerfilePath -t $customImageTag .
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "✅ Custom Docker image built successfully: $customImageTag" -ForegroundColor Green
            Write-Host ""
            Write-Host "📋 Image Details:" -ForegroundColor Cyan
            Write-Host "   Base image:   ${BaseImageName}:latest" -ForegroundColor Gray
            Write-Host "   Custom image: $customImageTag" -ForegroundColor Gray
            Write-Host "   Template:     $Template" -ForegroundColor Gray
            Write-Host ""
            Write-Host "🚀 Run the custom image:" -ForegroundColor Cyan
            Write-Host "   docker run -d -p 8080:8080 -e POSHMCP_TRANSPORT=http $customImageTag" -ForegroundColor White
            Write-Host ""
            Write-Host "🔍 Inspect modules installed:" -ForegroundColor Cyan
            Write-Host "   docker run --rm $customImageTag pwsh -Command 'Get-Module -ListAvailable'" -ForegroundColor White
            
            if ($Template -eq 'azure') {
                Write-Host ""
                Write-Host "☁️  Azure Template Notes:" -ForegroundColor Cyan
                Write-Host "   - Az modules are pre-installed" -ForegroundColor Gray
                Write-Host "   - Managed Identity startup script included" -ForegroundColor Gray
                Write-Host "   - Set env vars: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID" -ForegroundColor Gray
            }
        } else {
            Write-Host ""
            Write-Host "❌ Custom Docker build failed" -ForegroundColor Red
            exit 1
        }
    }
    
    'run' {
        Write-Information "Note: Running base image. To run a custom image, use docker run directly." -Tags 'Status'
        
        switch ($Mode) {
            { $_ -in 'web', 'http' } {
                Write-Information "Starting PoshMcp Web Server..." -Tags 'Status'
                Write-Verbose "Mode: $Mode"
                docker-compose --profile web up -d
                
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "✅ PoshMcp Web Server is running at http://localhost:8080" -ForegroundColor Green
                    Write-Host "💡 Use '.\docker.ps1 logs' to view logs" -ForegroundColor Cyan
                    Write-Host "💡 Use '.\docker.ps1 stop' to stop the server" -ForegroundColor Cyan
                }
            }
            
            { $_ -in 'stdio', 'server' } {
                Write-Information "Starting PoshMcp stdio Server..." -Tags 'Status'
                Write-Verbose "Mode: $Mode"
                docker-compose --profile stdio up -d
                
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "✅ PoshMcp stdio Server is running" -ForegroundColor Green
                    Write-Host "💡 Use '.\docker.ps1 logs' to view logs" -ForegroundColor Cyan
                    Write-Host "💡 Use '.\docker.ps1 stop' to stop the server" -ForegroundColor Cyan
                    Write-Host "💡 Connect via stdio to communicate with the MCP server" -ForegroundColor Cyan
                }
            }
            
            default {
                Write-Host "❌ Unknown mode: $Mode" -ForegroundColor Red
                Write-Host "Valid modes: web, http, stdio, server" -ForegroundColor Yellow
                Write-Host "Use 'Get-Help .\docker.ps1 -Examples' for usage examples" -ForegroundColor Cyan
                exit 1
            }
        }
    }
    
    'stop' {
        Write-Information "Stopping PoshMcp Servers..." -Tags 'Status'
        docker-compose --profile web --profile stdio down
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ PoshMcp Servers stopped" -ForegroundColor Green
        }
    }
    
    'logs' {
        Write-Information "Showing container logs (Ctrl+C to exit)..." -Tags 'Status'
        docker-compose --profile web --profile stdio logs -f
    }
    
    'clean' {
        Write-Information "Cleaning up containers and images..." -Tags 'Status'
        Write-Verbose "Removing all containers, images, volumes and orphans"
        docker-compose --profile web --profile stdio down --rmi all --volumes --remove-orphans
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Cleanup complete" -ForegroundColor Green
        }
    }
    
    default {
        Write-Host "❌ Unknown command: $Command" -ForegroundColor Red
        Write-Host "Valid commands: build, build-base, build-custom, run, stop, logs, clean" -ForegroundColor Yellow
        Write-Host "Use 'Get-Help .\docker.ps1 -Examples' for usage examples" -ForegroundColor Cyan
        exit 1
    }
}
