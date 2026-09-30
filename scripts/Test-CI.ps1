# Deterministic tests: no network, credentials, Git pushes, or real sleeps.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Ci.Common.ps1"
$script:passed = 0
$script:failed = 0
$script:sha = 'a' * 40
$script:otherSha = 'b' * 40
$testDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/ci-script-tests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$script:statePath = Join-Path $testDirectory 'state.json'

function Assert-Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "Expected '$Expected', got '$Actual'." }
}
function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    if (-not $caught -or $caught -notlike $Pattern) { throw "Expected error '$Pattern', got '$caught'." }
}
function Test-Case([string]$Name, [scriptblock]$Action) {
    $script:clock = [DateTime]::new(2026, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)
    $script:sleeps = [Collections.Generic.List[int]]::new()
    $script:calls = [Collections.Generic.List[object]]::new()
    $script:responses = [Collections.Generic.Queue[object]]::new()
    try {
        & $Action
        $script:passed++
        Write-Output "PASS $Name"
    }
    catch {
        $script:failed++
        Write-Output "FAIL $Name : $($_.Exception.Message)"
    }
}
function New-Run([string]$Status = 'completed', [string]$Conclusion = 'success',
    [long]$Id = 10, [string]$Commit = $script:sha, [string]$Branch = 'main', [string]$Event = 'push') {
    return [pscustomobject]@{
        id = $Id; head_sha = $Commit; head_branch = $Branch; event = $Event
        status = $Status; conclusion = $Conclusion; html_url = "https://github.com/example/repo/actions/runs/$Id"
    }
}
function Add-Page([object[]]$Runs = @()) { $script:responses.Enqueue(@{ workflow_runs = $Runs }) }
function Get-CiUtcNow { return $script:clock }
function Wait-CiPoll([int]$Seconds) {
    $script:sleeps.Add($Seconds)
    $script:clock = $script:clock.AddSeconds($Seconds)
}
function Invoke-CiApi([string]$Path, [string]$Method = 'GET', [object]$Body) {
    $script:calls.Add(@{ Path = $Path; Method = $Method; Body = $Body })
    if ($script:responses.Count -eq 0) { throw 'Unexpected API call.' }
    $response = $script:responses.Dequeue()
    if ($response -is [Exception]) { throw $response }
    return $response
}
function Watch-Test([string]$Branch = 'main', [int]$TimeoutMinutes = 45) {
    Wait-CiRun -Repository 'example/repo' -Commit $script:sha -Branch $Branch -StatePath $script:statePath -TimeoutMinutes $TimeoutMinutes | Out-Null
}

foreach ($url in @('git@github.com:example/repo.git', 'https://github.com/example/repo', 'https://github.com/example/repo.git/', 'ssh://git@github.com/example/repo.git')) {
    Test-Case "GitHub remote: $url" { Assert-Equal (Get-CiRepository $url) 'example/repo' }
}
foreach ($url in @('https://example.com/owner/repo', 'https://token@github.com/owner/repo', 'https://github.com/owner/repo/tree/main', 'https://github.com/owner/repo?token=x')) {
    Test-Case "Reject unsafe/unsupported remote: $url" { Assert-Throws { Get-CiRepository $url } '*must point to a GitHub.com*' }
}
Test-Case 'Match exact SHA, branch and workflow event; prefer latest run' {
    $runs = @(
        (New-Run -Id 10), (New-Run -Id 11), (New-Run -Id 12 -Commit $script:otherSha),
        (New-Run -Id 13 -Branch 'other'), (New-Run -Id 14 -Event 'pull_request')
    )
    Assert-Equal (Find-CiRun $runs $script:sha 'main').id 11
}
Test-Case 'Poll queued -> running -> success every 120 seconds and pin run ID' {
    Add-Page @((New-Run -Status 'queued' -Conclusion ''))
    $script:responses.Enqueue((New-Run -Status 'in_progress' -Conclusion ''))
    $script:responses.Enqueue((New-Run))
    Watch-Test
    Assert-Equal ($script:sleeps -join ',') '120,120'
    Assert-Equal $script:calls[0].Path "repos/example/repo/actions/workflows/ci.yml/runs?head_sha=$script:sha&per_page=100"
    Assert-Equal $script:calls[1].Path 'repos/example/repo/actions/runs/10'
    $state = Get-Content -LiteralPath $script:statePath -Raw | ConvertFrom-Json
    Assert-Equal $state.status 'completed'
    Assert-Equal $state.conclusion 'success'
    Assert-Equal $state.commit $script:sha
}
foreach ($conclusion in @('failure', 'cancelled', 'timed_out', 'skipped', 'neutral', 'action_required')) {
    Test-Case "Non-success conclusion fails: $conclusion" {
        Add-Page @((New-Run -Conclusion $conclusion))
        Assert-Throws { Watch-Test } "*conclusion '$conclusion'*"
        Assert-Equal $script:sleeps.Count 0
    }
}
Test-Case 'Delayed push run appears without duplicate workflow_dispatch' {
    Add-Page
    Add-Page @((New-Run))
    Watch-Test
    Assert-Equal ($script:sleeps -join ',') '120'
    Assert-Equal @($script:calls | Where-Object Method -eq 'POST').Count 0
}
Test-Case 'Missing push run dispatches once after two minutes; encodes branch path' {
    Add-Page
    Add-Page
    $script:responses.Enqueue(@{ object = @{ sha = $script:sha } })
    $script:responses.Enqueue($null)
    Add-Page @((New-Run -Branch 'feature/ci' -Event 'workflow_dispatch'))
    Watch-Test -Branch 'feature/ci'
    Assert-Equal ($script:sleeps -join ',') '120,120'
    Assert-Equal $script:calls[2].Path 'repos/example/repo/git/ref/heads/feature%2Fci'
    Assert-Equal $script:calls[3].Method 'POST'
    Assert-Equal $script:calls[3].Body.ref 'feature/ci'
    Assert-Equal @($script:calls | Where-Object Method -eq 'POST').Count 1
}
Test-Case 'Do not dispatch a newer commit when the remote branch moves' {
    Add-Page
    Add-Page
    $script:responses.Enqueue(@{ object = @{ sha = $script:otherSha } })
    Assert-Throws { Watch-Test } '*remote branch has moved*'
    Assert-Equal @($script:calls | Where-Object Method -eq 'POST').Count 0
}
Test-Case 'Never report another commit as successful' {
    Add-Page @((New-Run -Status 'queued' -Conclusion ''))
    $script:responses.Enqueue((New-Run -Commit $script:otherSha))
    Assert-Throws { Watch-Test } '*different commit or branch*'
}
Test-Case 'Timeout is bounded even when GitHub never completes' {
    Add-Page @((New-Run -Status 'queued' -Conclusion ''))
    $script:responses.Enqueue((New-Run -Status 'queued' -Conclusion ''))
    Assert-Throws { Watch-Test -TimeoutMinutes 3 } '*Timed out*'
    Assert-Equal ($script:sleeps -join ',') '120,60'
    Assert-Equal $script:clock.Minute 3
}
Test-Case 'API failures propagate instead of becoming a green result' {
    $script:responses.Enqueue([Exception]::new('GitHub API failed (403).'))
    Assert-Throws { Watch-Test } '*403*'
}
Test-Case 'An already completed run returns immediately' {
    Add-Page @((New-Run))
    Watch-Test
    Assert-Equal $script:sleeps.Count 0
    Assert-Equal $script:calls.Count 1
}

Write-Output "$script:passed passed; $script:failed failed."
if ($script:failed -gt 0) { exit 1 }
