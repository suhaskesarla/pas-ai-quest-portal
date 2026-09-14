# PAS AI Quest — Infra handoff for App Service deployment

## POC-only hosting and pipeline

**Azure App Service**

This personal/test POC uses Azure DevOps YAML for deployment. The frozen corporate Production architecture remains Azure Static Web Apps for the frontend and GitHub Actions for CI/CD; this pack does not replace it.

Why:

- team familiarity
- straightforward React + ASP.NET hosting
- simple Azure DevOps deployment
- public HTTPS endpoint included
- a disposable environment for testing the application before corporate deployment decisions

## POC resources requested

- Resource Group
- Linux App Service Plan
- React frontend App Service
- ASP.NET Core API App Service
- Azure SQL Database
- Storage Account / Blob container
- Key Vault
- Application Insights
- Log Analytics

## Deliberately deferred POC complexity

Not required for the first personal/test deployment:

- AKS
- private endpoints
- Front Door
- Application Gateway
- WAF
- custom domain
- custom TLS certificate
- NAT Gateway

These can be revisited for corporate deployment.

## Identity items needed

- Tenant ID
- SPA app registration / Client ID
- redirect URI
- API app registration
- API audience/Application ID URI
- delegated API scope
- consent
- `Quest.Manager` app role

## Teams items needed after API deployment

- Bot/Microsoft App registration
- Bot App ID
- Bot credential/certificate
- public messaging endpoint
- custom Teams app upload approval
- General channel
- Manager channel
- personal app installation for private messaging

## Deployment automation supplied

The repository pack contains:

```text
azure-pipelines.yml
infra/*.bicep
scripts/*.ps1
```

Pipeline actions:

```text
provision
deploy
full
destroy
```

## POC versus Production

The POC currently chooses simplicity:

- F1 App Service
- public Azure SQL endpoint with Azure-service access
- SQL authentication
- Storage shared key
- public Storage endpoint
- Key Vault provisioned but not mandatory for initial SQL/storage configuration

Corporate Production retains the frozen hosting/CI design and requires separate review of:

- paid App Service plan
- deployment slots
- VNet integration/private endpoints
- Managed Identity
- Key Vault references
- hardened SQL firewall
- Storage shared-key disablement
- production monitoring/alerts
- controlled migration runner
- backup/retention standards

## Questions for Infra

1. Which subscription/resource group should corporate UAT/PROD use?
2. Which corporate hosting configuration implements the frozen Static Web Apps frontend design?
3. Which paid App Service tier is standard?
4. VNet integration required?
5. SQL/Storage private endpoints required?
6. Managed Identity required for SQL/Storage?
7. Which Key Vault?
8. Which DNS names?
9. Which App Insights/Log Analytics workspace?
10. Azure DevOps service connection owner?
11. Who owns Entra registrations/app roles/consent?
12. Who owns Teams Bot/custom-app approval?
13. Which outbound proxy/firewall rules apply?
14. Does Azure workload traffic use corporate TLS interception?
15. Who runs/approves EF migrations?
