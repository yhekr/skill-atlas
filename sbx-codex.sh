#!/usr/bin/env bash
# Start a named worktree in sbx, using only JetBrains Central's proxy key.
set +x
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: ./sbx-codex.sh <name> [-- <Codex arguments...>]

Creates/reuses ../<name> and the branch <name>, then starts Codex in sbx.
Requires Bash, Git, jq, sbx, and an authenticated central (or jbcentral).
The sbx CLI must support: run --name NAME -e KEY=VALUE codex WORKTREE :git -- ARGS

Optional environment:
  CENTRAL_BIN         Path to the Central executable.
  CENTRAL_CONFIG      Central config file (default: ~/.jetbrains-central/config.json).
  CENTRAL_PROXY_PORT  Override the configured port (default: 19516).
  CENTRAL_PROXY_HOST  Host reachable from the sandbox (default: host.docker.internal).
USAGE
}

die() { printf 'sbx-codex: %s\n' "$*" >&2; exit 1; }
if [[ "$#" -eq 0 ]]; then usage >&2; exit 2; fi
if [[ "$1" == --help || "$1" == -h ]]; then usage; exit 0; fi
name=$1
shift
[[ "$name" =~ ^[a-z0-9][a-z0-9_-]{0,62}$ ]] ||
  die 'Use a name of 1-63 lowercase letters, digits, hyphens or underscores; start with a letter or digit.'
if [[ "$#" -gt 0 ]]; then
  [[ "$1" == -- ]] || die 'Put Codex arguments after --.'
  shift
fi

for dependency in git jq sbx; do
  command -v "$dependency" >/dev/null 2>&1 || die "Missing dependency: $dependency."
done
central_bin=${CENTRAL_BIN:-}
if [[ -z "$central_bin" ]]; then
  central_bin=$(command -v central || command -v jbcentral) ||
    die 'Install JetBrains Central CLI (central or jbcentral).'
fi
command -v "$central_bin" >/dev/null 2>&1 || die 'CENTRAL_BIN is not executable.'

proxy_host=${CENTRAL_PROXY_HOST:-host.docker.internal}
[[ "$proxy_host" =~ ^[a-zA-Z0-9][a-zA-Z0-9.-]*$ ]] ||
  die 'CENTRAL_PROXY_HOST must be a hostname or IPv4 address, without a scheme or port.'
validate_port() {
  [[ "$1" =~ ^[1-9][0-9]{0,4}$ ]] && (( 10#$1 <= 65535 ))
}
if [[ -n "${CENTRAL_PROXY_PORT:-}" ]]; then
  validate_port "$CENTRAL_PROXY_PORT" || die 'CENTRAL_PROXY_PORT must be an integer from 1 to 65535.'
fi

root=$(git rev-parse --show-toplevel 2>/dev/null) || die 'Run this script inside a Git working tree.'
root=$(cd -- "$root" && pwd -P)
worktree="$(dirname -- "$root")/$name"
[[ "$worktree" != "$root" ]] || die 'The sandbox name must differ from the current repository directory.'
common_dir() { (cd -- "$1" && cd -- "$(git rev-parse --git-common-dir)" && pwd -P); }

if [[ -e "$worktree" || -L "$worktree" ]]; then
  [[ -d "$worktree" && ! -L "$worktree" ]] || die 'The target already exists and is not a worktree directory.'
  target_root=$(git -C "$worktree" rev-parse --show-toplevel 2>/dev/null) ||
    die 'The target directory is not a Git worktree.'
  target_root=$(cd -- "$target_root" && pwd -P)
  [[ "$target_root" == "$worktree" && "$(common_dir "$worktree")" == "$(common_dir "$root")" ]] ||
    die 'The target is not a worktree of this repository.'
  target_branch=$(git -C "$worktree" symbolic-ref --quiet --short HEAD) ||
    die 'The existing worktree has a detached HEAD.'
  [[ "$target_branch" == "$name" ]] || die 'The existing worktree is on a different branch.'
else
  if git -C "$root" show-ref --verify --quiet "refs/heads/$name"; then
    git -C "$root" worktree add -- "$worktree" "$name"
  else
    git -C "$root" worktree add -b "$name" -- "$worktree" HEAD
  fi
fi

start_args=(proxy start --return-key)
proxy_help=$("$central_bin" proxy start --help)
if [[ "$proxy_help" == *--ensure-updated* ]]; then start_args+=(--ensure-updated); fi
key=$("$central_bin" "${start_args[@]}") || die 'Central proxy failed to start. Check central login.'
key=${key%$'\r'}
[[ "$key" =~ ^[a-zA-Z0-9._~-]+$ ]] || die 'Central did not return a valid proxy key.'

config=${CENTRAL_CONFIG:-"$HOME/.jetbrains-central/config.json"}
port=${CENTRAL_PROXY_PORT:-}
if [[ -z "$port" ]]; then
  if [[ -f "$config" ]]; then
    port=$(jq -er 'if type != "object" then error("Expected a config object") else .proxy_port | if . == null then 19516 else . end | tostring end' "$config") ||
      die 'Cannot read proxy_port from Central config.'
    port=${port%$'\r'}
  else
    port=19516
  fi
fi
validate_port "$port" || die 'Central proxy_port must be an integer from 1 to 65535.'
base_url="http://$proxy_host:$port/wire/$key/codex/openai/v1"

printf 'Starting Codex in %s (branch %s), via Central on %s:%s.\n' "$worktree" "$name" "$proxy_host" "$port"
# Provider overrides apply only to this Codex process. No host auth/config is copied.
# :git is the sbx integration that exposes linked-worktree Git metadata.
exec sbx run --name "$name" \
  -e "OPENAI_API_KEY=$key" \
  -e "OPENAI_BASE_URL=$base_url" \
  codex "$worktree" :git -- \
  -c 'model_provider="jetbrains_central"' \
  -c 'model_providers.jetbrains_central.name="JetBrains Central"' \
  -c "model_providers.jetbrains_central.base_url=\"$base_url\"" \
  -c 'model_providers.jetbrains_central.env_key="OPENAI_API_KEY"' \
  -c 'model_providers.jetbrains_central.wire_api="responses"' \
  -c 'model_providers.jetbrains_central.requires_openai_auth=false' \
  "$@"
