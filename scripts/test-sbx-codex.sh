#!/usr/bin/env bash
# Real Git/worktrees and jq; Central/sbx are isolated stubs. No model requests.
set -euo pipefail
repo=$(cd -- "$(dirname -- "$0")/.." && pwd -P)
launcher="$repo/sbx-codex.sh"
command -v jq >/dev/null || { echo 'Tests require jq.' >&2; exit 1; }
mkdir -p "$repo/artifacts"
test_root=$(mktemp -d "$repo/artifacts/sbx-tests.XXXXXX")
test_root=$(cd -- "$test_root" && pwd -P)
cleanup() {
  # Only remove this generated directory inside this repository's artifacts.
  case "$test_root" in
    "$repo"/artifacts/sbx-tests.*) rm -rf -- "$test_root" ;;
    *) printf 'Refusing cleanup outside the test directory: %s\n' "$test_root" >&2 ;;
  esac
}
trap cleanup EXIT
mkdir -p "$test_root/bin" "$test_root/no-hooks"
cat > "$test_root/bin/central" <<'STUB'
#!/usr/bin/env bash
set -eu
printf '%s\n' "$*" >> "$CENTRAL_TEST_CAPTURE"
if [[ "$*" == 'proxy start --help' ]]; then
  printf '%s\n' "--return-key ${MOCK_UPDATE_FLAG:-}"
else
  [[ "$*" == 'proxy start --return-key' || "$*" == 'proxy start --return-key --ensure-updated' ]] || exit 99
  [[ "${MOCK_CENTRAL_FAIL:-0}" == 0 ]] || exit 42
  printf '%s\n' "$MOCK_KEY"
fi
STUB
cat > "$test_root/bin/sbx" <<'STUB'
#!/usr/bin/env bash
set -eu
printf '%s\n' "$*" >> "$SBX_TEST_CALLS"
case "$1" in
  ls)
    [[ "${MOCK_LIST_FAIL:-0}" == 0 ]] || exit 23
    if [[ -n "${MOCK_LIST_JSON:-}" ]]; then printf '%s\n' "$MOCK_LIST_JSON"
    elif [[ -f "$SBX_TEST_STATE" ]]; then cat "$SBX_TEST_STATE"
    else printf '{"sandboxes":[]}\n'; fi
    exit 0 ;;
  create)
    printf '%s\0' "$@" > "$SBX_TEST_CREATE_CAPTURE"
    [[ "${MOCK_CREATE_EXIT:-0}" == 0 ]] || exit "$MOCK_CREATE_EXIT"
    args=("$@")
    jq -n --arg name "${args[2]}" --arg tree "${args[12]}" --arg common "${args[13]}" \
      '{sandboxes:[{name:$name,agent:"codex",workspaces:[$tree,$common]}]}' > "$SBX_TEST_STATE"
    exit 0 ;;
  policy)
    printf '%s\n' "$*" >> "$SBX_TEST_POLICY_CAPTURE"
    if [[ "$2" == allow ]]; then
      [[ "${MOCK_ALLOW_EXIT:-0}" == 0 ]] || exit "$MOCK_ALLOW_EXIT"
      if [[ "${MOCK_STILL_DENIED:-0}" == 0 ]]; then printf '%s\n' "$6" >> "$SBX_TEST_POLICY_STATE"; fi
      exit 0
    fi
    [[ "${MOCK_POLICY_CHECK_FAIL:-0}" == 0 ]] || exit "$MOCK_POLICY_CHECK_FAIL"
    if [[ -n "${MOCK_POLICY_JSON:-}" ]]; then printf '%s\n' "$MOCK_POLICY_JSON"; exit 0; fi
    if [[ -f "$SBX_TEST_POLICY_STATE" ]] && grep -Fxq "$6" "$SBX_TEST_POLICY_STATE"; then
      printf '{"allowed":true}\n'; exit 0
    fi
    jq -n --arg kind "${MOCK_DENY_KIND:-implicit}" '{allowed:false,deny_kind:$kind}'
    exit 1 ;;
  run) ;;
  *) exit 99 ;;
esac
printf '%s\0' "$@" > "$SBX_TEST_CAPTURE"
if [[ -n "${SBX_TEST_NATIVE_PROBE:-}" ]]; then
  powershell.exe -NoProfile -NonInteractive -File "$SBX_TEST_NATIVE_PROBE" "$@" > "$SBX_TEST_NATIVE_CAPTURE"
fi
exit "${MOCK_SBX_EXIT:-0}"
STUB
chmod +x "$test_root/bin/central" "$test_root/bin/sbx"
export PATH="$test_root/bin:$PATH"
export CENTRAL_BIN="$test_root/bin/central"
fixture_number=0
passed=0
new_fixture() {
  fixture_number=$((fixture_number + 1))
  fixture="$test_root/$fixture_number path with spaces"
  mkdir -p "$fixture/project/subdir"
  git -C "$fixture/project" init -q
  git -C "$fixture/project" config core.hooksPath "$test_root/no-hooks"
  git -C "$fixture/project" -c user.name=Tests -c user.email=tests@example.invalid commit -q --allow-empty -m initial
  git -C "$fixture/project" branch -M main
  export CENTRAL_CONFIG="$fixture/central.json"
  export CENTRAL_TEST_CAPTURE="$fixture/central.calls"
  export SBX_TEST_CAPTURE="$fixture/sbx.args"
  export SBX_TEST_CREATE_CAPTURE="$fixture/sbx-create.args"
  export SBX_TEST_CALLS="$fixture/sbx.calls"
  export SBX_TEST_STATE="$fixture/sbx.json"
  export SBX_TEST_POLICY_CAPTURE="$fixture/policy.calls"
  export SBX_TEST_POLICY_STATE="$fixture/policy.allowed"
  export MOCK_KEY='fake_proxy-key.123'
  export MOCK_CENTRAL_FAIL=0 MOCK_SBX_EXIT=0 MOCK_UPDATE_FLAG=''
  unset CENTRAL_PROXY_PORT CENTRAL_PROXY_HOST SBX_TEST_NATIVE_PROBE SBX_TEST_NATIVE_CAPTURE
  unset MOCK_LIST_FAIL MOCK_LIST_JSON MOCK_CREATE_EXIT MOCK_ALLOW_EXIT MOCK_STILL_DENIED
  unset MOCK_POLICY_CHECK_FAIL MOCK_POLICY_JSON MOCK_DENY_KIND
  printf '{"proxy_port":19516}\n' > "$CENTRAL_CONFIG"
}
run_launcher() {
  status=0
  (cd -- "$fixture/project/subdir" && bash "$launcher" "$@") > "$fixture/output" 2>&1 || status=$?
}
canonical_git_dir() {
  (cd -- "$1" && cd -- "$(git rev-parse --absolute-git-dir)" && pwd -P)
}
require() {
  if ! "$@"; then
    cat "$fixture/output" >&2
    printf 'Assertion failed: %s\n' "$*" >&2
    exit 1
  fi
}
has_arg() {
  local arg
  while IFS= read -r -d '' arg; do
    [[ "$arg" != "$1" ]] || return 0
  done < "$SBX_TEST_CAPTURE"
  if [[ -f "$SBX_TEST_CREATE_CAPTURE" ]]; then
    while IFS= read -r -d '' arg; do
      [[ "$arg" != "$1" ]] || return 0
    done < "$SBX_TEST_CREATE_CAPTURE"
  fi
  return 1
}
pass() { passed=$((passed + 1)); printf 'PASS %s\n' "$1"; }

new_fixture
run_launcher --help
require test "$status" -eq 0
require test ! -e "$CENTRAL_TEST_CAPTURE"
run_launcher
require test "$status" -eq 2
pass 'Help and missing arguments have no side effects'

mkdir "$fixture/missing-sbx"
printf '#!/bin/sh\nexit 99\n' > "$fixture/missing-sbx/git"
printf '#!/bin/sh\nexit 99\n' > "$fixture/missing-sbx/jq"
chmod +x "$fixture/missing-sbx/git" "$fixture/missing-sbx/jq"
status=0
PATH="$fixture/missing-sbx" "$BASH" "$launcher" feature > "$fixture/output" 2>&1 || status=$?
require test "$status" -eq 1
require grep -Fq 'Missing dependency: sbx.' "$fixture/output"
require test ! -d "$fixture/feature"
require test ! -e "$CENTRAL_TEST_CAPTURE"
pass 'Fail before side effects when sbx is missing'

for invalid in ../outside /absolute --flag 'two words' 'a;echo-bad' Uppercase; do
  run_launcher "$invalid"
  require test "$status" -ne 0
  require test ! -e "$CENTRAL_TEST_CAPTURE"
done
pass 'Reject path traversal, options and invalid names'

printf -v too_long '%064d' 0
for invalid in a with_underscore default "$too_long"; do
  run_launcher "$invalid"
  require test "$status" -ne 0
  require test ! -e "$CENTRAL_TEST_CAPTURE"
  require test ! -d "$fixture/$invalid"
done
pass 'Reject names unsupported by sbx before creating a worktree'

run_launcher feature -- --model 'model-from-central' 'Prompt with spaces; $(not-a-command)'
require test "$status" -eq 0
require test -f "$fixture/feature/.git"
require test "$(git -C "$fixture/feature" branch --show-current)" = feature
require has_arg "$fixture/feature"
require has_arg "$fixture/project/.git"
require has_arg "GIT_DIR=$(canonical_git_dir "$fixture/feature")"
require has_arg "GIT_WORK_TREE=$fixture/feature"
if has_arg ':git'; then echo 'Unsupported :git workspace argument.' >&2; exit 1; fi
require has_arg '--'
require has_arg 'OPENAI_API_KEY=fake_proxy-key.123'
require has_arg 'OPENAI_BASE_URL=http://host.docker.internal:19516/wire/fake_proxy-key.123/codex/openai/v1'
require has_arg 'model_providers.jetbrains_central.env_key="OPENAI_API_KEY"'
require has_arg 'model_providers.jetbrains_central.wire_api="responses"'
require has_arg 'model_providers.jetbrains_central.requires_openai_auth=false'
require has_arg 'Prompt with spaces; $(not-a-command)'
require test ! -e "$fixture/project/subdir/not-a-command"
if grep -Fq "$MOCK_KEY" "$fixture/output"; then echo 'Proxy key leaked to output.' >&2; exit 1; fi
require test ! -d "$fixture/feature/.codex"
pass 'Create worktree; preserve paths/arguments; configure Responses without leaking the key'

sbx_args=()
while IFS= read -r -d '' arg; do sbx_args+=("$arg"); done < "$SBX_TEST_CAPTURE"
require test "${#sbx_args[@]}" -eq 28
require test "${sbx_args[0]}" = run
require test "${sbx_args[1]}" = --name
require test "${sbx_args[2]}" = feature
require test "${sbx_args[11]}" = codex
require test "${sbx_args[12]}" = --
require test "${sbx_args[25]}" = --model
require test "${sbx_args[26]}" = model-from-central
require test "${sbx_args[27]}" = 'Prompt with spaces; $(not-a-command)'
create_args=()
while IFS= read -r -d '' arg; do create_args+=("$arg"); done < "$SBX_TEST_CREATE_CAPTURE"
require test "${#create_args[@]}" -eq 14
require test "${create_args[0]}" = create
require test "${create_args[11]}" = codex
require test "${create_args[12]}" = "$fixture/feature"
require test "${create_args[13]}" = "$fixture/project/.git"
require grep -Fxq 'policy allow network --sandbox feature localhost:19516' "$SBX_TEST_POLICY_CAPTURE"
pass 'Create with both mounts; allow only the scoped Central port; attach without workspaces'

printf 'uncommitted\n' > "$fixture/feature/keep.txt"
run_launcher feature
require test "$status" -eq 0
require test "$(cat "$fixture/feature/keep.txt")" = uncommitted
pass 'Reuse the matching worktree without losing local changes'
require test "$(grep -c '^create ' "$SBX_TEST_CALLS")" -eq 1
require test "$(grep -c '^policy allow ' "$SBX_TEST_CALLS")" -eq 1
export MOCK_KEY='refreshed_proxy-key'
run_launcher feature
require test "$status" -eq 0
require has_arg 'OPENAI_API_KEY=refreshed_proxy-key'
require test "$(grep -c '^create ' "$SBX_TEST_CALLS")" -eq 1
pass 'Reuse sandbox without recreating it or duplicating policy; refresh session credentials'

mkdir -p "$fixture/feature/subdir"
status=0
(cd -- "$fixture/feature/subdir" && bash "$launcher" second) > "$fixture/output" 2>&1 || status=$?
require test "$status" -eq 0
require has_arg "$fixture/project/.git"
require has_arg "GIT_DIR=$(canonical_git_dir "$fixture/second")"
require has_arg "GIT_WORK_TREE=$fixture/second"
pass 'Resolve the original common Git directory when launched inside a linked worktree'

new_fixture
git -C "$fixture/project" worktree add -q -b feature "$fixture/original-location"
git -C "$fixture/project" worktree move "$fixture/original-location" "$fixture/feature"
run_launcher feature
require test "$status" -eq 0
require has_arg "GIT_DIR=$fixture/project/.git/worktrees/original-location"
require has_arg "GIT_WORK_TREE=$fixture/feature"
pass 'Resolve the actual Git directory after a worktree has been moved'

new_fixture
git -C "$fixture/project" branch existing
run_launcher existing
require test "$status" -eq 0
pass 'Reuse an existing branch that has no worktree'

new_fixture
printf -v longest '%063d' 0
for valid in ab "$longest"; do
  run_launcher "$valid"
  require test "$status" -eq 0
  require test "$(git -C "$fixture/$valid" branch --show-current)" = "$valid"
done
pass 'Accept sandbox names at both length boundaries'

new_fixture
mkdir "$fixture/feature"
printf 'keep\n' > "$fixture/feature/keep.txt"
run_launcher feature
require test "$status" -ne 0
require test "$(cat "$fixture/feature/keep.txt")" = keep
require test ! -e "$CENTRAL_TEST_CAPTURE"
pass 'Refuse an unrelated directory without modifying it'

new_fixture
mkdir "$fixture/feature"
git -C "$fixture/feature" init -q
git -C "$fixture/feature" -c core.hooksPath="$test_root/no-hooks" -c user.name=Tests -c user.email=tests@example.invalid commit -q --allow-empty -m foreign
git -C "$fixture/feature" branch -M feature
run_launcher feature
require test "$status" -ne 0
require test ! -e "$CENTRAL_TEST_CAPTURE"
pass 'Refuse a same-named branch in a foreign repository'

new_fixture
run_launcher project
require test "$status" -ne 0
require test ! -e "$CENTRAL_TEST_CAPTURE"
pass 'Never use the current repository as the sandbox worktree'

new_fixture
git -C "$fixture/project" worktree add -q -b wrong "$fixture/feature"
run_launcher feature
require test "$status" -ne 0
require test ! -e "$CENTRAL_TEST_CAPTURE"
pass 'Refuse a worktree on the wrong branch'

new_fixture
git -C "$fixture/project" worktree add -q --detach "$fixture/feature"
run_launcher feature
require test "$status" -ne 0
require test ! -e "$CENTRAL_TEST_CAPTURE"
pass 'Refuse a detached worktree'

new_fixture
git -C "$fixture/project" branch feature
git -C "$fixture/project" worktree add -q "$fixture/already-checked-out" feature
run_launcher feature
require test "$status" -ne 0
require test ! -e "$CENTRAL_TEST_CAPTURE"
pass 'Do not force a branch already checked out elsewhere'

new_fixture
export MOCK_UPDATE_FLAG=--ensure-updated
printf '{"proxy_port":20123}\n' > "$CENTRAL_CONFIG"
run_launcher feature
require test "$status" -eq 0
require grep -Fq -- '--ensure-updated' "$CENTRAL_TEST_CAPTURE"
require has_arg 'OPENAI_BASE_URL=http://host.docker.internal:20123/wire/fake_proxy-key.123/codex/openai/v1'
pass 'Read configured port and use ensure-updated only when supported'

new_fixture
printf '{}\n' > "$CENTRAL_CONFIG"
run_launcher feature
require test "$status" -eq 0
require has_arg 'OPENAI_BASE_URL=http://host.docker.internal:19516/wire/fake_proxy-key.123/codex/openai/v1'
pass 'Default to port 19516 when the setting is missing'

export CENTRAL_PROXY_PORT=21345 CENTRAL_PROXY_HOST=192.168.65.1
run_launcher feature
require test "$status" -eq 0
require has_arg 'OPENAI_BASE_URL=http://192.168.65.1:21345/wire/fake_proxy-key.123/codex/openai/v1'
require grep -Fxq 'policy allow network --sandbox feature 192.168.65.1:21345' "$SBX_TEST_POLICY_CAPTURE"
pass 'Allow explicit sandbox host and port overrides'

new_fixture
printf '{broken json\n' > "$CENTRAL_CONFIG"
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Reject malformed Central config'

new_fixture
printf '{"proxy_port":65536}\n' > "$CENTRAL_CONFIG"
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
export CENTRAL_PROXY_PORT=0
run_launcher feature
require test "$status" -ne 0
pass 'Reject invalid configured and overridden ports'

new_fixture
printf '{"proxy_port":false}\n' > "$CENTRAL_CONFIG"
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
printf 'null\n' > "$CENTRAL_CONFIG"
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Reject boolean ports and non-object config instead of hiding them with defaults'

new_fixture
export CENTRAL_PROXY_HOST='host.example/steal'
run_launcher feature
require test "$status" -ne 0
require test ! -e "$CENTRAL_TEST_CAPTURE"
require test ! -d "$fixture/feature"
pass 'Reject a proxy host containing a URL path'

new_fixture
export MOCK_KEY=''
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
export MOCK_KEY=$'bad\nkey'
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Reject empty or multiline proxy keys'

new_fixture
export MOCK_CENTRAL_FAIL=1
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Do not launch sbx when Central fails'

new_fixture
export MOCK_KEY=$'fake_proxy-key.123\r' MOCK_SBX_EXIT=17
run_launcher feature
require test "$status" -eq 17
require has_arg 'OPENAI_API_KEY=fake_proxy-key.123'
pass 'Accept Windows line endings and propagate the sbx exit code'

new_fixture
export MOCK_LIST_FAIL=1
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CREATE_CAPTURE"
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Do not create a sandbox when listing existing sandboxes fails'

new_fixture
for bad_list in '{broken' '{}' '{"sandboxes":false}'; do
  export MOCK_LIST_JSON="$bad_list"
  run_launcher feature
  require test "$status" -ne 0
  require test ! -e "$SBX_TEST_CREATE_CAPTURE"
done
pass 'Reject malformed sandbox inventory instead of assuming no sandbox exists'

new_fixture
run_launcher feature
require test "$status" -eq 0
saved_state=$(cat "$SBX_TEST_STATE")
for change in '.sandboxes[0].agent="claude"' '.sandboxes[0].workspaces[0]="/unrelated"' '.sandboxes[0].workspaces |= reverse' '.sandboxes[0].workspaces |= .[:1]' '.sandboxes += .sandboxes'; do
  printf '%s' "$saved_state" | jq "$change" > "$SBX_TEST_STATE"
  run_launcher feature
  require test "$status" -ne 0
done
require test "$(grep -c '^run ' "$SBX_TEST_CALLS")" -eq 1
require test "$(grep -c '^create ' "$SBX_TEST_CALLS")" -eq 1
pass 'Refuse an existing sandbox with mismatched agent, mounts, or duplicate name'

case "${OSTYPE:-}" in
  msys*|cygwin*)
    printf '%s' "$saved_state" | jq '.sandboxes[0].workspaces |= map(sub("^/(?<drive>[a-z])/"; "\(.drive):/") | gsub("/"; "\\") | ascii_upcase)' > "$SBX_TEST_STATE"
    run_launcher feature
    require test "$status" -eq 0
    pass 'Match native Windows workspace paths without case or separator sensitivity'
    ;;
esac

new_fixture
export MOCK_CREATE_EXIT=18
run_launcher feature
require test "$status" -eq 18
require test ! -e "$SBX_TEST_POLICY_CAPTURE"
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Stop after failed sandbox creation without applying policy or launching Codex'

new_fixture
export MOCK_DENY_KIND=explicit
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
if grep -q '^policy allow ' "$SBX_TEST_CALLS"; then echo 'Explicit deny must not be modified.' >&2; exit 1; fi
pass 'Respect explicit network deny rules without adding an allow rule'

new_fixture
export MOCK_ALLOW_EXIT=19
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Do not start Codex if the scoped network permission cannot be added'

new_fixture
export MOCK_STILL_DENIED=1
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Verify that the policy really allows Central after adding the rule'

new_fixture
export MOCK_POLICY_CHECK_FAIL=2
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
unset MOCK_POLICY_CHECK_FAIL
export MOCK_POLICY_JSON='{"allowed":"false"}'
run_launcher feature
require test "$status" -ne 0
require test ! -e "$SBX_TEST_CAPTURE"
pass 'Reject policy check failures and malformed policy responses'

if [[ "${SBX_TEST_DOCKER:-0}" == 1 ]]; then
  command -v docker >/dev/null || { echo 'Docker smoke test requires docker.' >&2; exit 1; }
  new_fixture
  run_launcher docker-git
  require test "$status" -eq 0
  sbx_args=()
  while IFS= read -r -d '' arg; do sbx_args+=("$arg"); done < "$SBX_TEST_CREATE_CAPTURE"
  docker_worktree=${sbx_args[12]}
  docker_common=${sbx_args[13]}
  docker_git_dir=${sbx_args[8]#GIT_DIR=}
  docker_host_worktree=$docker_worktree
  docker_host_common=$docker_common
  windows_pointer=0
  case "${OSTYPE:-}" in
    msys*|cygwin*)
      docker_host_worktree=$(cygpath -am "$docker_worktree")
      docker_host_common=$(cygpath -am "$docker_common")
      windows_pointer=1
      # Exercise the same MSYS-to-native argument boundary as sbx.exe.
      export SBX_TEST_NATIVE_PROBE="$fixture/native-args.ps1"
      export SBX_TEST_NATIVE_CAPTURE="$fixture/native-args.json"
      printf 'ConvertTo-Json -InputObject @($args)\n' > "$SBX_TEST_NATIVE_PROBE"
      run_launcher docker-git
      require test "$status" -eq 0
      MSYS2_ARG_CONV_EXCL='GIT_DIR=;GIT_WORK_TREE=' require jq -e \
        --arg dir "GIT_DIR=$docker_git_dir" --arg tree "GIT_WORK_TREE=$docker_worktree" \
        '.[8] == $dir and .[10] == $tree' "$SBX_TEST_NATIVE_CAPTURE" >/dev/null
      ;;
  esac
  # Only the throwaway fixture and its .git are mounted. No Central key or
  # host credentials are passed to Docker; the launcher used a fake key.
  MSYS2_ARG_CONV_EXCL='*' docker run --rm \
    --mount "type=bind,source=$docker_host_worktree,target=$docker_worktree" \
    --mount "type=bind,source=$docker_host_common,target=$docker_common" \
    -e "GIT_DIR=$docker_git_dir" -e "GIT_WORK_TREE=$docker_worktree" \
    -e "EXPECT_WINDOWS_POINTER=$windows_pointer" -w "$docker_worktree" \
    alpine:3.22 sh -eu -c '
      apk add --no-cache git >/dev/null
      git config --global --add safe.directory "$GIT_WORK_TREE"
      if [ "$EXPECT_WINDOWS_POINTER" = 1 ]; then
        if env -u GIT_DIR -u GIT_WORK_TREE git status --porcelain >/dev/null 2>&1; then
          echo "Expected a Windows .git pointer to require the explicit Linux Git paths." >&2
          exit 1
        fi
      fi
      test "$(git branch --show-current)" = docker-git
      test -z "$(git status --porcelain)"
      printf "created inside Docker\n" > docker-smoke.txt
      git add docker-smoke.txt
      git -c core.hooksPath=/dev/null -c user.name=Tests -c user.email=tests@example.invalid commit -qm docker-git-smoke
      test -z "$(git status --porcelain)"
    ' > "$fixture/docker-output" 2>&1 || { cat "$fixture/docker-output" >&2; exit 1; }
  require test "$(git -C "$fixture/docker-git" log -1 --format=%s)" = docker-git-smoke
  require test "$(git -C "$fixture/project" log -1 --format=%s)" = initial
  require test -z "$(git -C "$fixture/docker-git" status --porcelain)"
  pass 'Real Docker: Git status and commit work with launcher mounts and Windows pointer files'
fi

printf '%s tests passed.\n' "$passed"
