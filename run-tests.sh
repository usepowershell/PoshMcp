#!/bin/bash

# PoshMcp Test Runner Script
# This script provides convenient commands for running different categories of tests

set -e

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

case "${1:-all}" in
    "unit")
        echo "🧪 Running Unit Tests..."
        dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --filter "FullyQualifiedName~Unit" --logger "console;verbosity=normal"
        ;;
    "functional")
        echo "⚙️ Running Functional Tests..."
        dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --filter "FullyQualifiedName~Functional" --logger "console;verbosity=normal"
        ;;
    "integration")
        echo "🔗 Running Integration Tests..."
        dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --filter "FullyQualifiedName~Integration" --logger "console;verbosity=normal"
        ;;
    "fast")
        echo "🚀 Running Fast Tests (Unit + Functional)..."
        dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --filter "FullyQualifiedName~Unit|FullyQualifiedName~Functional" --logger "console;verbosity=normal"
        ;;
    "all")
        echo "🎯 Running All Tests..."
        dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj --logger "console;verbosity=normal"
        ;;
    "help"|"-h"|"--help")
        echo "PoshMcp Test Runner"
        echo ""
        echo "Usage: $0 [category]"
        echo ""
        echo "Categories:"
        echo "  unit         Run unit tests only (fast, isolated)"
        echo "  functional   Run functional tests only (medium speed)"
        echo "  integration  Run integration tests only (slower, with dependencies)"
        echo "  fast         Run unit and functional tests (skip integration)"
        echo "  all          Run all tests (default)"
        echo "  help         Show this help message"
        echo ""
        echo "Examples:"
        echo "  $0 unit                # Run only unit tests"
        echo "  $0 fast                # Run unit and functional tests"
        echo "  $0                     # Run all tests"
        ;;
    *)
        echo "❌ Unknown test category: $1"
        echo "Run '$0 help' for usage information"
        exit 1
        ;;
esac
