#!/bin/bash

# Startup script for PoshMcp - can run either Web server or stdio server
# Based on POSHMCP_MODE environment variable

set -e

# Default to web mode if not specified
POSHMCP_MODE=${POSHMCP_MODE:-web}

echo "PoshMcp startup mode: $POSHMCP_MODE"

case "$POSHMCP_MODE" in
    "web"|"http")
        echo "Starting PoshMcp Web Server..."
        cd /app/web
        exec dotnet PoshMcp.Web.dll
        ;;
    "stdio"|"server")
        echo "Starting PoshMcp stdio Server..."
        cd /app/server
        exec dotnet PoshMcp.dll
        ;;
    *)
        echo "Error: Invalid POSHMCP_MODE='$POSHMCP_MODE'. Valid values: web, http, stdio, server"
        exit 1
        ;;
esac
