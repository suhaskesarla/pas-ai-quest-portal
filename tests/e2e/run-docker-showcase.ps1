param([string]$PlaywrightConfig = 'docker-showcase.config.ts')

$ErrorActionPreference = 'Stop'

$runId = if ($env:QA_RUN_ID) { $env:QA_RUN_ID } else { (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') }
$e2eRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $e2eRoot '../..')).Path
$runRoot = Join-Path $repoRoot "tests/reports/$runId"
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$env:QA_RUN_ID = $runId
$env:QA_RUN_ROOT = $runRoot

function Write-StartupFailure([string]$message) {
  $sha = (git -C $repoRoot rev-parse HEAD).Trim()
  @(
    "timestamp=$((Get-Date).ToUniversalTime().ToString('o'))"
    "git_commit=$sha"
    'test_mode=Docker Compose'
    'base_url=http://localhost:5173'
    'tests_passed=0'
    'tests_failed=1'
    'clean_database=true'
    'fixture_data_used=false'
    "docker_health=$message"
    'screenshots=[]'
    'final_result=failed'
  ) | Set-Content -LiteralPath (Join-Path $runRoot 'summary.txt')
}

Push-Location $repoRoot
try {
  docker compose down -v
  docker compose up --build -d
  $deadline = (Get-Date).AddMinutes(4)
  $ready = $false
  do {
    try {
      $root = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5173' -TimeoutSec 5
      $profiles = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5173/api/auth/demo/profiles' -TimeoutSec 5
      $me = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:5173/api/auth/me' -TimeoutSec 5
      $ready = $root.StatusCode -eq 200 -and $profiles.StatusCode -eq 200 -and $me.StatusCode -eq 200
    } catch { Start-Sleep -Seconds 3 }
  } until ($ready -or (Get-Date) -ge $deadline)
  docker compose ps | Set-Content -LiteralPath (Join-Path $runRoot 'docker-health.txt')
  if (-not $ready) {
    Write-StartupFailure 'Compose services did not expose web/auth readiness within four minutes.'
    exit 1
  }
} catch {
  docker compose ps | Set-Content -LiteralPath (Join-Path $runRoot 'docker-health.txt')
  Write-StartupFailure $_.Exception.Message
  exit 1
} finally {
  Pop-Location
}

Push-Location $e2eRoot
try {
  npx playwright test --config $PlaywrightConfig
  exit $LASTEXITCODE
} finally {
  Pop-Location
}
