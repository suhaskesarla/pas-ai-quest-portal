# Azure App Service POC deployment

This pack provisions a disposable personal/test POC with Bicep and deploys it through the manually queued [Azure DevOps pipeline](azure-pipelines.yml). Its App Service frontend and Azure DevOps deployment pipeline are POC-only choices. The frozen corporate Production architecture remains Azure Static Web Apps for the frontend and GitHub Actions for CI/CD; this pack does not replace that design.

The Azure POC branch is based on `main` independently of the Entra feature branch. Run the `deploy` or `full` action only after the Entra branch has merged into the deployed codebase; `provision` may be reviewed separately.

The pipeline supports `provision`, `deploy`, `full`, and a separately guarded `destroy` action. It creates two App Services, Azure SQL, Storage, Key Vault, Application Insights, and Log Analytics in a dedicated POC resource group. Live Entra app registrations, consent, role assignment, and Participant mapping are external prerequisites. No Azure resources are created by checking in this pack.

Read the [implementation guide](docs/infra_deploy/AZURE_POC_IMPLEMENTATION.md), [pipeline variables](docs/infra_deploy/PIPELINE_VARIABLES.md), [infrastructure handoff](docs/infra_deploy/INFRA_HANDOFF.md), and [cost and cleanup guidance](docs/infra_deploy/DESTROY_AND_COST_SAFETY.md) before a first run.

## Repository-aligned settings

The API uses `ConnectionStrings__QuestDatabase`, `Storage__ConnectionString`, `Authentication__Mode`, `Authentication__Entra__TenantId`, `Authentication__Entra__Audience`, `Authentication__Entra__RequiredScope`, and `Notifications__PortalBaseUrl`. The API's required scope is the **scope name** from the token's `scp` claim. The SPA uses the **full scope URI** in `VITE_ENTRA_API_SCOPE`.

The frontend build sets `VITE_APP_ENVIRONMENT=Production` and the four `VITE_ENTRA_*` values. Browser API calls use relative `/api` URLs. The web App Service runs [static-server.mjs](infra/runtime/static-server.mjs), configured with `PAS_API_ORIGIN`, to proxy those calls to the API App Service on the same browser origin. The API project is `src/api/PAS.AIQuestPortal.Api.csproj` (`net8.0`), the frontend build is `npm run build` in `src/web`, and EF migrations use the API project as both project and startup project.

## Before queueing

1. Create an Azure DevOps ARM service connection and provide the variables in [PIPELINE_VARIABLES.md](docs/infra_deploy/PIPELINE_VARIABLES.md). Mark `SQL_ADMIN_PASSWORD` secret.
2. Register the SPA redirect URI and API delegated scope in the test Entra tenant. Supply `ENTRA_API_SCOPE` as the full URI and `ENTRA_API_SCOPE_NAME` as its final scope name.
3. Use `action=full` for the first provision/deploy run. The pipeline can run SQL-backed tests against an ephemeral local SQL container when `runTests=true`.
4. Use `action=destroy` with `confirmDestroy=DELETE` only when the dedicated POC resource group should be deleted. Entra and Teams tenant objects are outside that group.

This pack has not been exercised against a live Azure subscription or tenant. Review the Bicep and service connection permissions before queueing a deployment.
