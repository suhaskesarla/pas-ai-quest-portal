[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $ResourceGroupName,
  [Parameter(Mandatory)] [string] $ApiAppName,
  [Parameter(Mandatory)] [string] $WebAppName,
  [Parameter(Mandatory)] [string] $SqlServerName,
  [Parameter(Mandatory)] [string] $SqlDatabaseName,
  [Parameter(Mandatory)] [string] $SqlAdminLogin,
  [Parameter(Mandatory)] [string] $SqlConnectionStringSetting,
  [Parameter(Mandatory)] [string] $StorageAccountName,
  [Parameter(Mandatory)] [string] $AuthModeSetting,
  [Parameter(Mandatory)] [string] $TenantSetting,
  [Parameter(Mandatory)] [string] $AudienceSetting,
  [Parameter(Mandatory)] [string] $ScopeSetting,
  [Parameter(Mandatory)] [string] $StorageConnectionSetting,
  [Parameter(Mandatory)] [string] $PortalBaseUrlSetting,
  [Parameter(Mandatory)] [string] $EntraTenantId,
  [Parameter(Mandatory)] [string] $EntraApiAudience,
  [Parameter(Mandatory)] [string] $EntraApiScopeName
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:SQL_ADMIN_PASSWORD)) {
  throw 'SQL_ADMIN_PASSWORD must be supplied as a secret environment variable.'
}

if ([string]::IsNullOrWhiteSpace($EntraTenantId) -or
    [string]::IsNullOrWhiteSpace($EntraApiAudience) -or
    [string]::IsNullOrWhiteSpace($EntraApiScopeName)) {
  throw 'ENTRA_TENANT_ID, ENTRA_API_AUDIENCE and ENTRA_API_SCOPE_NAME must be configured before a Production/Entra deployment.'
}

$portalUrl = "https://$WebAppName.azurewebsites.net"
$apiUrl = "https://$ApiAppName.azurewebsites.net"

# --------------------------------------------------------------------
# SQL
# --------------------------------------------------------------------
$sqlConnectionString = "Server=tcp:$SqlServerName.database.windows.net,1433;Initial Catalog=$SqlDatabaseName;Persist Security Info=False;User ID=$SqlAdminLogin;Password=$($env:SQL_ADMIN_PASSWORD);MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

# --------------------------------------------------------------------
# Storage
# POC uses a shared key. Production should prefer Managed Identity.
# --------------------------------------------------------------------
$storageConnectionString = az storage account show-connection-string `
  --resource-group $ResourceGroupName `
  --name $StorageAccountName `
  --query connectionString `
  --output tsv

if ([string]::IsNullOrWhiteSpace($storageConnectionString)) {
  throw "Could not obtain Storage connection string for $StorageAccountName."
}

# --------------------------------------------------------------------
# PAS application configuration
#
# Setting names are supplied by the repository-aligned pipeline defaults.
# --------------------------------------------------------------------
$settings = @(
  "$AuthModeSetting=Entra",
  "$TenantSetting=$EntraTenantId",
  "$AudienceSetting=$EntraApiAudience",
  "$ScopeSetting=$EntraApiScopeName",
  "$SqlConnectionStringSetting=$sqlConnectionString",
  "$StorageConnectionSetting=$storageConnectionString",
  "$PortalBaseUrlSetting=$portalUrl",
  "Cors__AllowedOrigins__0=$portalUrl"
)

az webapp config appsettings set `
  --resource-group $ResourceGroupName `
  --name $ApiAppName `
  --settings $settings `
  --output none

# The SPA calls relative /api paths; the web App Service proxies those
# requests to the API so browser authorization remains same-origin.
az webapp config appsettings set `
  --resource-group $ResourceGroupName `
  --name $WebAppName `
  --settings "PAS_API_ORIGIN=$apiUrl" `
  --output none

Write-Host "Configured API: $apiUrl"
Write-Host "Web /api proxy target: $apiUrl"
Write-Host "Secrets were supplied through pipeline environment/configuration, not source control."
