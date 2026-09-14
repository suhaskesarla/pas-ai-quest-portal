[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $WebUrl,
  [Parameter(Mandatory)] [string] $ApiUrl
)

$ErrorActionPreference = 'Stop'

function Wait-Http {
  param(
    [string] $Url,
    [int[]] $AcceptedStatus,
    [int] $Attempts = 12,
    [int] $DelaySeconds = 10
  )

  for ($i = 1; $i -le $Attempts; $i++) {
    try {
      $response = Invoke-WebRequest -Uri $Url -MaximumRedirection 0 -SkipHttpErrorCheck -TimeoutSec 20
      if ($AcceptedStatus -contains [int]$response.StatusCode) {
        Write-Host "PASS $Url -> $($response.StatusCode)"
        return
      }

      Write-Host "Attempt ${i}: $Url -> $($response.StatusCode)"
    }
    catch {
      Write-Host "Attempt ${i}: $Url -> $($_.Exception.Message)"
    }

    Start-Sleep -Seconds $DelaySeconds
  }

  throw "Smoke check failed for $Url"
}

# Frontend should return a normal page.
Wait-Http -Url $WebUrl -AcceptedStatus @(200)

# The API root may legitimately be 200, 401, 403 or 404 depending on routing.
# The check proves the App Service is reachable and not returning a platform 5xx.
Wait-Http -Url $ApiUrl -AcceptedStatus @(200, 401, 403, 404)

Write-Host "Azure POC smoke checks passed."
