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
printf '%s\0' "$@" > "$SBX_TEST_CAPTURE"
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
  export MOCK_KEY='fake_proxy-key.123'
  export MOCK_CENTRAL_FAIL=0 MOCK_SBX_EXIT=0 MOCK_UPDATE_FLAG=''
  unset CENTRAL_PROXY_PORT CENTRAL_PROXY_HOST
  printf '{"proxy_port":19516}\n' > "$CENTRAL_CONFIG"
}
run_launcher() {
  status=0
  (cd -- "$fixture/project/subdir" && bash "$launcher" "$@") > "$fixture/output" 2>&1 || status=$?
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
require test "${#sbx_args[@]}" -eq 25
require test "${sbx_args[0]}" = run
require test "${sbx_args[1]}" = --name
require test "${sbx_args[2]}" = feature
require test "${sbx_args[7]}" = codex
require test "${sbx_args[8]}" = "$fixture/feature"
require test "${sbx_args[9]}" = --
require test "${sbx_args[22]}" = --model
require test "${sbx_args[23]}" = model-from-central
require test "${sbx_args[24]}" = 'Prompt with spaces; $(not-a-command)'
pass 'Use one workspace followed by the sbx argument separator, without a :git mount'

printf 'uncommitted\n' > "$fixture/feature/keep.txt"
run_launcher feature
require test "$status" -eq 0
require test "$(cat "$fixture/feature/keep.txt")" = uncommitted
pass 'Reuse the matching worktree without losing local changes'

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

printf '%s tests passed.\n' "$passed"
