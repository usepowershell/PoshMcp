#!/bin/bash
# Pre-deployment validation script
# Checks prerequisites and validates configuration before deployment

set -e

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BICEP_FILE="$SCRIPT_DIR/main.bicep"
PARAMETERS_FILE="$SCRIPT_DIR/parameters.json"

echo "PoshMcp Azure Deployment Validation"
echo "===================================="
echo ""

# Check Azure CLI
echo -n "Checking Azure CLI... "
if ! command -v az &> /dev/null; then
    echo -e "${RED}FAIL${NC}"
    echo "Azure CLI not installed"
    exit 1
fi
AZ_VERSION=$(az version --query '"azure-cli"' -o tsv)
echo -e "${GREEN}OK${NC} (version $AZ_VERSION)"

# Check Docker
echo -n "Checking Docker... "
if ! command -v docker &> /dev/null; then
    echo -e "${RED}FAIL${NC}"
    echo "Docker not installed"
    exit 1
fi
DOCKER_VERSION=$(docker --version | cut -d' ' -f3 | tr -d ',')
echo -e "${GREEN}OK${NC} (version $DOCKER_VERSION)"

# Check Docker daemon
echo -n "Checking Docker daemon... "
if ! docker ps &> /dev/null; then
    echo -e "${RED}FAIL${NC}"
    echo "Docker daemon not running"
    exit 1
fi
echo -e "${GREEN}OK${NC}"

# Check Azure login
echo -n "Checking Azure authentication... "
if ! az account show &> /dev/null; then
    echo -e "${RED}FAIL${NC}"
    echo "Not logged in to Azure. Run: az login"
    exit 1
fi
SUBSCRIPTION=$(az account show --query name -o tsv)
echo -e "${GREEN}OK${NC} ($SUBSCRIPTION)"

# Check Bicep file exists
echo -n "Checking Bicep template... "
if [ ! -f "$BICEP_FILE" ]; then
    echo -e "${RED}FAIL${NC}"
    echo "main.bicep not found"
    exit 1
fi
echo -e "${GREEN}OK${NC}"

# Validate Bicep syntax
echo -n "Validating Bicep syntax... "
if ! az bicep build --file "$BICEP_FILE" --stdout > /dev/null 2>&1; then
    echo -e "${RED}FAIL${NC}"
    echo "Bicep file has syntax errors"
    az bicep build --file "$BICEP_FILE"
    exit 1
fi
echo -e "${GREEN}OK${NC}"

# Check parameters file
echo -n "Checking parameters file... "
if [ ! -f "$PARAMETERS_FILE" ]; then
    echo -e "${RED}FAIL${NC}"
    echo "parameters.json not found"
    exit 1
fi
echo -e "${GREEN}OK${NC}"

# Validate parameters JSON
echo -n "Validating parameters JSON... "
if ! jq empty "$PARAMETERS_FILE" 2>/dev/null; then
    echo -e "${RED}FAIL${NC}"
    echo "parameters.json is not valid JSON"
    exit 1
fi
echo -e "${GREEN}OK${NC}"

# Check for placeholder values
echo -n "Checking for placeholder values... "
if grep -q "YOUR_REGISTRY" "$PARAMETERS_FILE"; then
    echo -e "${YELLOW}WARNING${NC}"
    echo "Parameters file contains placeholder values (YOUR_REGISTRY)"
    echo "Update parameters.json or use environment variables"
else
    echo -e "${GREEN}OK${NC}"
fi

# Check required environment variables
echo ""
echo "Environment Variables:"
if [ -n "$REGISTRY_NAME" ]; then
    echo -e "  REGISTRY_NAME: ${GREEN}$REGISTRY_NAME${NC}"
else
    echo -e "  REGISTRY_NAME: ${YELLOW}Not set (required for deployment)${NC}"
fi

if [ -n "$RESOURCE_GROUP" ]; then
    echo -e "  RESOURCE_GROUP: ${GREEN}$RESOURCE_GROUP${NC}"
else
    echo -e "  RESOURCE_GROUP: ${YELLOW}Not set (will use default: poshmcp-rg)${NC}"
fi

# Check location
LOCATION=${LOCATION:-"eastus"}
echo -n "Validating Azure location... "
if az account list-locations --query "[?name=='$LOCATION']" -o tsv &> /dev/null; then
    echo -e "${GREEN}OK${NC} ($LOCATION)"
else
    echo -e "${YELLOW}WARNING${NC} (location '$LOCATION' may be invalid)"
fi

# Check quota availability (if possible)
echo -n "Checking Container Apps availability in region... "
if az provider show --namespace Microsoft.App --query "registrationState" -o tsv 2>/dev/null | grep -q "Registered"; then
    echo -e "${GREEN}OK${NC}"
else
    echo -e "${YELLOW}WARNING${NC}"
    echo "Microsoft.App provider not registered. Registering..."
    az provider register --namespace Microsoft.App
    echo "Registration initiated (may take a few minutes)"
fi

echo ""
echo -e "${GREEN}✓ Validation completed successfully${NC}"
echo ""
echo "Ready to deploy! Run:"
echo "  export REGISTRY_NAME=myregistry"
echo "  ./deploy.sh"
