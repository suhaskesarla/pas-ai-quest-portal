[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $ResourceGroupName,
  [Parameter(Mandatory)] [string] $SqlServerName,
  [Parameter(Mandatory)] [string] $SqlDatabaseName,
  [Parameter(Mandatory)] [string] $SqlAdminLogin,
  [Parameter(Mandatory)] [string] $ProjectPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:SQL_ADMIN_PASSWORD)) {
  throw 'SQL_ADMIN_PASSWORD must be supplied as a secret environment variable.'
}

if (-not (Test-Path $ProjectPath)) {
  throw "EF project not found: $ProjectPath"
}

$ruleName = "ado-agent-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
$agentIp = $null

try {
  # Hosted Azure DevOps agents have changing outbound IPs.
  # For a POC we open only the current agent IP temporarily.
  $agentIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').ToString().Trim()

  if ($agentIp -notmatch '^\d{1,3}(\.\d{1,3}){3}$') {
    throw "Could not determine a valid IPv4 address for the build agent."
  }

  Write-Host "Creating temporary SQL firewall rule for the current build agent."
  az sql server firewall-rule create `
    --resource-group $ResourceGroupName `
    --server $SqlServerName `
    --name $ruleName `
    --start-ip-address $agentIp `
    --end-ip-address $agentIp `
    --output none

  $connectionString = "Server=tcp:$SqlServerName.database.windows.net,1433;Initial Catalog=$SqlDatabaseName;Persist Security Info=False;User ID=$SqlAdminLogin;Password=$($env:SQL_ADMIN_PASSWORD);MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  $env:ConnectionStrings__QuestDatabase = $connectionString

  $toolPath = Join-Path $env:AGENT_TEMPDIRECTORY 'dotnet-tools'
  dotnet tool install --tool-path $toolPath dotnet-ef --version '8.*'
  if ($LASTEXITCODE -ne 0) { throw 'Could not install dotnet-ef 8.x.' }
  & (Join-Path $toolPath 'dotnet-ef') database update `
    --project $ProjectPath `
    --startup-project $ProjectPath `
    --connection $connectionString `
    --configuration Release `
    --no-build

  if ($LASTEXITCODE -ne 0) {
    throw "EF Core database migration failed."
  }
}
finally {
  if ($agentIp) {
    Write-Host "Removing temporary SQL firewall rule."
    az sql server firewall-rule delete `
      --resource-group $ResourceGroupName `
      --server $SqlServerName `
      --name $ruleName `
      --output none 2>$null
  }
}
