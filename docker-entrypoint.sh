#!/bin/bash

# Simplified startup script for PoshMcp
# Delegates all transport mode handling to the poshmcp CLI

set -e

# Run poshmcp serve with transport mode from environment or default to http
POSHMCP_TRANSPORT=${POSHMCP_TRANSPORT:-http}
exec /app/server/poshmcp serve --transport "$POSHMCP_TRANSPORT"

