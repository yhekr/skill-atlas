Set-StrictMode -Version Latest

function Get-CiRepository {
    param([string]$RemoteUrl)
    if ($RemoteUrl -notmatch '^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?/?$') {
        throw 'origin must point to a GitHub.com repository using HTTPS or SSH.'
    }
    return $Matches[1]
}

function Get-CiToken {
    if ($env:GH_TOKEN) { return $env:GH_TOKEN }
    if ($env:GITHUB_TOKEN) { return $env:GITHUB_TOKEN }
    $lines = @('protocol=https', 'host=github.com', '', '') -join [char]10
    $credential = @($lines | git credential fill 2>$null)
    if ($LASTEXITCODE -ne 0) { throw 'GitHub HTTPS credentials are unavailable. Sign in through Git Credential Manager.' }
    foreach ($line in $credential) {
        if ($line.StartsWith('password=')) { return $line.Substring(9) }
    }
    throw 'GitHub HTTPS credentials did not contain a token.'
}

function Invoke-CiApi {
    param([string]$Path, [string]$Method = 'GET', [object]$Body)
    $request = @{
        Uri = "https://api.github.com/$Path"
        Method = $Method
        TimeoutSec = 30
        Headers = @{
            Authorization = "Bearer $(Get-CiToken)"
            Accept = 'application/vnd.github+json'
            'X-GitHub-Api-Version' = '2022-11-28'
            'User-Agent' = 'SkillAtlas-CI-watcher'
        }
    }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json'
        $request.Body = $Body | ConvertTo-Json -Depth 5 -Compress
    }
    try { Invoke-RestMethod @request }
    catch {
        # Do not include request headers, credentials, or response bodies in logs.
        $status = 'network error'
        if ($_.Exception.PSObject.Properties['Response'] -and $null -ne $_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }
        throw "GitHub API failed ($status). Check connectivity and Actions permissions."
    }
}

function Get-CiUtcNow { return [DateTime]::UtcNow }
function Wait-CiPoll {
    param([int]$Seconds)
    Start-Sleep -Seconds $Seconds
}

function Write-CiState {
    param([string]$Path, [string]$Commit, [string]$Branch, [string]$Status,
        [string]$Conclusion, [string]$Url, [string]$Message)
    $state = [ordered]@{
        commit = $Commit
        branch = $Branch
        status = $Status
        conclusion = $Conclusion
        url = $Url
        message = $Message
        updatedAt = (Get-CiUtcNow).ToString('o')
        processId = $PID
    }
    $temporaryPath = "$Path.tmp"
    $state | ConvertTo-Json | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Find-CiRun {
    param([object[]]$Runs, [string]$Commit, [string]$Branch)
    return $Runs | Where-Object {
        $_.head_sha -eq $Commit -and $_.head_branch -eq $Branch -and
        $_.event -in @('push', 'workflow_dispatch')
    } | Sort-Object id -Descending | Select-Object -First 1
}

function Wait-CiRun {
    param([string]$Repository, [string]$Commit, [string]$Branch, [string]$StatePath,
        [int]$TimeoutMinutes = 45, [int]$PollSeconds = 120)
    $deadline = (Get-CiUtcNow).AddMinutes($TimeoutMinutes)
    $runId = $null
    $emptyPolls = 0
    $dispatched = $false
    $url = ''
    while ((Get-CiUtcNow) -lt $deadline) {
        if ($null -eq $runId) {
            $response = Invoke-CiApi -Path "repos/$Repository/actions/workflows/ci.yml/runs?head_sha=$Commit&per_page=100"
            $run = Find-CiRun -Runs @($response.workflow_runs) -Commit $Commit -Branch $Branch
        }
        else {
            $run = Invoke-CiApi -Path "repos/$Repository/actions/runs/$runId"
        }
        if ($null -ne $run) {
            if ($run.head_sha -ne $Commit -or $run.head_branch -ne $Branch) {
                throw 'GitHub returned a run for a different commit or branch.'
            }
            $runId = $run.id
            $url = $run.html_url
            $message = "CI $($run.status) / $($run.conclusion): $url"
            Write-Output "[$((Get-CiUtcNow).ToString('u'))] $message"
            Write-CiState -Path $StatePath -Commit $Commit -Branch $Branch -Status $run.status -Conclusion $run.conclusion -Url $url -Message $message
            if ($run.status -eq 'completed') {
                if ($run.conclusion -eq 'success') { return }
                throw "CI finished with conclusion '$($run.conclusion)': $url"
            }
        }
        else {
            $emptyPolls++
            # Allow the push event two minutes to register before falling back to a dispatch.
            if ($emptyPolls -ge 2 -and -not $dispatched) {
                $encodedBranch = [Uri]::EscapeDataString($Branch)
                $remoteRef = Invoke-CiApi -Path "repos/$Repository/git/ref/heads/$encodedBranch"
                if ($remoteRef.object.sha -ne $Commit) {
                    throw 'The remote branch has moved. Refusing to dispatch CI for a different commit.'
                }
                Invoke-CiApi -Path "repos/$Repository/actions/workflows/ci.yml/dispatches" -Method POST -Body @{ ref = $Branch } | Out-Null
                $dispatched = $true
                Write-Output 'Requested workflow_dispatch for the published branch.'
            }
            Write-Output "[$((Get-CiUtcNow).ToString('u'))] Waiting for CI for $Commit."
            Write-CiState -Path $StatePath -Commit $Commit -Branch $Branch -Status 'waiting' -Message 'Waiting for GitHub to register the workflow run.'
        }
        $remaining = [int][Math]::Ceiling(($deadline - (Get-CiUtcNow)).TotalSeconds)
        if ($remaining -le 0) { break }
        Wait-CiPoll -Seconds ([Math]::Min($PollSeconds, $remaining))
    }
    throw "Timed out waiting for CI after $TimeoutMinutes minutes. Last run: $url"
}
