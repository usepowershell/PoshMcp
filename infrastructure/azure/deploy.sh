#!/bin/bash
# Azure Container Apps deployment script for PoshMcp
# This script handles the complete deployment workflow including:
# - Prerequisites validation
# - Container image build and push
# - Infrastructure deployment via Bicep
# - Post-deployment verification

set -e  # Exit on error
set -o pipefail  # Exit on pipe failure

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BICEP_FILE="$SCRIPT_DIR/main.bicep"
PARAMETERS_FILE="$SCRIPT_DIR/parameters.json"

# Default values (can be overridden by environment variables)
RESOURCE_GROUP="${RESOURCE_GROUP:-poshmcp-rg}"
LOCATION="${LOCATION:-eastus}"
CONTAINER_APP_NAME="${CONTAINER_APP_NAME:-poshmcp}"
REGISTRY_NAME="${REGISTRY_NAME:-}"
IMAGE_TAG="${IMAGE_TAG:-$(date +%Y%m%d-%H%M%S)}"
SUBSCRIPTION="${SUBSCRIPTION:-}"

# Helper functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check prerequisites
check_prerequisites() {
    log_info "Checking prerequisites..."
    
    # Check Azure CLI
    if ! command -v az &> /dev/null; then
        log_error "Azure CLI not found. Please install: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
        exit 1
    fi
    
    # Check Docker
    if ! command -v docker &> /dev/null; then
        log_error "Docker not found. Please install: https://docs.docker.com/get-docker/"
        exit 1
    fi
    
    # Check if logged in to Azure
    if ! az account show &> /dev/null; then
        log_error "Not logged in to Azure. Please run: az login"
        exit 1
    fi
    
    log_success "All prerequisites met"
}

# Set Azure subscription
set_subscription() {
    if [ -n "$SUBSCRIPTION" ]; then
        log_info "Setting subscription to: $SUBSCRIPTION"
        az account set --subscription "$SUBSCRIPTION"
    fi
    
    CURRENT_SUB=$(az account show --query name -o tsv)
    log_info "Using subscription: $CURRENT_SUB"
}

# Create resource group if it doesn't exist
create_resource_group() {
    log_info "Checking resource group: $RESOURCE_GROUP"
    
    if ! az group show --name "$RESOURCE_GROUP" &> /dev/null; then
        log_info "Creating resource group: $RESOURCE_GROUP in $LOCATION"
        az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
        log_success "Resource group created"
    else
        log_info "Resource group already exists"
    fi
}

# Create or get Azure Container Registry
setup_container_registry() {
    if [ -z "$REGISTRY_NAME" ]; then
        log_error "REGISTRY_NAME environment variable not set"
        echo "Usage: REGISTRY_NAME=myregistry ./deploy.sh"
        exit 1
    fi
    
    log_info "Checking Azure Container Registry: $REGISTRY_NAME"
    
    if ! az acr show --name "$REGISTRY_NAME" --resource-group "$RESOURCE_GROUP" &> /dev/null; then
        log_info "Creating Azure Container Registry: $REGISTRY_NAME"
        az acr create \
            --name "$REGISTRY_NAME" \
            --resource-group "$RESOURCE_GROUP" \
            --location "$LOCATION" \
            --sku Standard \
            --admin-enabled true
        log_success "Container registry created"
    else
        log_info "Container registry already exists"
    fi
    
    # Get registry credentials
    REGISTRY_SERVER="${REGISTRY_NAME}.azurecr.io"
    log_info "Registry server: $REGISTRY_SERVER"
}

# Build and push container image
build_and_push_image() {
    log_info "Building container image..."
    
    cd "$PROJECT_ROOT"
    
    # Build image
    FULL_IMAGE_NAME="${REGISTRY_SERVER}/poshmcp:${IMAGE_TAG}"
    docker build -t "$FULL_IMAGE_NAME" -t "${REGISTRY_SERVER}/poshmcp:latest" -f Dockerfile .
    
    log_success "Image built: $FULL_IMAGE_NAME"
    
    # Login to ACR
    log_info "Logging in to Azure Container Registry..."
    az acr login --name "$REGISTRY_NAME"
    
    # Push image
    log_info "Pushing image to registry..."
    docker push "$FULL_IMAGE_NAME"
    docker push "${REGISTRY_SERVER}/poshmcp:latest"
    
    log_success "Image pushed to: $REGISTRY_SERVER"
}

# Deploy infrastructure using Bicep
deploy_infrastructure() {
    log_info "Deploying infrastructure with Bicep..."
    
    # Update parameters file with actual values
    TEMP_PARAMS=$(mktemp)
    jq --arg image "$FULL_IMAGE_NAME" \
       --arg registry "$REGISTRY_SERVER" \
       --arg location "$LOCATION" \
       '.parameters.containerImage.value = $image |
        .parameters.containerRegistryServer.value = $registry |
        .parameters.location.value = $location' \
       "$PARAMETERS_FILE" > "$TEMP_PARAMS"
    
    # Deploy using Bicep
    DEPLOYMENT_NAME="poshmcp-deployment-$(date +%Y%m%d-%H%M%S)"
    
    az deployment group create \
        --name "$DEPLOYMENT_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --template-file "$BICEP_FILE" \
        --parameters "@$TEMP_PARAMS" \
        --verbose
    
    rm "$TEMP_PARAMS"
    
    log_success "Infrastructure deployed"
}

# Get deployment outputs
get_deployment_info() {
    log_info "Retrieving deployment information..."
    
    APP_URL=$(az containerapp show \
        --name "$CONTAINER_APP_NAME" \
        --resource-group "$RESOURCE_GROUP" \
        --query "properties.configuration.ingress.fqdn" \
        -o tsv)
    
    if [ -n "$APP_URL" ]; then
        log_success "Application URL: https://$APP_URL"
        
        # Test health endpoint
        log_info "Testing health endpoint..."
        sleep 10  # Wait for app to start
        
        if curl -f -s "https://$APP_URL/health/ready" > /dev/null; then
            log_success "Health check passed"
        else
            log_warning "Health check failed - application may still be starting"
        fi
        
        echo ""
        log_info "Deployment Summary:"
        echo "  Application URL: https://$APP_URL"
        echo "  Health Check: https://$APP_URL/health"
        echo "  Resource Group: $RESOURCE_GROUP"
        echo "  Container App: $CONTAINER_APP_NAME"
    else
        log_error "Could not retrieve application URL"
    fi
}

# Main deployment workflow
main() {
    log_info "Starting PoshMcp deployment to Azure Container Apps"
    echo ""
    
    check_prerequisites
    set_subscription
    create_resource_group
    setup_container_registry
    build_and_push_image
    deploy_infrastructure
    get_deployment_info
    
    echo ""
    log_success "Deployment completed successfully!"
}

# Run main function
main
