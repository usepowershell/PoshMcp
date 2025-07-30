#!/bin/bash

# PowerShell MCP Server Launcher
# This script provides options to run the server or test client

echo "=== PowerShell MCP Server ==="
echo ""
echo "Choose an option:"
echo "1. Run MCP Server (for production use)"
echo "2. Run Interactive Test Client (for testing/debugging)"
echo "3. Build both projects"
echo "4. Run tests"
echo "5. Launch MCP Inspector"
echo "6. Exit"
echo ""

export Logging__LogLevel__Microsoft="Debug"

read -p "Enter your choice (1-6): " choice

case $choice in
    1)
        echo "Starting MCP Server..."
        echo "Press Ctrl+C to stop the server."
        echo ""
        dotnet run --project PoshMcp.Server/PoshMcp.csproj
        ;;
    2)
        echo "Starting Interactive Test Client..."
        dotnet run --project TestClient/TestClient.csproj
        ;;
    3)
        echo "Building all projects..."
        dotnet build
        echo "Build complete!"
        ;;
    4)
        echo "Running tests serially..."
        dotnet test --maxcpucount:1
        ;;
    5)
        echo "Launching MCP Inspector..."
        npx @modelcontextprotocol/inspector dotnet run --project PoshMcp.Server/PoshMcp.csproj
        ;;
    6)
        echo "Goodbye!"
        exit 0
        ;;
    *)
        echo "Invalid choice. Please run the script again and choose 1-5."
        exit 1
        ;;
esac
