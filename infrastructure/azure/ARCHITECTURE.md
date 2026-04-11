# Azure Container Apps infrastructure summary

## Overview

This directory contains production-ready Infrastructure-as-Code (IaC) templates for deploying PoshMcp to Azure Container Apps. The deployment creates a fully managed, scalable, and observable container hosting environment.

## Architecture diagram

```
┌─────────────────────────────────────────────────────────┐
│               Azure Resource Group                       │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │    Container Apps Environment                   │    │
│  │  ┌──────────────────────────────────────────┐  │    │
│  │  │      PoshMcp Container App                │  │    │
│  │  │                                           │  │    │
│  │  │  - Image: ACR/poshmcp:tag                │  │    │
│  │  │  - Port: 8080 (HTTPS ingress)            │  │    │
│  │  │  - Probes: /health, /health/ready        │  │    │
│  │  │  - Scale: 1-10 replicas                  │  │    │
│  │  │  - Identity: User-assigned managed       │  │    │
│  │  │                                           │  │    │
│  │  └───────────────┬───────────────────────────┘  │    │
│  │                  │                               │    │
│  │                  │  Logs & Metrics               │    │
│  └──────────────────┼───────────────────────────────┘    │
│                     │                                     │
│  ┌──────────────────▼───────────────────────────────┐    │
│  │      Log Analytics Workspace                      │    │
│  │  - Container logs                                 │    │
│  │  - System events                                 │    │
│  │  - 30-day retention                              │    │
│  └──────────────────┬───────────────────────────────┘    │
│                     │                                     │
│  ┌──────────────────▼───────────────────────────────┐    │
│  │      Application Insights                         │    │
│  │  - APM (Application Performance Monitoring)       │    │
│  │  - Distributed tracing                           │    │
│  │  - OpenTelemetry metrics                         │    │
│  │  - Correlation ID tracking                       │    │
│  └───────────────────────────────────────────────────┘    │
│                                                          │
│  ┌───────────────────────────────────────────────────┐    │
│  │      Managed Identity                              │    │
│  │  - User-assigned                                  │    │
│  │  - Credential-free Azure auth                     │    │
│  │  - RBAC for Azure resources                      │    │
│  └───────────────────────────────────────────────────┘    │
│                                                          │
└──────────────────────────────────────────────────────────┘
         │
         │  Image Pull
         ▼
┌─────────────────────────────────┐
│  Azure Container Registry (ACR) │
│  - poshmcp:latest               │
│  - poshmcp:YYYYMMDD-HHMMSS     │
└─────────────────────────────────┘
```

## Components

### Container App
- **Name:** poshmcp (configurable)
- **Image:** Pulled from Azure Container Registry
- **Port:** 8080 (HTTP internally, HTTPS externally)
- **Environment:** POSHMCP_TRANSPORT=http (ASP.NET Core)
- **Health Checks:** 
  - Startup: `/health` (150s max tolerance)
  - Liveness: `/health/ready` (30s interval)
  - Readiness: `/health/ready` (10s interval)
- **Scaling:** HTTP-based, 1-10 replicas (configurable)
- **Resources:** 0.5 vCPU, 1GB memory (configurable)

### Log Analytics workspace
- **Purpose:** Centralized logging and monitoring
- **Retention:** 30 days (configurable)
- **Log Types:**
  - Container console logs
  - System logs
  - Health check events
- **Integration:** Linked to Application Insights

### Application Insights
- **Purpose:** Application Performance Monitoring (APM)
- **Features:**
  - Request/response tracking
  - Correlation ID propagation
  - OpenTelemetry metrics export
  - Custom telemetry from PoshMcp
- **Connection:** Injected as secret into Container App

### Managed Identity
- **Type:** User-assigned
- **Purpose:** Credential-free authentication to Azure services
- **Usage:**
  - Can access Azure Key Vault
  - Can read from Azure Storage
  - Can authenticate to other Azure resources
- **Security:** No credentials stored in container

### Container Apps environment
- **Purpose:** Hosting environment for Container Apps
- **Features:**
  - Integrated Log Analytics
  - Shared networking
  - Dapr integration ready (future)
- **Zone Redundancy:** Disabled (configurable)

For a full list of files and navigation guidance, see [INDEX.md](INDEX.md).

## Deployment workflow

```
┌─────────────────┐
│  validate.sh/.ps1│
└────────┬─────────┘
         │ Check prerequisites
         │ Validate Bicep syntax
         │ Check Azure auth
         ▼
┌─────────────────┐
│   deploy.sh/.ps1 │
└────────┬─────────┘
         │
         ├─> Create Resource Group (if needed)
         │
         ├─> Create/Verify ACR
         │
         ├─> Build Docker Image
         │   └─> Tag with timestamp
         │
         ├─> Push to ACR
         │   └─> Authenticate with managed identity
         │
         ├─> Deploy Bicep Template
         │   ├─> Log Analytics Workspace
         │   ├─> Application Insights
         │   ├─> Container Apps Environment
         │   ├─> Managed Identity
         │   └─> Container App
         │
         └─> Verify Deployment
             ├─> Get app URL
             ├─> Test health endpoint
             └─> Display summary
```

## Default configuration

### Production (parameters.json)
```
CPU: 0.5 cores
Memory: 1.0 GB
Min Replicas: 1 (always-on)
Max Replicas: 10
Scaling: 50 concurrent requests/replica
Estimated Cost: $30-50/month
```

### Development (parameters.local.json.template)
```
CPU: 0.25 cores
Memory: 0.5 GB
Min Replicas: 0 (scale-to-zero)
Max Replicas: 3
Scaling: 50 concurrent requests/replica
Estimated Cost: $5-15/month
```

## Integration with PoshMcp features

### Phase 1 Health Checks (2026-03-27)
- ✅ Container Apps probes use `/health` and `/health/ready` endpoints
- ✅ Startup probe allows 150s for PowerShell initialization
- ✅ Liveness/readiness probes verify <500ms health check performance

### Phase 1 Correlation IDs (2026-03-27)
- ✅ Correlation IDs flow through to Application Insights
- ✅ Distributed tracing across multiple replicas
- ✅ X-Correlation-ID header propagates through Container Apps ingress

### Phase 1 Observability (2026-03-27)
- ✅ OpenTelemetry metrics exported to Application Insights
- ✅ Container logs forwarded to Log Analytics
- ✅ Correlation IDs appear in all logs and metrics

## Security features

✅ **HTTPS-only ingress** - Automatic TLS termination  
✅ **Managed Identity** - No stored credentials  
✅ **Encrypted secrets** - Registry passwords and connection strings  
✅ **Non-root container** - Runs as appuser (UID 1001)  
✅ **Network isolation** - Internal Container Apps Environment networking  
✅ **RBAC-ready** - Managed identity for fine-grained permissions  

## Monitoring strategy

### Real-time monitoring
- **Container Logs:** `az containerapp logs show --follow`
- **Health Checks:** `curl https://[app-url]/health`
- **Metrics:** Azure Portal > Container App > Metrics

### Historical analysis
- **Log Analytics:** Kusto Query Language (KQL) queries
- **Application Insights:** Request/dependency maps
- **Cost Analysis:** Azure Cost Management + Billing

### Alerting (future enhancement)
- Failed health checks
- High error rate
- Excessive scaling
- Cost thresholds

## CI/CD integration

### GitHub Actions
- Workflow example provided in [README.md](README.md#cicd-integration)
- Requires: AZURE_CREDENTIALS, REGISTRY_NAME, RESOURCE_GROUP secrets
- Automated deployment on push to main branch

### Azure DevOps
- Pipeline example provided in [README.md](README.md#cicd-integration)
- Uses azure/login@v1 task
- Supports multi-stage deployments

## Next steps

After deployment:
1. ✅ Test health endpoints
2. ✅ Verify logs in Log Analytics
3. ✅ Check Application Insights integration
4. ⚠️ Configure alerts and budget limits
5. ⚠️ Set up continuous deployment (CI/CD)
6. ⚠️ Consider private networking (VNet integration)
7. ⚠️ Implement Azure Key Vault for secrets
8. ⚠️ Configure backup and disaster recovery

## Maintenance

### Regular tasks
- Monitor costs and adjust scaling parameters
- Review health check success rate
- Update container images regularly
- Review and rotate secrets
- Analyze performance metrics

### Upgrade path
- Update image: `az containerapp update --image new-image:tag`
- Update infrastructure: Re-run deployment scripts with new parameters
- Rollback: Use revision activation

## See also

- [README.md](README.md) — Full deployment guide with prerequisites, configuration, and troubleshooting
- [QUICKSTART.md](QUICKSTART.md) — Quick-reference commands for common operations
- [MODULARIZATION.md](MODULARIZATION.md) — Bicep module architecture and scope handling
- [CHECKLIST.md](CHECKLIST.md) — Step-by-step deployment verification checklist
- [INDEX.md](INDEX.md) — File navigation guide
- [PoshMcp main README](../../README.md) — Project overview
- [Azure Container Apps documentation](https://learn.microsoft.com/azure/container-apps/)
