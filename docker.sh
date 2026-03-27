#!/bin/bash

# Build and run PoshMcp Server in Docker (Web or stdio mode)

set -e

# Function to display usage
usage() {
    echo "Usage: $0 [build|run|stop|logs|clean] [mode|options]"
    echo ""
    echo "Commands:"
    echo "  build [options]        - Build the Docker image"
    echo "  run [mode]             - Run the container using docker-compose"
    echo "  stop                   - Stop the running container"
    echo "  logs                   - Show container logs"
    echo "  clean                  - Remove container and image"
    echo ""
    echo "Build Options:"
    echo "  --modules \"module1 module2\"  - Pre-install PowerShell modules at build time"
    echo "  --scope [AllUsers|CurrentUser] - Module installation scope (default: AllUsers)"
    echo ""
    echo "Modes (for run command):"
    echo "  web|http    - Run as HTTP web server (default)"
    echo "  stdio       - Run as stdio MCP server"
    echo ""
    echo "Examples:"
    echo "  $0 build"
    echo "  $0 build --modules \"Pester PSScriptAnalyzer\""
    echo "  $0 build --modules \"Az.Accounts@2.0.0 Pester@>=5.0.0\""
    echo "  $0 run web      # Start web server"
    echo "  $0 run stdio    # Start stdio server"
    echo ""
    echo "Environment Variables:"
    echo "  INSTALL_PS_MODULES     - Space or comma-separated list of modules"
    echo "  MODULE_INSTALL_SCOPE   - Installation scope (AllUsers or CurrentUser)"
    echo "  SKIP_PUBLISHER_CHECK   - Skip publisher validation (true or false)"
    echo ""
    echo "Module Version Syntax:"
    echo "  ModuleName             - Install latest version"
    echo "  ModuleName@1.2.3       - Install specific version"
    echo "  ModuleName@>=1.0.0     - Install minimum version"
    echo "  ModuleName@<=2.0.0     - Install maximum version"
    exit 1
}

# Check if command is provided
if [ $# -eq 0 ]; then
    usage
fi

COMMAND=$1
shift  # Remove first argument, rest are options
MODE=""
MODULES="${INSTALL_PS_MODULES:-}"
SCOPE="${MODULE_INSTALL_SCOPE:-AllUsers}"
SKIP_CHECK="${SKIP_PUBLISHER_CHECK:-true}"

# Parse additional arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --modules)
            MODULES="$2"
            shift 2
            ;;
        --scope)
            SCOPE="$2"
            shift 2
            ;;
        web|http|stdio|server)
            MODE="$1"
            shift
            ;;
        *)
            echo "❌ Unknown option: $1"
            usage
            ;;
    esac
done

# Default mode if not specified
if [ -z "$MODE" ]; then
    MODE="web"
fi

IMAGE_NAME="poshmcp"

case $COMMAND in
    build)
        echo "Building Docker image..."
        
        # Prepare build arguments
        BUILD_ARGS=""
        if [ -n "$MODULES" ]; then
            echo "📦 Pre-installing PowerShell modules: $MODULES"
            BUILD_ARGS="--build-arg INSTALL_PS_MODULES=\"$MODULES\""
        fi
        
        if [ -n "$SCOPE" ]; then
            BUILD_ARGS="$BUILD_ARGS --build-arg MODULE_INSTALL_SCOPE=\"$SCOPE\""
        fi
        
        if [ -n "$SKIP_CHECK" ]; then
            BUILD_ARGS="$BUILD_ARGS --build-arg SKIP_PUBLISHER_CHECK=\"$SKIP_CHECK\""
        fi
        
        # Build the image
        eval "docker build $BUILD_ARGS -t $IMAGE_NAME ."
        
        echo ""
        echo "✅ Docker image built successfully: $IMAGE_NAME"
        
        if [ -n "$MODULES" ]; then
            echo "📦 Pre-installed modules: $MODULES"
            echo ""
            echo "💡 These modules are now available and don't need runtime installation"
            echo "💡 Update appsettings.json to use 'ImportModules' instead of 'InstallModules'"
        fi
        ;;
    
    run)
        case $MODE in
            web|http)
                echo "Starting PoshMcp Web Server..."
                docker-compose --profile web up -d
                echo "✅ PoshMcp Web Server is running at http://localhost:8080"
                echo "💡 Use '$0 logs' to view logs"
                echo "💡 Use '$0 stop' to stop the server"
                ;;
            stdio|server)
                echo "Starting PoshMcp stdio Server..."
                docker-compose --profile stdio up -d
                echo "✅ PoshMcp stdio Server is running"
                echo "💡 Use '$0 logs' to view logs"
                echo "💡 Use '$0 stop' to stop the server"
                echo "💡 Connect via stdio to communicate with the MCP server"
                ;;
            *)
                echo "❌ Unknown mode: $MODE"
                usage
                ;;
        esac
        ;;
    
    stop)
        echo "Stopping PoshMcp Servers..."
        docker-compose --profile web --profile stdio down
        echo "✅ PoshMcp Servers stopped"
        ;;
    
    logs)
        echo "Showing container logs (Ctrl+C to exit)..."
        docker-compose --profile web --profile stdio logs -f
        ;;
    
    clean)
        echo "Cleaning up containers and images..."
        docker-compose --profile web --profile stdio down --rmi all --volumes --remove-orphans
        echo "✅ Cleanup complete"
        ;;
    
    *)
        echo "❌ Unknown command: $COMMAND"
        usage
        ;;
esac
