[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $ResourceGroupName,
  [Parameter(Mandatory)] [string] $KeyVaultName,
  [Parameter(Mandatory)] [string] $Confirm
)

$ErrorActionPreference = 'Stop'

if ($Confirm -cne 'DELETE') {
  throw "Destroy refused. Queue the pipeline with confirmDestroy=DELETE."
}

$exists = az group exists --name $ResourceGroupName
if ($exists -ne 'true') {
  Write-Host "Resource group $ResourceGroupName does not exist. Nothing to delete."
  exit 0
}

Write-Host "Deleting POC resource group: $ResourceGroupName"
az group delete --name $ResourceGroupName --yes --only-show-errors

# Key Vault soft-delete survives resource-group deletion.
# Purge allows the deterministic POC vault name to be reused.
# This may fail if the caller lacks Key Vault purge permission.
try {
  Write-Host "Attempting to purge soft-deleted Key Vault $KeyVaultName."
  az keyvault purge --name $KeyVaultName --only-show-errors
}
catch {
  Write-Warning "Key Vault purge was not permitted. Azure resources were deleted, but the vault name may remain reserved until its soft-delete retention expires or an admin purges it."
}

Write-Host ""
Write-Host "POC Azure resource cleanup complete."
Write-Host "NOTE: Entra app registrations and Teams tenant registrations are NOT resource-group resources and were not deleted."
