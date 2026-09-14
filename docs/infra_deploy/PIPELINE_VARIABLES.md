# Azure DevOps pipeline variables

## Required secret

| Variable | Secret? | Purpose |
|---|---:|---|
| `SQL_ADMIN_PASSWORD` | Yes | POC SQL admin password used for provisioning, migrations and API connection |

## Required for real Entra deployment

| Variable | Secret? | Purpose |
|---|---:|---|
| `ENTRA_TENANT_ID` | No | Corporate/test tenant ID |
| `ENTRA_SPA_CLIENT_ID` | No | SPA app registration client ID |
| `ENTRA_API_AUDIENCE` | No | PAS API audience / API app ID URI as expected by backend |
| `ENTRA_API_SCOPE` | No | Full delegated scope URI requested by the SPA |
| `ENTRA_API_SCOPE_NAME` | No | Exact delegated scope name required in the API token's `scp` claim (for example `access_as_user`) |

These values can be held in an Azure DevOps variable group.

## Queue-time pipeline parameters

Important parameters:

| Parameter | Example |
|---|---|
| `action` | `full` |
| `azureServiceConnection` | `sc-pas-ai-quest-poc` |
| `environmentName` | `poc` |
| `location` | `australiaeast` |
| `nameSuffix` | `suhas01` |
| `appServiceSku` | `F1` |
| `useFreeSql` | `true` |
| `runTests` | `true` |
| `runMigrations` | `true` |

## Application-setting compatibility parameters

Verify these defaults against the repository:

```text
apiAuthModeSetting       = Authentication__Mode
apiTenantSetting         = Authentication__Entra__TenantId
apiAudienceSetting       = Authentication__Entra__Audience
apiScopeSetting          = Authentication__Entra__RequiredScope
storageConnectionSetting = Storage__ConnectionString
portalBaseUrlSetting     = Notifications__PortalBaseUrl
sqlConnectionStringSetting = ConnectionStrings__QuestDatabase
```

The web App Service receives `PAS_API_ORIGIN=https://<API app>.azurewebsites.net`; its static server proxies relative `/api` requests to the API. The frontend build sets `VITE_APP_ENVIRONMENT=Production` and the four `VITE_ENTRA_*` values from the Entra branch.

## Do not add

Never create frontend variables containing:

```text
client secret
Bot secret
SQL password without secret masking
access token
refresh token
```

The SPA has no client secret.
