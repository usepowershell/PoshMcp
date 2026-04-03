# Azure Container Apps deployment checklist

Use this checklist to ensure a smooth deployment of PoshMcp to Azure Container Apps.

## Pre-deployment

### Prerequisites
- [ ] Azure CLI installed (`az version` shows v2.50.0+)
- [ ] Docker installed and running (`docker ps` succeeds)
- [ ] Logged into Azure (`az login` completed)
- [ ] Correct subscription selected (`az account show`)
- [ ] Azure Container Registry name chosen (alphanumeric, globally unique)
- [ ] Resource group name chosen
- [ ] Azure region selected (recommend: eastus, westus2, westeurope)

### Validation
- [ ] Run validation script (`./validate.sh` or `.\validate.ps1`)
- [ ] All validation checks pass (green)
- [ ] No critical warnings

### Configuration
- [ ] Review [parameters.json](parameters.json)
- [ ] Update `containerImage` placeholder if needed
- [ ] Update `containerRegistryServer` placeholder if needed
- [ ] Customize CPU/memory if needed
- [ ] Customize replica counts (min/max)
- [ ] Update PowerShell function names if needed
- [ ] Set appropriate tags

### Environment Variables
- [ ] Set `REGISTRY_NAME` (required)
- [ ] Set `RESOURCE_GROUP` (optional, defaults to poshmcp-rg)
- [ ] Set `LOCATION` (optional, defaults to eastus)
- [ ] Set `AZURE_SUBSCRIPTION` (optional, uses current subscription)

## Deployment

### Run Deployment Script
- [ ] Navigate to `infrastructure/azure` directory
- [ ] Make scripts executable (bash): `chmod +x deploy.sh validate.sh`
- [ ] Run deployment: `./deploy.sh` or `.\deploy.ps1 -RegistryName myregistry`

### Monitor Deployment Progress
- [ ] Resource group creation succeeds
- [ ] Azure Container Registry created/verified
- [ ] Docker image builds successfully
- [ ] Image pushed to ACR
- [ ] Bicep deployment completes (5-10 minutes)
- [ ] Health check passes
- [ ] Application URL displayed

## Post-deployment verification

### Health checks
- [ ] Navigate to application URL (https://[app-name].[region].azurecontainerapps.io)
- [ ] Test `/health` endpoint (detailed JSON response)
- [ ] Test `/health/ready` endpoint (returns 200 OK)
- [ ] Verify all checks show "Healthy" status

### Logging and monitoring
- [ ] Open Azure Portal
- [ ] Navigate to resource group
- [ ] Open Log Analytics workspace
- [ ] Run sample query to verify logs flowing:
  ```kusto
  ContainerAppConsoleLogs_CL
  | where ContainerAppName_s == "poshmcp"
  | where TimeGenerated > ago(15m)
  | take 20
  ```
- [ ] Open Application Insights
- [ ] Verify data is being received (Live Metrics)

### Container App verification
- [ ] Check replica status: `az containerapp replica list --name poshmcp --resource-group [rg] -o table`
- [ ] Verify replicas are running
- [ ] Check revision status: `az containerapp revision list --name poshmcp --resource-group [rg] -o table`
- [ ] Verify latest revision is active

### Security verification
- [ ] Managed identity created and assigned
- [ ] Ingress configured (HTTPS only)
- [ ] Secrets stored securely (not in plaintext)
- [ ] Container running as non-root user

## Configuration verification

### Scaling
- [ ] Verify autoscaling rule configured (HTTP, 50 concurrent requests)
- [ ] Check min replicas matches parameters
- [ ] Check max replicas matches parameters
- [ ] Test scaling by sending traffic (optional)

### Resources
- [ ] Verify CPU allocation matches parameters
- [ ] Verify memory allocation matches parameters
- [ ] Check resource usage in metrics

### Environment variables
- [ ] Verify PowerShell functions configured correctly
- [ ] Check dynamic reload tools enabled/disabled as configured
- [ ] Confirm Application Insights connection string injected

## Documentation

- [ ] Note the application URL for team
- [ ] Document Application Insights instrumentation key
- [ ] Record Log Analytics workspace ID
- [ ] Save managed identity client ID (if needed)
- [ ] Update project documentation with deployment details

## Team Handoff

- [ ] Share application URL with team
- [ ] Provide access to Azure Portal (RBAC if needed)
- [ ] Share Log Analytics workspace for monitoring
- [ ] Document any custom configurations
- [ ] Provide troubleshooting commands

## Optional Enhancements

### Security
- [ ] Configure Azure Key Vault integration
- [ ] Set up private networking (VNet integration)
- [ ] Implement Azure Front Door (global load balancing)
- [ ] Configure diagnostic settings for compliance

### Monitoring
- [ ] Create alerts for failed health checks
- [ ] Set up cost alerts
- [ ] Configure alert for high error rate
- [ ] Set up on-call notifications

### Automation
- [ ] Set up CI/CD pipeline (GitHub Actions or Azure DevOps)
- [ ] Configure deployment slots/blue-green deployment
- [ ] Implement automated rollback on failure
- [ ] Add deployment approval gates

### Backup & DR
- [ ] Document disaster recovery procedure
- [ ] Set up backup for configuration
- [ ] Test failover to secondary region (if multi-region)
- [ ] Document rollback procedures

## Troubleshooting

If any step fails, see [README.md — Troubleshooting](README.md#troubleshooting) or [QUICKSTART.md — Troubleshooting commands](QUICKSTART.md#troubleshooting-commands).

Common issues:
- **Image pull failure:** Check ACR credentials, verify registry name
- **Health check failures:** Check `/health` endpoint, verify port 8080
- **Deployment timeout:** Check Bicep syntax, verify resource availability
- **Scale issues:** Check CPU/memory allocation, verify autoscaling rules

## Sign-off

- [ ] Deployment completed successfully
- [ ] Health checks passing
- [ ] Logs flowing to Log Analytics
- [ ] Application Insights receiving telemetry
- [ ] Team notified of deployment
- [ ] Documentation updated

---

**Deployment Date:** _____________
**Deployed By:** _____________
**Application URL:** _____________
**Resource Group:** _____________
**Notes:** _____________
