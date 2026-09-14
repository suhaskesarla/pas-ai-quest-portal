[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$providers = @(
  'Microsoft.Web',
  'Microsoft.Sql',
  'Microsoft.Storage',
  'Microsoft.KeyVault',
  'Microsoft.Insights',
  'Microsoft.OperationalInsights'
)

foreach ($provider in $providers) {
  Write-Host "Registering $provider ..."
  az provider register --namespace $provider --wait --only-show-errors | Out-Null
}

Write-Host "Required Azure resource providers are registered."
