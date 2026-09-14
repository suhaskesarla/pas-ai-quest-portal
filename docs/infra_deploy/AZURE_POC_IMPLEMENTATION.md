# PAS AI Quest — Azure App Service POC implementation

## 1. Why this exists

PAS AI Quest is already designed to run as a React/Vite frontend plus ASP.NET Core API with SQL, blob/evidence storage, Microsoft Entra authentication and Teams notifications.

The purpose of this deployment pack is to make the Azure POC repeatable and disposable:

```text
Run pipeline: full
        |
        +--> Provision Azure resources
        |
        +--> Build/test application
        |
        +--> Apply EF migrations
        |
        +--> Deploy API
        |
        +--> Deploy frontend
        |
        +--> Smoke check

Run pipeline: destroy
        |
        +--> Delete dedicated POC resource group
```

This personal/test POC intentionally uses **App Service** and a manually queued Azure DevOps deployment pipeline. The frozen corporate Production design remains Azure Static Web Apps for the frontend and GitHub Actions for CI/CD. This pack neither replaces nor changes that architecture.

The POC branch is independent of the Entra feature branch. Its `deploy` and `full` actions require the Entra code to be present in the deployed revision; merge Entra first. Infrastructure-only `provision` can be reviewed separately.

---

## 2. What the pipeline provisions

The Bicep deployment creates:

| Resource | Why |
|---|---|
| Resource Group | One disposable boundary for the POC |
| Linux App Service Plan | Hosts both apps cheaply for the POC |
| Frontend App Service | Hosts the built React/Vite SPA |
| API App Service | Hosts ASP.NET Core API and notification worker |
| Azure SQL logical server | SQL endpoint |
| Azure SQL Database | PAS transactional data/outbox |
| Azure Storage | Evidence/files |
| Azure Key Vault | Future secure Bot/other secrets |
| Log Analytics | Central Azure logs |
| Application Insights | Application telemetry |

The default App Service Plan SKU is `F1`.

The SQL module requests:

```text
General Purpose serverless
useFreeLimit = true
freeLimitExhaustionBehavior = AutoPause
```

This deliberately favours **cost safety** over continuous availability. If the monthly free SQL allowance is exhausted, the database can pause rather than automatically generating paid overage.

---

## 3. What the pipeline does NOT create automatically

### Microsoft Entra registrations

The pipeline does not automatically create:

- SPA app registration
- API app registration
- API delegated scope
- `Quest.Manager` app role
- tenant admin consent

Why:

These are tenant-level identity objects, not ordinary resource-group resources. Creating and modifying them requires Microsoft Entra permissions that are often controlled separately from Azure subscription Contributor permissions.

For the POC, create them once in the test tenant and pass the resulting values into the pipeline.

### Microsoft Teams tenant activation

The pipeline also does not automatically:

- approve/upload a Teams custom app
- assign Teams app setup policies
- install the personal app for users
- capture real General/Manager destinations
- capture personal conversation references

These require tenant/admin/user interaction and should happen after the Azure API is publicly reachable.

---

## 4. Prerequisites

Before using the pipeline you need:

### Azure

- Azure subscription
- Azure DevOps project and a pipeline connected to this repository
- Azure Resource Manager service connection with permission to create/delete the POC resource group
- permission to register required resource providers

### Azure DevOps secret

Create a secret pipeline variable:

```text
SQL_ADMIN_PASSWORD
```

For the POC this password is:

- passed securely to Bicep
- used to configure the API connection string
- used temporarily by the EF migration step

It is not stored in the repository.

### Entra variables

Before the `deploy`/`full` action can run successfully in Production/Entra mode, configure:

```text
ENTRA_TENANT_ID
ENTRA_SPA_CLIENT_ID
ENTRA_API_AUDIENCE
ENTRA_API_SCOPE
ENTRA_API_SCOPE_NAME
```

The frontend redirect URI is derived automatically:

```text
https://<web-app-name>.azurewebsites.net
```

and must also be registered in the SPA app registration.

---

## 5. Azure DevOps setup

### 5.1 Create an ARM service connection

In Azure DevOps:

```text
Project Settings
  -> Service connections
  -> New service connection
  -> Azure Resource Manager
```

Give it access to the test Azure subscription.

Example name:

```text
sc-pas-ai-quest-poc
```

Queue the pipeline with this value for:

```text
azureServiceConnection
```

### 5.2 Add secret variable

Create:

```text
SQL_ADMIN_PASSWORD
```

Mark it secret.

A variable group can also be used.

### 5.3 Add Entra variables

Add:

```text
ENTRA_TENANT_ID
ENTRA_SPA_CLIENT_ID
ENTRA_API_AUDIENCE
ENTRA_API_SCOPE
ENTRA_API_SCOPE_NAME
```

These IDs/scopes are configuration, not SPA secrets.

---

## 6. Naming

The pipeline uses a user-provided short suffix because several Azure names must be globally unique.

Example:

```text
nameSuffix = suhas01
```

This produces names similar to:

```text
rg-pas-ai-quest-poc

app-pas-ai-quest-web-poc-suhas01
app-pas-ai-quest-api-poc-suhas01
sql-pas-ai-quest-poc-suhas01

stpasqpocsuhas01
kv-pasq-poc-suhas01
```

Choose a suffix containing only short lowercase letters/numbers.

---

## 7. Pipeline actions

### `full`

Use for the first deployment:

```text
action = full
```

Runs:

1. Azure provisioning
2. backend build
3. backend tests
4. frontend build/tests
5. packaging
6. App Service configuration
7. EF Core migration
8. API deployment
9. frontend deployment
10. smoke checks

### `provision`

Use when you want Azure resources only:

```text
action = provision
```

No application code is deployed.

### `deploy`

Use after the environment already exists:

```text
action = deploy
```

Infrastructure is not recreated.

Bicep is idempotent, so using `full` repeatedly is also safe in normal circumstances, but `deploy` is faster.

### `destroy`

Use:

```text
action = destroy
confirmDestroy = DELETE
```

The script deletes:

```text
rg-pas-ai-quest-<environment>
```

Everything inside that resource group is removed.

It also attempts to purge the soft-deleted POC Key Vault so its deterministic name can be reused.

It does **not** delete:

- Entra SPA app registration
- Entra API app registration
- Entra app-role assignments
- Teams custom app
- Teams tenant installations

Those objects live outside the resource group.

---

## 8. Frontend deployment

The Vite frontend is built on the Azure DevOps agent.

Build-time values include:

```text
VITE_ENTRA_TENANT_ID
VITE_ENTRA_CLIENT_ID
VITE_ENTRA_REDIRECT_URI
VITE_ENTRA_API_SCOPE
VITE_APP_ENVIRONMENT=Production
```

The generated `dist` directory is packaged and deployed to the frontend App Service.

A tiny dependency-free Node server (`infra/runtime/static-server.mjs`) is included to:

- serve the built SPA
- proxy the frontend's relative `/api` requests to the API App Service using `PAS_API_ORIGIN`
- support React deep-link fallback to `index.html`
- preserve Teams notification deep links
- avoid introducing another runtime package just to serve static assets

---

## 9. API deployment

The API project is `src/api/PAS.AIQuestPortal.Api.csproj` (`net8.0`). The pipeline runs:

```text
dotnet restore
dotnet build
dotnet test
dotnet publish
```

When `runTests=true`, SQL-backed tests use an ephemeral SQL Server container on the hosted build agent with `TEST_SQL_CONNECTION` set for that run. The published output is ZIP-deployed to the API App Service.

The App Service uses:

```text
ASPNETCORE_ENVIRONMENT=Production
```

This is intentional because PAS Production/Entra security rules must be exercised. Production Demo Auth should not become a hidden fallback.

---

## 10. Database migration

The migration script:

1. determines the Azure DevOps agent's current public IP
2. creates a temporary SQL firewall rule for that IP
3. runs `dotnet ef database update`
4. deletes the temporary rule in a `finally` block

This approach is appropriate for a POC using Microsoft-hosted build agents.

For a corporate Production deployment, replace this with a controlled migration runner/private network path.

### Important

The script assumes the EF migrations are associated with:

```text
src/api/PAS.AIQuestPortal.Api.csproj
```

If the repository stores migrations in a separate project, change `ProjectPath` / startup project accordingly.

---

## 11. Database connection

For the POC, the API uses SQL authentication.

The password is held as a secret Azure DevOps variable.

The pipeline configures the App Service connection string and does not write it into source control.

### Production improvement

Prefer:

```text
App Service Managed Identity
        |
        v
Azure SQL Entra authentication
```

when corporate Infra is ready to configure SQL Entra administration and database users.

The POC avoids this extra setup so the first Azure test remains straightforward.

---

## 12. Storage authentication

For the POC, the deployment obtains the Storage connection string and places it into the API App Service configuration.

This is intentionally simpler.

Production should prefer Managed Identity with Azure RBAC and disable shared-key access if organisational standards allow it.

---

## 13. Key Vault

Key Vault is provisioned now because later PAS deployment will need a secure home for values such as the Teams Bot credential.

The POC pipeline does not automatically insert secrets into Key Vault because a normal Azure Contributor service connection does not necessarily have Key Vault data-plane permission.

That separation prevents the deployment from silently assuming excessive permissions.

---

## 14. Entra configuration

The Entra code expects the deployment to supply:

```text
Tenant ID
SPA Client ID
API audience
delegated API scope
```

The API setting names are deliberately represented as pipeline parameters:

```text
apiAuthModeSetting
apiTenantSetting
apiAudienceSetting
apiScopeSetting
```

Why:

The pipeline defaults have been checked against `QuestAuthenticationOptions.cs`, `Program.cs`, and `appsettings.json`. To inspect candidate setting names again, run:

```powershell
pwsh ./scripts/discover-appsettings.ps1
```

and compare the output with:

```text
src/api/Configuration/QuestAuthenticationOptions.cs
src/api/appsettings.json
```

The API receives `Authentication__Mode`, `Authentication__Entra__TenantId`, `Authentication__Entra__Audience`, and `Authentication__Entra__RequiredScope`. The SPA receives the full `ENTRA_API_SCOPE` URI; the API receives `ENTRA_API_SCOPE_NAME` as the exact scope name expected in `scp`.

---

## 15. Frontend-to-API routing

Frontend and API are separate App Services. The frontend application uses relative `/api` URLs, so the POC Node static server proxies `/api` to the HTTPS API origin configured as `PAS_API_ORIGIN` on the web App Service. The browser therefore uses the frontend origin for API calls. The API's `Cors__AllowedOrigins__0` is also set to the exact frontend URL for direct-origin access; no wildcard origin is configured.

---

## 16. Teams after Azure deployment

Once the API is reachable at:

```text
https://app-pas-ai-quest-api-....azurewebsites.net
```

continue with the existing Teams activation guide.

High-level flow:

```text
Azure API deployed
   |
   v
Bot registration / credential
   |
   v
Bot messaging endpoint -> PAS API
   |
   v
Teams app package upload/install
   |
   v
General/Manager destination capture
   |
   v
Participant tenantId+oid provisioning
   |
   v
Personal conversation capture
   |
   v
TeamsBot provider
   |
   v
Live notification smoke tests
```

Do not invent conversation IDs or manually bypass the application provisioning flow.

---

## 17. Cost-safety decisions

This POC deliberately avoids:

- AKS
- Application Gateway
- Front Door
- NAT Gateway
- private endpoints
- Premium App Service tiers
- custom domains
- custom certificates

The Azure-provided:

```text
*.azurewebsites.net
```

HTTPS endpoints are enough for the initial test.

The SQL database defaults to free-limit AutoPause so the database does not automatically bill over its monthly free limit.

App Service defaults to `F1`, which is a POC/test tier and has no production SLA.

---

## 18. What to change for corporate UAT/Production

Do not copy the POC security/network simplifications directly into corporate Production.

Discuss with Infra:

- paid App Service tier
- deployment slots
- VNet integration
- SQL private endpoint
- Storage private endpoint
- Managed Identity for SQL/Storage
- Key Vault references
- purge protection
- WAF / Front Door if required
- fixed/private migration runner
- corporate DNS
- corporate TLS policies
- outbound firewall/proxy rules
- Application Insights retention
- backups
- alerting
- RBAC
- Conditional Access
- Teams admin policy

The POC is intended to prove application functionality and integration, not to define the final enterprise landing zone.

---

## 19. Known assumptions to verify against the repository

Before the first run, verify:

### Backend project path

Expected:

```text
src/api/PAS.AIQuestPortal.Api.csproj
```

### SQL connection string

ASP.NET Core environment variable:

```text
ConnectionStrings__QuestDatabase
```

### Storage application setting

Default deployment key:

```text
Storage__ConnectionString
```

### Authentication keys

Validated API environment-variable names:

```text
Authentication__Mode
Authentication__Entra__TenantId
Authentication__Entra__Audience
Authentication__Entra__RequiredScope
```

### Portal URL setting

Validated notification URL environment-variable name:

```text
Notifications__PortalBaseUrl
```

The API's `Authentication__Entra__RequiredScope` value is the scope **name**, for example `access_as_user`. `VITE_ENTRA_API_SCOPE` is the full scope URI, for example `api://<api-app-id>/access_as_user`.

---

## 20. Recommended first run

After the Azure account and DevOps service connection exist:

```text
1. Pick suffix:
   suhas01

2. Add secret:
   SQL_ADMIN_PASSWORD

3. Create/obtain Entra:
   tenant ID
   SPA client ID
   API audience
   full API scope URI and exact API scope name

4. Register:
   https://app-pas-ai-quest-web-poc-suhas01.azurewebsites.net
   as the SPA redirect URI.

5. Queue pipeline:

   action                  = full
   environmentName         = poc
   nameSuffix              = suhas01
   azureServiceConnection  = <your service connection>
   useFreeSql              = true

6. Validate:
   frontend
   API
   Entra sign-in
   Participant profile
   Manager profile

7. Activate Teams.

8. When finished:

   action         = destroy
   confirmDestroy = DELETE
```

---

## 21. Delivery status produced by this pack

After the pack is added to the repository:

```text
Azure IaC scaffold              DONE
App Service infrastructure      AUTOMATED
Azure SQL provisioning          AUTOMATED
Storage provisioning            AUTOMATED
Key Vault provisioning          AUTOMATED
Monitoring provisioning         AUTOMATED
Frontend build/deploy           AUTOMATED
API build/deploy                AUTOMATED
EF migration step               AUTOMATED
Smoke checks                    AUTOMATED
POC Azure cleanup               AUTOMATED

Entra tenant objects            EXTERNAL / ACTIVATION
Teams tenant objects            EXTERNAL / ACTIVATION
Corporate Production hosting/CI   FROZEN DESIGN UNCHANGED
```
