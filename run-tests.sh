#!/bin/bash

# PoshMcp Test Runner Script
# This script provides convenient commands for running different categories of tests

set -e

## Check to see if TEST_VERBOSITY exists, if not default to normal
if [ -z "${TEST_VERBOSITY}" ]; then
    TEST_VERBOSITY="normal"
fi

## Check to see if TEST_VERBOSITY is a valid value
## q, quiet, m, minimal, n, normal, d, detailed, diag, diagnostic
case "${TEST_VERBOSITY}" in
    "q"|"quiet"|"m"|"minimal"|"n"|"normal"|"d"|"detailed"|"diag"|"diagnostic")
        # Valid verbosity level
        ;;
    *)
        echo "❌ Invalid TEST_VERBOSITY value: '${TEST_VERBOSITY}'"
        echo "Valid values are: q, quiet, m, minimal, n, normal, d, detailed, diag, diagnostic"
        exit 1
        ;;
esac



PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

case "${1:-all}" in
    "unit")
        echo "🧪 Running Unit Tests..."
        filter="--filter FullyQualifiedName~Unit"
        ;;
    "functional")
        ## Validate TEST_FILTER if provided
        if [ -n "${TEST_FILTER}" ]; then
            # Get all functional test directories
            functional_dirs=""
            if [ -d "PoshMcp.Tests/Functional" ]; then
                functional_dirs=$(find PoshMcp.Tests/Functional -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort)
            fi
            
            if [ -z "${functional_dirs}" ]; then
                echo "❌ No functional test directories found in PoshMcp.Tests/Functional"
                exit 1
            fi
            
            # Check if TEST_FILTER matches any directory name (start or full match)
            filter_valid=false
            for dir in ${functional_dirs}; do
                # Check for exact match or if the directory starts with the filter
                if [ "${dir}" = "${TEST_FILTER}" ] || [[ "${dir}" == "${TEST_FILTER}"* ]]; then
                    filter_valid=true
                    break
                fi
            done
            
            if [ "${filter_valid}" = false ]; then
                echo "❌ Invalid TEST_FILTER value: '${TEST_FILTER}'"
                echo "Available functional test directories:"
                for dir in ${functional_dirs}; do
                    echo "  - ${dir}"
                done
                echo ""
                echo "TEST_FILTER must match the start or full name of a directory"
                exit 1
            fi
        fi
        echo "⚙️ Running Functional Tests..."
        filter="--filter FullyQualifiedName~Functional.${TEST_FILTER}"
        ;;
    "integration")
        echo "🔗 Running Integration Tests..."
        filter="--filter FullyQualifiedName~Integration.${TEST_FILTER}"
        ;;
    "fast")
        echo "🚀 Running Fast Tests (Unit + Functional)..."
        filter="--filter FullyQualifiedName~Unit|FullyQualifiedName~Functional"
        ;;
    "all")
        echo "🎯 Running All Tests..."
        filter=""
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
        echo "Environment Variables:"
        echo "  TEST_VERBOSITY     Set test output verbosity level (default: normal)"
        echo "                     Valid values: q, quiet, m, minimal, n, normal, d, detailed, diag, diagnostic"
        echo "  TEST_FILTER  Filter functional tests by directory name (optional)"
        echo "                     Must match the start or full name of a directory in PoshMcp.Tests/Functional"
        echo ""
        echo "Examples:"
        echo "  $0 unit                                    # Run only unit tests"
        echo "  $0 fast                                    # Run unit and functional tests"
        echo "  $0                                         # Run all tests"
        echo "  TEST_VERBOSITY=quiet $0 unit               # Run unit tests with minimal output"
        echo "  TEST_VERBOSITY=detailed $0 all             # Run all tests with detailed output"
        echo "  TEST_FILTER=Generated $0 functional  # Run only GeneratedAssembly functional tests"
        echo "  TEST_FILTER=McpServer $0 functional  # Run only McpServerSetup functional tests"
        exit 0
        ;;
    *)
        echo "❌ Unknown test category: $1"
        echo "Run '$0 help' for usage information"
        exit 1
        ;;
esac


dotnet test PoshMcp.Tests/PoshMcp.Tests.csproj ${filter} --logger "console;verbosity=${TEST_VERBOSITY}"