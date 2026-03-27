# Build and run PoshMcp Server in Docker (Web or stdio mode)
# PowerShell version for Windows users

[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet('build', 'run', 'stop', 'logs', 'clean')]
    [string]$Command,
    
    [Parameter(Position = 1)]
    [string]$Mode = 'web',
    
    [Parameter()]
    [string]$Modules = $env:INSTALL_PS_MODULES,
    
    [Parameter()]
    [ValidateSet('AllUsers', 'CurrentUser')]
    [string]$Scope = $env:MODULE_INSTALL_SCOPE ?? 'AllUsers',
    
    [Parameter()]
    [switch]$Help
)

# Enable Information stream for interactive use
$InformationPreference = 'Continue'

function Show-Usage {
    Write-Host @"
Usage: .\docker.ps1 [build|run|stop|logs|clean] [mode|options]

Commands:
  build [options]        - Build the Docker image
  run [mode]             - Run the container using docker-compose
  stop                   - Stop the running container
  logs                   - Show container logs
  clean                  - Remove container and image

Build Options:
  -Modules "module1 module2"     - Pre-install PowerShell modules at build time
  -Scope [AllUsers|CurrentUser]  - Module installation scope (default: AllUsers)

Modes (for run command):
  web|http    - Run as HTTP web server (default)
  stdio       - Run as stdio MCP server

Examples:
  .\docker.ps1 build
  .\docker.ps1 build -Modules "Pester PSScriptAnalyzer"
  .\docker.ps1 build -Modules "Az.Accounts@2.0.0 Pester@>=5.0.0"
  .\docker.ps1 run web       # Start web server
  .\docker.ps1 run stdio     # Start stdio server

Environment Variables:
  `$env:INSTALL_PS_MODULES     - Space or comma-separated list of modules
  `$env:MODULE_INSTALL_SCOPE   - Installation scope (AllUsers or CurrentUser)
  `$env:SKIP_PUBLISHER_CHECK   - Skip publisher validation (true or false)

Module Version Syntax:
  ModuleName             - Install latest version
  ModuleName@1.2.3       - Install specific version
  ModuleName@>=1.0.0     - Install minimum version
  ModuleName@<=2.0.0     - Install maximum version
"@
    exit 0
}

if ($Help) {
    Show-Usage
}

$ImageName = "poshmcp"
$SkipCheck = $env:SKIP_PUBLISHER_CHECK ?? "true"

switch ($Command) {
    'build' {
        Write-Information "Building Docker image..." -Tags 'Status'
        
        $buildArgs = @()
        
        if ($Modules) {
            Write-Information "📦 Pre-installing PowerShell modules: $Modules" -Tags 'Status'
            $buildArgs += "--build-arg"
            $buildArgs += "INSTALL_PS_MODULES=$Modules"
        }
        
        if ($Scope) {
            $buildArgs += "--build-arg"
            $buildArgs += "MODULE_INSTALL_SCOPE=$Scope"
        }
        
        if ($SkipCheck) {
            $buildArgs += "--build-arg"
            $buildArgs += "SKIP_PUBLISHER_CHECK=$SkipCheck"
        }
        
        $buildArgs += "-t"
        $buildArgs += $ImageName
        $buildArgs += "."
        
        Write-Verbose "Build arguments: $($buildArgs -join ' ')"
        Write-Verbose "Image name: $ImageName"
        Write-Verbose "Module scope: $Scope"
        Write-Information "Running: docker build $($buildArgs -join ' ')" -Tags 'Status'
        & docker build @buildArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "✅ Docker image built successfully: $ImageName" -ForegroundColor Green
            
            if ($Modules) {
                Write-Host "📦 Pre-installed modules: $Modules" -ForegroundColor Green
                Write-Host ""
                Write-Host "💡 These modules are now available and don't need runtime installation" -ForegroundColor Cyan
                Write-Host "💡 Update appsettings.json to use 'ImportModules' instead of 'InstallModules'" -ForegroundColor Cyan
            }
        } else {
            Write-Host ""
            Write-Host "❌ Docker build failed" -ForegroundColor Red
            exit 1
        }
    }
    
    'run' {
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
                Show-Usage
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
        Show-Usage
    }
}
