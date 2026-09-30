$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$existing = git -C $root config --get core.hooksPath
if ($LASTEXITCODE -notin @(0, 1)) { throw 'Cannot read Git hook configuration.' }
if ($existing -and $existing -ne '.githooks') {
    throw "An existing hooksPath is configured: $existing. Merge the post-commit hook manually instead of replacing it."
}
$defaultHook = git -C $root rev-parse --git-path hooks/post-commit
if (-not [IO.Path]::IsPathRooted($defaultHook)) { $defaultHook = Join-Path $root $defaultHook }
if (-not $existing -and (Test-Path -LiteralPath $defaultHook)) {
    throw 'An existing post-commit hook was found. Merge it manually before installing.'
}
git -C $root config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Could not install the repository-local Git hook.' }
Write-Output 'Installed: every commit starts a background push + GitHub CI watcher (120-second polling).'
Write-Output 'Skip one commit: set SKILL_ATLAS_SKIP_CI=1. Disable: git config --local --unset core.hooksPath'
