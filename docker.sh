#!/bin/bash

# Build and run PoshMcp Server in Docker (Web or stdio mode)

set -e

# Function to display usage
usage() {
    echo "Usage: $0 [build|run|stop|logs|clean] [mode]"
    echo ""
    echo "Commands:"
    echo "  build       - Build the Docker image"
    echo "  run [mode]  - Run the container using docker-compose"
    echo "  stop        - Stop the running container"
    echo "  logs        - Show container logs"
    echo "  clean       - Remove container and image"
    echo ""
    echo "Modes (for run command):"
    echo "  web|http    - Run as HTTP web server (default)"
    echo "  stdio       - Run as stdio MCP server"
    echo ""
    echo "Examples:"
    echo "  $0 build"
    echo "  $0 run web      # Start web server"
    echo "  $0 run stdio    # Start stdio server"
    exit 1
}

# Check if command is provided
if [ $# -eq 0 ]; then
    usage
fi

COMMAND=$1
MODE=${2:-web}  # Default to web mode
IMAGE_NAME="poshmcp"

case $COMMAND in
    build)
        echo "Building Docker image..."
        docker build -t $IMAGE_NAME .
        echo "✅ Docker image built successfully: $IMAGE_NAME"
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
