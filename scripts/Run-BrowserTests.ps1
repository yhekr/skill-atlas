param([ValidateSet('test', 'update', 'prove')][string]$Mode = 'test')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Mode -eq 'update' -and $env:CI) { throw 'Baseline updates are a local review action, not a CI action.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    Get-Command docker -ErrorAction Stop | Out-Null
    $artifacts = Join-Path $repoRoot 'artifacts/e2e'
    $snapshots = Join-Path $repoRoot 'tests/e2e/snapshots'
    New-Item -ItemType Directory -Force -Path $artifacts, $snapshots | Out-Null
    docker build -f Dockerfile.e2e -t skill-atlas-e2e:local .
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the browser test image.' }
    $script = switch ($Mode) {
        'test' { 'test:e2e' }
        'update' { 'test:e2e:update' }
        'prove' { 'test:e2e:prove' }
    }
    $snapshotMount = "type=bind,source=$snapshots,target=/work/tests/e2e/snapshots"
    if ($Mode -ne 'update') { $snapshotMount += ',readonly' }
    # No network during tests: scanner API is fixture-backed; assets are local.
    docker run --rm --init --network none --shm-size=1gb `
        --mount "type=bind,source=$artifacts,target=/work/artifacts/e2e" `
        --mount $snapshotMount `
        -e CI=true skill-atlas-e2e:local npm run $script
    if ($LASTEXITCODE -ne 0) { throw "Browser $Mode failed. Inspect artifacts/e2e." }
} finally { Pop-Location }
