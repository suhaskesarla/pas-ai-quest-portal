[CmdletBinding()]
param(
  [string] $AppSettingsPath = 'src/api/appsettings.json'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $AppSettingsPath)) {
  throw "File not found: $AppSettingsPath"
}

$json = Get-Content $AppSettingsPath -Raw | ConvertFrom-Json

function Walk {
  param(
    [object] $Value,
    [string] $Prefix
  )

  if ($null -eq $Value) {
    return
  }

  $properties = $Value.PSObject.Properties
  if ($properties.Count -eq 0) {
    return
  }

  foreach ($property in $properties) {
    $path = if ($Prefix) { "$Prefix`__$($property.Name)" } else { $property.Name }
    $childProperties = $property.Value.PSObject.Properties

    if ($null -ne $property.Value -and $childProperties.Count -gt 0 -and $property.Value -isnot [string]) {
      Walk -Value $property.Value -Prefix $path
    }
    else {
      Write-Output $path
    }
  }
}

Write-Host "Candidate ASP.NET Core environment-variable keys:"
Walk -Value $json -Prefix ''
