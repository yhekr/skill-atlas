[CmdletBinding()]
param(
    [switch]$Background,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,
    [string]$Branch,
    [ValidateRange(3, 180)][int]$TimeoutMinutes = 45
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. "$PSScriptRoot/Ci.Common.ps1"
$root = Split-Path $PSScriptRoot -Parent
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'Never'
$lock = $null
$statePath = $null

try {
    if (-not $Commit) {
        $Commit = git -C $root rev-parse --verify HEAD
        if ($LASTEXITCODE -ne 0) { throw 'No commit exists yet.' }
    }
    if (-not $Branch) {
        $Branch = git -C $root symbolic-ref --quiet --short HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Detached HEAD: switch to a branch before starting CI.' }
    }
    git -C $root check-ref-format "refs/heads/$Branch" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Invalid branch name.' }
    $remotes = @(git -C $root remote get-url --push --all origin)
    if ($LASTEXITCODE -ne 0 -or $remotes.Count -ne 1) { throw 'Exactly one origin push URL is required.' }
    $repository = Get-CiRepository -RemoteUrl $remotes[0]
    $gitDirectory = git -C $root rev-parse --absolute-git-dir
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the Git directory.' }
    $logDirectory = Join-Path $gitDirectory 'ci-watch'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $statePath = Join-Path $logDirectory "$Commit.json"

    if ($Background) {
        # Encode arguments as PowerShell literals, including paths with spaces or quotes.
        $scriptLiteral = "'" + (Join-Path $PSScriptRoot 'Run-CI.ps1').Replace("'", "''") + "'"
        $branchLiteral = "'" + $Branch.Replace("'", "''") + "'"
        $command = "& $scriptLiteral -Commit '$Commit' -Branch $branchLiteral -TimeoutMinutes $TimeoutMinutes"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $logBase = Join-Path $logDirectory ("{0}-{1}" -f $Commit, [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $start = @{
            FilePath = (Get-Process -Id $PID).Path
            ArgumentList = @('-NoProfile', '-NonInteractive', '-OutputFormat', 'Text', '-EncodedCommand', $encoded)
            WorkingDirectory = $root
            RedirectStandardOutput = "$logBase.log"
            RedirectStandardError = "$logBase.err.log"
            PassThru = $true
        }
        if ($env:OS -eq 'Windows_NT') { $start.WindowStyle = 'Hidden' }
        else {
            # nohup keeps the watcher alive when Git's hook shell exits on Unix.
            $start.ArgumentList = @($start.FilePath) + $start.ArgumentList
            $start.FilePath = 'nohup'
        }
        $process = Start-Process @start
        Write-Output "CI watcher started (PID $($process.Id)). Poll interval: 120 seconds."
        Write-Output "Status: $statePath"
        Write-Output "Log: $logBase.log"
        exit 0
    }

    try {
        $lock = [IO.File]::Open((Join-Path $logDirectory "$Commit.lock"), 'OpenOrCreate', 'ReadWrite', 'None')
    }
    catch [IO.IOException] {
        Write-Output "A CI watcher is already running for $Commit."
        exit 0
    }
    Write-CiState -Path $statePath -Commit $Commit -Branch $Branch -Status 'pushing' -Message 'Publishing the commit to GitHub.'
    Write-Output "Pushing $Commit to $repository / $Branch (no force push)."
    # Normalize SSH origins to HTTPS so Git Credential Manager can authenticate.
    # Push the captured SHA, not a branch that may have changed since the hook started.
    $ErrorActionPreference = 'Continue'
    git -C $root -c 'url.https://github.com/.insteadOf=git@github.com:' -c 'url.https://github.com/.insteadOf=ssh://git@github.com/' push -- origin "${Commit}:refs/heads/$Branch" 2>&1 | ForEach-Object { Write-Output "$_" }
    $pushExitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($pushExitCode -ne 0) { throw 'Push failed. CI was not started; the local commit is preserved.' }
    Wait-CiRun -Repository $repository -Commit $Commit -Branch $Branch -StatePath $statePath -TimeoutMinutes $TimeoutMinutes
    exit 0
}
catch {
    $message = $_.Exception.Message
    if ($null -ne $statePath -and $null -ne $lock) {
        # Preserve the GitHub result/URL on failure or cancellation.
        $lastState = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
        $conclusion = ''
        $url = ''
        if ($null -ne $lastState) { $conclusion = $lastState.conclusion; $url = $lastState.url }
        Write-CiState -Path $statePath -Commit $Commit -Branch $Branch -Status 'error' -Conclusion $conclusion -Url $url -Message $message
    }
    [Console]::Error.WriteLine("CI watcher: $message")
    exit 1
}
finally {
    if ($null -ne $lock) { $lock.Dispose() }
}
