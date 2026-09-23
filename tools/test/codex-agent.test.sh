#!/usr/bin/env bash
#
# tools/codex-agent.sh の分岐を、偽の codex で確かめるテスト。
# 実モデルと実認証情報は使わない。
#
# 用法:
#   bash tools/test/codex-agent.test.sh
#
# 出力は 1 ケース 1 行で、ok N - <名前> / not ok N - <名前> / ok N - <名前> # SKIP <理由> の形をとる。
# 1 件でも失敗すれば終了コード 1 を返す。
#
# 環境の分離:
#   ラッパーは USERPROFILE(無ければ HOME)の下で定義を探し、ログを作り、古いログを削除する。
#   実際のユーザ設定とログに触れないよう、ケースごとに一時ルートを作り、
#   USERPROFILE と HOME をその下の home に、カレントディレクトリを work に向ける。
#   PATH は偽の codex を置いた bin と /usr/bin、/bin だけに絞る。
#   エージェント名はテスト専用の名前にし、全ケースの後で実ホームのログ置き場にその名前が無いことを確かめる。

set -u

test_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$test_dir/../.." && pwd)"
WRAPPER="$repo_root/tools/codex-agent.sh"
CROSS_REVIEW_JS="$repo_root/tools/cross-review.js"
BASH_BIN="${BASH:-bash}"

# 実ホームはテスト開始時点の値を控える。ケースの中では一時パスへ差し替えるためである。
REAL_HOME="${USERPROFILE:-$HOME}"
REAL_HOME="${REAL_HOME//\\//}"

# 呼び出し元の環境に試験用フックが残っていると、全ケースの結果が変わるため外す。
unset CODEX_AGENT_SIMULATE_RATE_LIMIT CODEX_AGENT_SIMULATE_UNAVAILABLE

# 実在の定義と衝突しない名前にする。ログ名に含まれるので、実ホームへの漏れの検出にも使う。
AGENT="cxa-test-${RANDOM}${RANDOM}"

TEST_TMP="$(mktemp -d "${TMPDIR:-/tmp}/cxa-test.XXXXXX")" || { echo "一時ディレクトリを作れない" >&2; exit 1; }
trap 'rm -rf "$TEST_TMP"' EXIT

has_cygpath=0
command -v cygpath >/dev/null 2>&1 && has_cygpath=1

# 書き込み担当の目印を扱うケースだけ、git のディレクトリを PATH に足す。
# ケースの PATH は /usr/bin と /bin に絞っているが、Git for Windows の git は /mingw64/bin にあることがあるためである。
# 他のケースに足さないのは、git の有無で既存のケースの環境を変えないためである。
GIT_BIN_DIR=""
if command -v git >/dev/null 2>&1; then
  GIT_BIN_DIR="$(dirname "$(command -v git)")"
fi

# ラッパーと同じ規則でパスを揃える。比較の前に両辺へ適用する。
norm_path() {
  local p="${1//\\//}"
  if [ "$has_cygpath" -eq 1 ]; then
    cygpath -m "$p" 2>/dev/null || printf '%s' "$p"
  else
    printf '%s' "$p"
  fi
}

# ---------------------------------------------------------------------------
# 判定の補助
# ---------------------------------------------------------------------------

FAILS=""
SKIP_REASON=""
root=""
RC=""

fail() {
  FAILS+="$1"$'\n'
}

skip() {
  SKIP_REASON="$1"
}

expect_eq() {
  [ "$2" = "$3" ] || fail "$1: 期待=[$2] 実際=[$3]"
}

expect_rc() {
  expect_eq "終了コード" "$1" "$RC"
}

# 標準出力に完全一致の行がある。
expect_out_line() {
  grep -Fxq -- "$1" "$root/out" || fail "標準出力に行が無い: [$1]"
}

expect_out_no_match() {
  if grep -Fq -- "$1" "$root/out"; then
    fail "標準出力に出てはならない文字列がある: [$1]"
  fi
}

# 標準エラーに codex-agent: で始まる理由の行がある。
expect_err_reason() {
  grep -q '^codex-agent: ' "$root/err" || fail "標準エラーに codex-agent: で始まる理由の行が無い"
}

last_out_line() {
  tail -n 1 "$root/out"
}

# 偽 codex の呼び出し回数を種類別に数える(help / exec / other)。
count_calls() {
  local n=0
  if [ -f "$root/fake/calls.log" ]; then
    n="$(grep -c "^@@CALL $1\$" "$root/fake/calls.log")"
  fi
  printf '%s' "$n"
}

expect_no_exec() {
  expect_eq "exec - の呼び出し回数" "0" "$(count_calls exec)"
}

expect_no_codex_call() {
  if [ -s "$root/fake/calls.log" ]; then
    fail "codex が起動された: $(tr '\n' ' ' <"$root/fake/calls.log")"
  fi
}

# n 番目(1 始まり)の呼び出しについて、指定した項目を取り出す。
call_field() {
  awk -v want="$1" -v field="$2" '
    /^@@CALL / { n++; next }
    n == want && field == "home" && /^CODEX_HOME=/ { sub(/^CODEX_HOME=/, ""); print }
  ' "$root/fake/calls.log"
}

call_kind() {
  awk -v want="$1" '/^@@CALL / { n++; if (n == want) { print $2; exit } }' "$root/fake/calls.log"
}

# 最初の exec - 呼び出しの引数を 1 行 1 引数で出す。
exec_args() {
  awk '
    /^@@CALL / { inblock = ($2 == "exec" && !done); next }
    /^@@END$/ { if (inblock) done = 1; inblock = 0; next }
    inblock && /^ARG / { sub(/^ARG /, ""); print }
  ' "$root/fake/calls.log"
}

# 引数列で flag の直後に来る値をすべて出す。
arg_values_after() {
  exec_args | awk -v flag="$1" 'prev == flag { print } { prev = $0 }'
}

expect_arg_pair() {
  local values
  values="$(arg_values_after "$1")"
  if ! printf '%s\n' "$values" | grep -Fxq -- "$2"; then
    fail "引数 $1 の値が期待と違う: 期待=[$2] 実際=[$(printf '%s' "$values" | tr '\n' ' ')]"
  fi
}

# exec の引数一覧が、期待する一覧と過不足なく一致する(順序は問わない)。
# 権限の固定(CLAUDE.md)を守るため、必須の引数があるかではなく一覧の一致で見る。
# 前者だと、権限を緩める引数(-s や --sandbox= の別表記、--approve-for-me、
# --dangerously-bypass-approvals-and-sandbox など)が足されても通ってしまう。
# 値と引数の組は expect_arg_pair で別に確かめる。-o の値は実行ごとに変わるので <last> に置き換える。
# expect_exec_args <sandbox> <effort>
expect_exec_args() {
  local expected actual
  expected="$(printf '%s\n' exec -c approval_policy=never --skip-git-repo-check --sandbox "$1" -m model-test \
    -c "model_reasoning_effort=\"$2\"" -C "$(norm_path "$root/work")" -o '<last>' - | LC_ALL=C sort)"
  actual="$(exec_args | awk 'prev == "-o" { print "<last>"; prev = ""; next } { print; prev = $0 }' | LC_ALL=C sort)"
  if [ "$expected" != "$actual" ]; then
    fail "exec の引数一覧が期待と違う: 期待=[$(printf '%s' "$expected" | tr '\n' ' ')] 実際=[$(printf '%s' "$actual" | tr '\n' ' ')]"
  fi
}

# ---------------------------------------------------------------------------
# ケースの環境
# ---------------------------------------------------------------------------

write_fake_codex() {
  cat >"$root/bin/codex" <<FAKE
#!/bin/bash
FAKE_DIR='$root/fake'
FAKE
  cat >>"$root/bin/codex" <<'FAKE'
# 偽の codex。振る舞いは FAKE_DIR のファイルで決める。
#   no_output_last_message  あれば exec --help に --output-last-message を出さない
#   last_message            -o <file> へ書く最終報告
#   stdout / stderr         そのまま出す内容
#   sleep                   stderr を出したあとに待つ秒数
#   help_delay              exec --help で --output-last-message の行を出したあと、残りを出す前に待つ秒数
#   exit_code               終了コード(既定 0)
#   native_wait             stderr を出したあと、Windows の ping.exe をこの回数で exec して待つ
#                           npm のシム(sh スクリプトが node.exe を exec する形)と同じプロセスの形を作る
# exec のときは、自分の PID を exec.pid に、Windows の PID が読めれば exec.winpid に書く。
kind=other
last_arg=""
[ $# -gt 0 ] && last_arg="${!#}"
if [ "${1:-}" = exec ] && [ "${2:-}" = --help ]; then
  kind=help
elif [ "${1:-}" = exec ] && [ "$last_arg" = - ]; then
  kind=exec
fi
{
  printf '@@CALL %s\n' "$kind"
  printf 'CODEX_HOME=%s\n' "${CODEX_HOME-<unset>}"
  for a in "$@"; do printf 'ARG %s\n' "$a"; done
  printf '@@END\n'
} >>"$FAKE_DIR/calls.log"

case "$kind" in
  help)
    printf 'Run Codex non-interactively\n\nUsage: codex exec [OPTIONS] [PROMPT]\n\nOptions:\n'
    printf '  -m, --model <MODEL>\n'
    [ -e "$FAKE_DIR/no_output_last_message" ] || printf '  -o, --output-last-message <FILE>\n'
    [ -f "$FAKE_DIR/help_delay" ] && sleep "$(cat "$FAKE_DIR/help_delay")"
    printf '  -C, --cd <DIR>\n'
    exit 0
    ;;
  exec)
    printf '%s\n' "$$" >"$FAKE_DIR/exec.pid"
    if [ -r "/proc/$$/winpid" ]; then
      cat "/proc/$$/winpid" >"$FAKE_DIR/exec.winpid"
    fi
    cat >"$FAKE_DIR/exec.stdin.$$"
    cp "$FAKE_DIR/exec.stdin.$$" "$FAKE_DIR/exec.stdin"
    out=""
    prev=""
    for a in "$@"; do
      [ "$prev" = -o ] && out="$a"
      prev="$a"
    done
    if [ -n "$out" ] && [ -f "$FAKE_DIR/last_message" ]; then
      cat "$FAKE_DIR/last_message" >"$out"
    fi
    [ -f "$FAKE_DIR/stdout" ] && cat "$FAKE_DIR/stdout"
    [ -f "$FAKE_DIR/stderr" ] && cat "$FAKE_DIR/stderr" >&2
    if [ -f "$FAKE_DIR/native_wait" ]; then
      exec "$(cat "$FAKE_DIR/ping_exe")" -n "$(cat "$FAKE_DIR/native_wait")" 127.0.0.1 >/dev/null
    fi
    [ -f "$FAKE_DIR/sleep" ] && sleep "$(cat "$FAKE_DIR/sleep")"
    code=0
    [ -f "$FAKE_DIR/exit_code" ] && code="$(cat "$FAKE_DIR/exit_code")"
    exit "$code"
    ;;
  *)
    exit 0
    ;;
esac
FAKE
  chmod +x "$root/bin/codex"
}

DEFAULT_FM='codex_home: ~/.codex-test
codex_model: model-test
codex_reasoning_effort: low
codex_sandbox: workspace-write'
DEFAULT_BODY='役割文の1行目
役割文の2行目'

# write_def <path> <フロントマターの中身> [役割文]
write_def() {
  mkdir -p "$(dirname "$1")"
  {
    printf -- '---\nname: test-agent\ndescription: テスト用の定義\n%s\n---\n' "$2"
    if [ $# -ge 3 ] && [ -n "$3" ]; then
      printf '\n%s\n' "$3"
    fi
  } >"$1"
}

work_def() {
  printf '%s' "$root/work/.claude/gpt-agents/$AGENT.md"
}

home_def() {
  printf '%s' "$root/home/.claude/gpt-agents/$AGENT.md"
}

new_case_root() {
  root="$(mktemp -d "$TEST_TMP/case.XXXXXX")"
  mkdir -p "$root/home/.codex-test" "$root/work/.claude/gpt-agents" "$root/bin" "$root/fake" "$root/tmp"
  write_fake_codex
  write_def "$(work_def)" "$DEFAULT_FM" "$DEFAULT_BODY"
  REQ='依頼の本文である。
2 行目の依頼。'
  EXTRA_ENV=()
  NO_FAKE_BIN=0
  EXTRA_PATH=""
  RUN_OUT=""
}

# fake_set <name> <内容>
fake_set() {
  printf '%s' "$2" >"$root/fake/$1"
}

logs_dir() {
  printf '%s' "$root/home/.claude/codex-agent/logs"
}

# ラッパーを起動する。標準出力は $root/out(RUN_OUT があればそのパス)、標準エラーは $root/err、終了コードは RC に入る。
# 出力に log= の行があれば、一時ルートの配下を指すことも確かめる。
# EXTRA_PATH があれば PATH の末尾に足す。
run_wrapper() {
  local path="$root/bin:/usr/bin:/bin" out="${RUN_OUT:-$root/out}"
  [ "$NO_FAKE_BIN" = 1 ] && path="/usr/bin:/bin"
  [ -z "$EXTRA_PATH" ] || path="$path:$EXTRA_PATH"
  (
    cd "$root/work" || exit 99
    export USERPROFILE="$root/home" HOME="$root/home" PATH="$path" TMPDIR="$root/tmp"
    printf '%s' "$REQ" | env ${EXTRA_ENV[@]+"${EXTRA_ENV[@]}"} "$BASH_BIN" "$WRAPPER" "$@" >"$out" 2>"$root/err"
  )
  RC=$?
  check_log_path "$out"
}

check_log_path() {
  local want line value
  want="$(norm_path "$root")"
  while IFS= read -r line; do
    value="${line#codex-agent: log=}"
    case "$(norm_path "$value")" in
      "$want"/*) ;;
      *) fail "log= が一時ルートの配下を指していない: [$value] (一時ルート [$want])" ;;
    esac
  done < <(grep '^codex-agent: log=' "$1")
}

only_log_file() {
  ls "$(logs_dir)"/*.log 2>/dev/null | head -n 1
}

# 標準出力の log= の行が指すログのパス。
out_log_path() {
  sed -n 's/^codex-agent: log=//p' "$1" | head -n 1
}

# ラッパーをバックグラウンドで起動する。標準出力と標準エラーの行き先、PATH などは run_wrapper と同じである。
# 起動したサブシェルの PID を BG_PID に入れる。終了は finish_bg_wrapper で待つ。
start_bg_wrapper() {
  local path="$root/bin:/usr/bin:/bin"
  [ -z "$EXTRA_PATH" ] || path="$path:$EXTRA_PATH"
  (
    cd "$root/work" || exit 99
    export USERPROFILE="$root/home" HOME="$root/home" PATH="$path" TMPDIR="$root/tmp"
    printf '%s' "$REQ" | "$BASH_BIN" "$WRAPPER" "$@" >"$root/out" 2>"$root/err"
  ) &
  BG_PID=$!
}

finish_bg_wrapper() {
  wait "$BG_PID"
  RC=$?
  check_log_path "$root/out"
}

# 条件が成り立つまで 0.1 秒おきに確かめる。上限を過ぎたら 1 を返す。
# wait_until <上限の秒数> <コマンド...>
wait_until() {
  local limit=$(( $1 * 10 )) i=0
  shift
  while ! "$@"; do
    i=$((i + 1))
    [ "$i" -lt "$limit" ] || return 1
    sleep 0.1
  done
  return 0
}

# 実行中のラッパーの標準出力に log= の行が出て、そのログに印の行が書かれている。
running_log_has() {
  local log
  log="$(out_log_path "$root/out")"
  [ -n "$log" ] && [ -f "$log" ] && grep -Fq -- "$1" "$log"
}

# 実行中の観測に使う偽 codex の振る舞い。印の行を stderr に出したあと待つ。
set_running_fake() {
  fake_set stderr 'WARNING: 警告の行
hook: フックの行
経過: 実行中の印
'
  fake_set last_message '報告
'
  fake_set sleep "$1"
}

# 完了後のログの最後の行が標準出力の最後の行(result= の行)と一致し、
# 標準出力の result= の行と run= の行がそれぞれちょうど 1 回である。
expect_log_ends_with_result() {
  local log last
  last="$(last_out_line)"
  case "$last" in
    "codex-agent: result="*) ;;
    *) fail "標準出力の最後の行が result= の行でない: [$last]" ;;
  esac
  expect_eq "標準出力の result= の行数" "1" "$(grep -c '^codex-agent: result=' "$root/out")"
  expect_eq "標準出力の run= の行数" "1" "$(grep -c '^codex-agent: run=' "$root/out")"
  log="$(out_log_path "$root/out")"
  if [ -z "$log" ] || [ ! -f "$log" ]; then
    fail "log= の行が指すログが無い: [$log]"
    return
  fi
  expect_eq "ログの最後の行" "$last" "$(tail -n 1 "$log")"
  expect_eq "ログの result= の行数" "1" "$(grep -c '^codex-agent: result=' "$log")"
  expect_eq "ログの 1 行目" "$(sed -n 2p "$root/out")" "$(sed -n 1p "$log")"
}

# Windows のプロセスとして残っているか。tasklist の CSV 出力で PID の列を照合する。
win_pid_alive() {
  tasklist //FI "PID eq $1" //NH //FO CSV 2>/dev/null | grep -Fq "\"$1\""
}

win_pid_gone() {
  ! win_pid_alive "$1"
}

# ---------------------------------------------------------------------------
# ケース
# ---------------------------------------------------------------------------

t_auth_home() {
  fake_set last_message '報告
'
  run_wrapper "$AGENT"
  expect_rc 0
  local want
  want="$(norm_path "$root/home/.codex-test")"
  expect_eq "codex の呼び出し回数" "2" "$(grep -c '^@@CALL ' "$root/fake/calls.log" 2>/dev/null)"
  expect_eq "1 回目の種類" "help" "$(call_kind 1)"
  expect_eq "2 回目の種類" "exec" "$(call_kind 2)"
  expect_eq "exec --help の CODEX_HOME" "$want" "$(call_field 1 home)"
  expect_eq "exec の CODEX_HOME" "$want" "$(call_field 2 home)"
}

t_args_basic() {
  run_wrapper "$AGENT"
  expect_rc 0
  exec_args | grep -Fxq -- '--skip-git-repo-check' || fail "--skip-git-repo-check が無い"
  expect_arg_pair --sandbox workspace-write
  expect_arg_pair -m model-test
  expect_arg_pair -c 'model_reasoning_effort="low"'
  expect_arg_pair -C "$(norm_path "$root/work")"
  expect_eq "approval_policy=never の回数" "1" "$(exec_args | grep -Fxc 'approval_policy=never')"
  expect_arg_pair -c 'approval_policy=never'
  expect_eq "最後の引数" "-" "$(exec_args | tail -n 1)"
  expect_exec_args workspace-write low
}

t_args_effort_override() {
  run_wrapper "$AGENT" --effort high
  expect_rc 0
  expect_arg_pair -c 'model_reasoning_effort="high"'
  if exec_args | grep -Fxq 'model_reasoning_effort="low"'; then
    fail "定義の effort(low)が残っている"
  fi
}

t_args_defaults() {
  write_def "$(work_def)" 'codex_home: ~/.codex-test
codex_model: model-test' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_rc 0
  expect_arg_pair -c 'model_reasoning_effort="medium"'
  expect_arg_pair --sandbox read-only
  expect_exec_args read-only medium
}

t_args_workdir() {
  mkdir -p "$root/other dir"
  run_wrapper "$AGENT" -C "$root/other dir"
  expect_rc 0
  expect_arg_pair -C "$(norm_path "$root/other dir")"
}

t_prompt_with_role() {
  run_wrapper "$AGENT"
  expect_rc 0
  printf '%s\n\n---\n\n## 依頼\n\n%s\n' "$DEFAULT_BODY" "$REQ" >"$root/expected_stdin"
  if ! cmp -s "$root/expected_stdin" "$root/fake/exec.stdin"; then
    fail "標準入力の内容が違う: 期待=[$(cat "$root/expected_stdin")] 実際=[$(cat "$root/fake/exec.stdin" 2>/dev/null)]"
  fi
}

t_prompt_without_role() {
  write_def "$(work_def)" "$DEFAULT_FM"
  run_wrapper "$AGENT"
  expect_rc 0
  printf '%s\n' "$REQ" >"$root/expected_stdin"
  if ! cmp -s "$root/expected_stdin" "$root/fake/exec.stdin"; then
    fail "標準入力の内容が違う: 期待=[$(cat "$root/expected_stdin")] 実際=[$(cat "$root/fake/exec.stdin" 2>/dev/null)]"
  fi
}

t_success_output() {
  fake_set last_message '最終報告の1行目
最終報告の2行目
'
  fake_set stderr '経過: 考えている
WARNING: 警告の行
hook: フックの行
'
  fake_set stdout 'FAKE-STDOUT-MARK
'
  run_wrapper "$AGENT"
  expect_rc 0
  expect_eq "標準出力の行数" "6" "$(wc -l <"$root/out" | tr -d ' ')"
  local l1
  l1="$(sed -n 1p "$root/out")"
  case "$l1" in
    "codex-agent: agent=$AGENT model=model-test effort=low sandbox=workspace-write codex_home=$(norm_path "$root/home/.codex-test") workdir=$(norm_path "$root/work")") ;;
    *) fail "1 行目が監査行でない: [$l1]" ;;
  esac
  case "$(sed -n 2p "$root/out")" in
    "codex-agent: run=$AGENT-"*" pid="*" started="*) ;;
    *) fail "2 行目が run= の行でない: [$(sed -n 2p "$root/out")]" ;;
  esac
  case "$(sed -n 3p "$root/out")" in
    "codex-agent: log="*) ;;
    *) fail "3 行目が log= の行でない: [$(sed -n 3p "$root/out")]" ;;
  esac
  expect_eq "4 行目" "最終報告の1行目" "$(sed -n 4p "$root/out")"
  expect_eq "5 行目" "最終報告の2行目" "$(sed -n 5p "$root/out")"
  expect_eq "6 行目" "codex-agent: result=ok" "$(sed -n 6p "$root/out")"
  expect_out_no_match "経過: 考えている"
  expect_out_no_match "FAKE-STDOUT-MARK"
  if grep -Fq "経過: 考えている" "$root/err"; then
    fail "経過が標準エラーに出ている"
  fi
  local log
  log="$(only_log_file)"
  if [ -z "$log" ]; then
    fail "ログファイルが無い"
  else
    grep -Fq "経過: 考えている" "$log" || fail "ログに経過(stderr)が無い"
    grep -Fq "FAKE-STDOUT-MARK" "$log" || fail "ログに codex の stdout が無い"
    if grep -qE '^(WARNING|hook:)' "$log"; then
      fail "ログに WARNING か hook: の行が残っている"
    fi
    expect_eq "log= の値" "$(norm_path "$log")" "$(norm_path "$(sed -n 's/^codex-agent: log=//p' "$root/out")")"
  fi
}

t_no_output_last_message() {
  : >"$root/fake/no_output_last_message"
  fake_set last_message '使われないはずの報告
'
  local i body=""
  for i in $(seq 1 50); do body+="stdout-line-$i"$'\n'; done
  fake_set stdout "$body"
  run_wrapper "$AGENT"
  expect_rc 0
  if exec_args | grep -Fxq -- '-o'; then
    fail "--output-last-message の無い版に -o が渡っている"
  fi
  {
    for i in $(seq 11 50); do printf 'stdout-line-%s\n' "$i"; done
    printf 'codex-agent: result=ok\n'
  } >"$root/expected_tail"
  tail -n +4 "$root/out" >"$root/actual_tail"
  if ! cmp -s "$root/expected_tail" "$root/actual_tail"; then
    fail "報告が標準出力の末尾 40 行になっていない: 実際の先頭=[$(head -n 2 "$root/actual_tail" | tr '\n' ' ')] 行数=$(wc -l <"$root/actual_tail" | tr -d ' ')"
  fi
  expect_out_no_match "使われないはずの報告"
}

# ヘルプの --output-last-message の行を読んだ時点で読み手が終わっても、有る版と判定する。
# ヘルプの残りを書く前に待たせ、読み手が先に終わる順序を毎回起こす。
t_help_detect_late_writer() {
  fake_set help_delay 0.5
  fake_set last_message '遅れて書くヘルプでも使われる報告
'
  run_wrapper "$AGENT"
  expect_rc 0
  exec_args | grep -Fxq -- '-o' || fail "-o が渡っていない(--output-last-message が無い版と判定された)"
  expect_out_line "遅れて書くヘルプでも使われる報告"
}

t_empty_last_message() {
  fake_set stdout 'report-from-stdout
'
  run_wrapper "$AGENT"
  expect_rc 0
  exec_args | grep -Fxq -- '-o' || fail "-o が渡っていない"
  expect_eq "4 行目" "report-from-stdout" "$(sed -n 4p "$root/out")"
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
}

t_last_message_no_newline() {
  fake_set last_message '改行で終わらない報告'
  run_wrapper "$AGENT"
  expect_rc 0
  expect_out_line "改行で終わらない報告"
  expect_out_line "codex-agent: result=ok"
}

# 利用上限と混雑の出力を確かめる。reason は rate-limit / unavailable、result は result= の値。
expect_fallback_tail() {
  local reason="$1" result="$2" n prev
  expect_rc 75
  expect_eq "最後の行" "codex-agent: result=$result" "$(last_out_line)"
  n="$(wc -l <"$root/out" | tr -d ' ')"
  prev="$(sed -n "$((n - 1))p" "$root/out")"
  case "$prev" in
    "codex-agent: $reason evidence: "*) ;;
    *) fail "result 行の直前が $reason evidence の行でない: [$prev]" ;;
  esac
  grep -q "^codex-agent: $reason evidence: " "$root/out" || fail "$reason evidence の行が無い"
}

t_rate_limit_stderr() {
  fake_set stderr "ERROR: You've hit your usage limit.
"
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_fallback_tail rate-limit rate-limited
}

t_rate_limit_stdout() {
  fake_set stdout "You've hit your usage limit. Try again later.
"
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_fallback_tail rate-limit rate-limited
}

# 根拠が複数行あっても、1 行ずつ接頭辞を付けて result 行の直前に並べる。
# 接頭辞の無い行が挟まると、呼び出し側の定義が根拠の範囲を読み違える。
t_rate_limit_multi_evidence() {
  fake_set stderr "ERROR: You've hit your usage limit.
ERROR: rate limit exceeded, retry later
"
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_fallback_tail rate-limit rate-limited
  expect_eq "evidence の行数" "2" "$(grep -c '^codex-agent: rate-limit evidence: ' "$root/out")"
  local n
  n="$(wc -l <"$root/out" | tr -d ' ')"
  expect_eq "result の 2 行前" "codex-agent: rate-limit evidence: ERROR: You've hit your usage limit." "$(sed -n "$((n - 2))p" "$root/out")"
  expect_eq "result の 1 行前" "codex-agent: rate-limit evidence: ERROR: rate limit exceeded, retry later" "$(sed -n "$((n - 1))p" "$root/out")"
}

t_unavailable() {
  fake_set stderr 'ERROR: Selected model is at capacity
'
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_fallback_tail unavailable unavailable
}

t_tool_handshake_failed_exit0() {
  fake_set stderr '2026-09-23T16:03:29.519274Z ERROR codex_core::tools::router: error=code-mode host exited during handshake
'
  # 最終回答に成功行と同じ語があっても、判定は経過(標準エラー)だけで行う。
  fake_set last_message '接続に失敗し、 succeeded in の行を確認できなかった。
'
  fake_set stdout '接続に失敗し、 succeeded in の行を確認できなかった。
'
  run_wrapper "$AGENT"
  expect_fallback_tail unavailable unavailable
  local n prev
  n="$(wc -l <"$root/out" | tr -d ' ')"
  prev="$(sed -n "$((n - 1))p" "$root/out")"
  case "$prev" in
    *'code-mode host exited during handshake'*) ;;
    *) fail "result 行の直前の evidence に一致した ERROR 行が無い: [$prev]" ;;
  esac
}

t_tool_handshake_recovered_exit0() {
  fake_set stderr 'ERROR codex_core::tools::router: error=code-mode host exited during handshake
exec
bash -lc ls in /work
 succeeded in 12ms:
'
  run_wrapper "$AGENT"
  expect_rc 0
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
}

t_plain_failure() {
  fake_set stderr 'ERROR: something went wrong
'
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_rc 1
  expect_eq "最後の行" "codex-agent: result=failed exit=1" "$(last_out_line)"
  if grep -q 'evidence: ' "$root/out"; then
    fail "通常の失敗に evidence の行が出ている"
  fi
}

t_codex_exit_75() {
  fake_set stderr 'ERROR: something went wrong
'
  fake_set exit_code 75
  run_wrapper "$AGENT"
  expect_rc 1
  expect_eq "最後の行" "codex-agent: result=failed exit=75" "$(last_out_line)"
}

t_success_body_mentions_limit() {
  fake_set last_message 'rate limit の扱いを調べた結果を報告する。
'
  fake_set stderr 'ERROR: usage limit という文字列をテストデータで読んだ
'
  run_wrapper "$AGENT"
  expect_rc 0
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
}

t_limit_word_mid_log() {
  local i body='note: fixture mentions usage limit here
'
  for i in $(seq 1 12); do body+="unrelated line $i"$'\n'; done
  fake_set stderr "$body"
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_rc 1
  expect_eq "最後の行" "codex-agent: result=failed exit=1" "$(last_out_line)"
}

t_429_id_not_matched() {
  fake_set stderr 'ERROR: request id=14290 failed
'
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_rc 1
  expect_eq "最後の行" "codex-agent: result=failed exit=1" "$(last_out_line)"
}

t_429_word_matched() {
  fake_set stderr 'ERROR: HTTP 429 Too Many Requests
'
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_fallback_tail rate-limit rate-limited
}

# 終了コード 2 の共通判定。
expect_exit2() {
  expect_rc 2
  expect_err_reason
  expect_no_exec
}

t_exit2_no_agent() {
  run_wrapper
  expect_exit2
}

t_exit2_unknown_option() {
  run_wrapper "$AGENT" --bogus
  expect_exit2
}

t_exit2_slash_in_name() {
  run_wrapper "gpt-agents/$AGENT"
  expect_exit2
}

t_exit2_bad_effort_option() {
  run_wrapper "$AGENT" --effort extreme
  expect_exit2
}

t_exit2_bad_effort_def() {
  write_def "$(work_def)" 'codex_home: ~/.codex-test
codex_model: model-test
codex_reasoning_effort: extreme' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_exit2
}

t_exit2_bad_sandbox() {
  write_def "$(work_def)" 'codex_home: ~/.codex-test
codex_model: model-test
codex_sandbox: danger-full-access' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_exit2
}

t_exit2_bad_enabled() {
  write_def "$(work_def)" "$DEFAULT_FM
codex_enabled: yes" "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_exit2
}

t_exit2_no_codex_home_key() {
  write_def "$(work_def)" 'codex_model: model-test' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_exit2
}

t_exit2_codex_home_missing() {
  write_def "$(work_def)" 'codex_home: ~/.codex-missing
codex_model: model-test' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_exit2
}

t_exit2_workdir_missing() {
  run_wrapper "$AGENT" -C "$root/no-such-dir"
  expect_exit2
}

t_exit2_blank_request() {
  REQ=$'   \n\t\n'
  run_wrapper "$AGENT"
  expect_exit2
}

# 終了コード 3 の共通判定。
expect_exit3() {
  expect_rc 3
  expect_out_line "codex-agent: result=failed exit=3"
  expect_err_reason
  expect_no_codex_call
}

t_exit3_no_def() {
  rm -f "$(work_def)"
  run_wrapper "$AGENT"
  expect_exit3
}

t_exit3_no_codex() {
  if (PATH=/usr/bin:/bin command -v codex >/dev/null 2>&1); then
    skip "/usr/bin か /bin に実物の codex がある"
    return
  fi
  NO_FAKE_BIN=1
  run_wrapper "$AGENT"
  expect_exit3
}

t_exit3_disabled() {
  write_def "$(work_def)" "$DEFAULT_FM
codex_enabled: false" "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_exit3
}

t_exit3_no_model_key() {
  write_def "$(work_def)" 'codex_home: ~/.codex-test' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_exit3
}

t_exit3_empty_model() {
  write_def "$(work_def)" 'codex_home: ~/.codex-test
codex_model: ""' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_exit3
}

t_lookup_work_first() {
  write_def "$(work_def)" 'codex_home: ~/.codex-test
codex_model: model-work' "$DEFAULT_BODY"
  write_def "$(home_def)" 'codex_home: ~/.codex-test
codex_model: model-home' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_rc 0
  expect_arg_pair -m model-work
  grep -q "^codex-agent: agent=$AGENT model=model-work " "$root/out" || fail "監査行の model が model-work でない"
}

t_lookup_home_fallback() {
  rm -f "$(work_def)"
  write_def "$(home_def)" 'codex_home: ~/.codex-test
codex_model: model-home' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_rc 0
  expect_arg_pair -m model-home
}

t_fm_trailing_comment() {
  write_def "$(work_def)" 'codex_home: ~/.codex-test  # 認証ホーム
codex_model: model-x  # comment' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_rc 0
  expect_arg_pair -m model-x
}

t_fm_double_quotes() {
  write_def "$(work_def)" 'codex_home: "~/.codex-test"
codex_model: "model-dq"' "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_rc 0
  expect_arg_pair -m model-dq
}

t_fm_single_quotes() {
  write_def "$(work_def)" "codex_home: '~/.codex-test'
codex_model: 'model-sq'" "$DEFAULT_BODY"
  run_wrapper "$AGENT"
  expect_rc 0
  expect_arg_pair -m model-sq
}

t_log_concurrent() {
  fake_set sleep 1
  fake_set last_message '報告
'
  fake_set stderr 'WARNING: 警告の行
hook: フックの行
経過の行
'
  local path="$root/bin:/usr/bin:/bin" pid1 pid2 rc1 rc2
  local run_one='cd "$1/work" && printf "%s" "依頼" | "$2" "$3" "$4" >"$5" 2>&1'
  (
    export USERPROFILE="$root/home" HOME="$root/home" PATH="$path" TMPDIR="$root/tmp"
    "$BASH_BIN" -c "$run_one" _ "$root" "$BASH_BIN" "$WRAPPER" "$AGENT" "$root/out1"
  ) &
  pid1=$!
  (
    export USERPROFILE="$root/home" HOME="$root/home" PATH="$path" TMPDIR="$root/tmp"
    "$BASH_BIN" -c "$run_one" _ "$root" "$BASH_BIN" "$WRAPPER" "$AGENT" "$root/out2"
  ) &
  pid2=$!
  wait "$pid1"; rc1=$?
  wait "$pid2"; rc2=$?
  expect_eq "1 つ目の終了コード" "0" "$rc1"
  expect_eq "2 つ目の終了コード" "0" "$rc2"
  check_log_path "$root/out1"
  check_log_path "$root/out2"
  local l1 l2
  l1="$(sed -n 's/^codex-agent: log=//p' "$root/out1")"
  l2="$(sed -n 's/^codex-agent: log=//p' "$root/out2")"
  [ -n "$l1" ] && [ "$l1" != "$l2" ] || fail "ログ名が衝突している: [$l1] [$l2]"
  expect_eq "ログファイルの数" "2" "$(ls "$(logs_dir)"/*.log 2>/dev/null | wc -l | tr -d ' ')"
  local f
  for f in "$(logs_dir)"/*.log; do
    [ -f "$f" ] || continue
    if grep -qE '^(WARNING|hook:)' "$f"; then
      fail "ログに WARNING か hook: の行が残っている: $f"
    fi
    grep -Fq '経過の行' "$f" || fail "ログに経過の行が無い: $f"
  done
  cp "$root/out1" "$root/out"
}

# 実行中(偽 codex が stderr を出したあと待っている間)に、標準出力へ run= と log= の行が出ている。
# 完了の前に確かめたことは、標準出力にまだ result= の行が無いことで裏付ける。
t_running_out_lines() {
  set_running_fake 3
  start_bg_wrapper "$AGENT"
  if ! wait_until 10 running_log_has '経過: 実行中の印'; then
    fail "実行中のログに印の行が現れない"
    finish_bg_wrapper
    return
  fi
  if grep -q '^codex-agent: result=' "$root/out"; then
    fail "確かめる前にラッパーが終わっていた(偽 codex の待ちが短い)"
  fi
  cp "$root/out" "$root/out.running"
  local run_line log_line run_id pid started log_name
  run_line="$(sed -n 2p "$root/out.running")"
  log_line="$(sed -n 3p "$root/out.running")"
  case "$(sed -n 1p "$root/out.running")" in
    "codex-agent: agent=$AGENT "*) ;;
    *) fail "実行中の 1 行目が監査行でない: [$(sed -n 1p "$root/out.running")]" ;;
  esac
  case "$run_line" in
    "codex-agent: run="*) ;;
    *) fail "実行中の 2 行目が run= の行でない: [$run_line]" ;;
  esac
  case "$log_line" in
    "codex-agent: log="*) ;;
    *) fail "実行中の 3 行目が log= の行でない: [$log_line]" ;;
  esac
  run_id="$(printf '%s\n' "$run_line" | sed -n 's/^codex-agent: run=\([^ ]*\) pid=[^ ]* started=[^ ]*$/\1/p')"
  pid="$(printf '%s\n' "$run_line" | sed -n 's/^codex-agent: run=[^ ]* pid=\([^ ]*\) started=[^ ]*$/\1/p')"
  started="$(printf '%s\n' "$run_line" | sed -n 's/^codex-agent: run=[^ ]* pid=[^ ]* started=\([^ ]*\)$/\1/p')"
  log_name="$(basename "${log_line#codex-agent: log=}")"
  [ -n "$run_id" ] || fail "run= の行から実行 ID を読めない: [$run_line]"
  expect_eq "実行 ID とログのファイル名" "$run_id.log" "$log_name"
  case "$run_id" in
    "$AGENT-"[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]-*) ;;
    *) fail "実行 ID が <agent>-<YYYYmmdd-HHMMSS>-<PID> の形でない: [$run_id]" ;;
  esac
  case "$pid" in
    ''|*[!0-9]*) fail "pid= の値が数字でない: [$pid]" ;;
  esac
  printf '%s\n' "$started" | grep -Eq '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' \
    || fail "started= が UTC の ISO 8601 形式でない: [$started]"
  finish_bg_wrapper
  expect_rc 0
}

# 実行中のログに、偽 codex の stderr の行が既に書かれ、先頭行が run= の行で、WARNING 行と hook 行が無い。
t_running_log() {
  set_running_fake 3
  start_bg_wrapper "$AGENT"
  if ! wait_until 10 running_log_has '経過: 実行中の印'; then
    fail "実行中のログに印の行が現れない"
    finish_bg_wrapper
    return
  fi
  local log
  log="$(out_log_path "$root/out")"
  cp "$log" "$root/log.running"
  if grep -q '^codex-agent: result=' "$root/out"; then
    fail "確かめる前にラッパーが終わっていた(偽 codex の待ちが短い)"
  fi
  expect_eq "実行中のログの 1 行目" "$(sed -n 2p "$root/out")" "$(sed -n 1p "$root/log.running")"
  case "$(sed -n 1p "$root/log.running")" in
    "codex-agent: run="*) ;;
    *) fail "実行中のログの 1 行目が run= の行でない: [$(sed -n 1p "$root/log.running")]" ;;
  esac
  if grep -qE '^(WARNING|hook:)' "$root/log.running"; then
    fail "実行中のログに WARNING か hook: の行が書かれている"
  fi
  if grep -q '^codex-agent: result=' "$root/log.running"; then
    fail "実行中のログに result= の行がある"
  fi
  finish_bg_wrapper
  expect_rc 0
}

t_log_result_ok() {
  fake_set last_message '報告
'
  fake_set stderr '経過の行
'
  run_wrapper "$AGENT"
  expect_rc 0
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
  expect_log_ends_with_result
}

t_log_result_rate_limited() {
  fake_set stderr "ERROR: You've hit your usage limit.
"
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_rc 75
  expect_eq "最後の行" "codex-agent: result=rate-limited" "$(last_out_line)"
  expect_log_ends_with_result
}

# 最終回答が改行で終わらない失敗でも、ログの result= の行は独立した行になる。
t_log_result_failed() {
  fake_set stderr 'ERROR: something went wrong
'
  fake_set stdout '改行で終わらない出力'
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_rc 1
  expect_eq "最後の行" "codex-agent: result=failed exit=1" "$(last_out_line)"
  expect_out_line "改行で終わらない出力"
  expect_log_ends_with_result
}

# Windows で、コマンドラインに <実行 ID>.last を含むプロセス(-o で最終報告の受け皿を受け取る Codex 側)の PID を 1 行ずつ出す。
# 実行 ID は環境変数で渡す。コマンドラインに実行 ID を書くと、問い合わせた PowerShell 自身が一致するためである。
win_codex_side_pids() {
  RUN_ID="$1" powershell -NoProfile -NonInteractive -Command \
    'Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like ("*" + $env:RUN_ID + ".last*") } | ForEach-Object { $_.ProcessId }' \
    2>/dev/null | tr -d '\r'
}

# Windows で、指定した PID の直接の子の PID を 1 行ずつ出す。
win_child_pids() {
  powershell -NoProfile -NonInteractive -Command \
    "Get-CimInstance Win32_Process -Filter 'ParentProcessId=$1' | ForEach-Object { \$_.ProcessId }" \
    2>/dev/null | tr -d '\r'
}

# 中断の手順を確かめる。docs/gpt-agents.md の「既知の制約」に書いた手順と同じ順で止める。
#   1. run= の行の pid を taskkill /T /F で止める。ラッパーが result= の行を書かないようにするためである。
#   2. コマンドラインに実行 ID を含む Codex 側のプロセスを taskkill /T /F で止める。
# 2 が要るのは、Git Bash が Git Bash 系のプログラム(npm のシムの sh など)を exec すると、
# 中継のプロセスが終わって Windows 上の親子関係が途切れ、1 の taskkill /T が Codex まで届かないためである。
# 偽 codex は npm のシムと同じ形(bash スクリプトが Windows の実行ファイルを exec する)をとる。
# Windows の Git Bash に限る。他の環境では PID の体系と停止の手段が違うため確かめない。
t_kill_by_run_pid() {
  local ping_exe=""
  ping_exe="$(command -v PING.EXE 2>/dev/null || command -v ping.exe 2>/dev/null)"
  if [ ! -r "/proc/$$/winpid" ] || ! command -v taskkill >/dev/null 2>&1 || ! command -v tasklist >/dev/null 2>&1 \
    || ! command -v powershell >/dev/null 2>&1 || [ -z "$ping_exe" ]; then
    skip "/proc/<pid>/winpid、taskkill、tasklist、powershell、ping.exe のいずれかが無い(Windows の Git Bash 以外)"
    return
  fi
  fake_set stderr 'WARNING: 警告の行
経過: 実行中の印
'
  fake_set ping_exe "$ping_exe"
  fake_set native_wait 30
  start_bg_wrapper "$AGENT"
  if ! wait_until 10 running_log_has '経過: 実行中の印' || [ ! -s "$root/fake/exec.winpid" ]; then
    fail "偽 codex の起動を確かめられない"
    [ -s "$root/fake/exec.winpid" ] && taskkill //T //F //PID "$(tr -d '\r\n' <"$root/fake/exec.winpid")" >/dev/null 2>&1
    finish_bg_wrapper
    return
  fi
  local pid run_id fake_pid children side p
  pid="$(sed -n 's/^codex-agent: run=[^ ]* pid=\([0-9][0-9]*\) started=.*$/\1/p' "$root/out")"
  run_id="$(sed -n 's/^codex-agent: run=\([^ ]*\) pid=.*$/\1/p' "$root/out")"
  fake_pid="$(tr -d '\r\n' <"$root/fake/exec.winpid")"
  children="$(win_child_pids "$fake_pid")"
  if [ -z "$pid" ] || [ -z "$run_id" ]; then
    fail "run= の行から pid か実行 ID を読めない"
    taskkill //T //F //PID "$fake_pid" >/dev/null 2>&1
    finish_bg_wrapper
    return
  fi
  win_pid_alive "$fake_pid" || fail "停止の前に偽 codex(PID $fake_pid)が見つからない"
  [ -n "$children" ] || fail "停止の前に偽 codex の子(ping.exe)が見つからない"

  taskkill //T //F //PID "$pid" >"$root/taskkill.out" 2>&1 || fail "ラッパーの taskkill が失敗した"
  wait_until 5 win_pid_gone "$pid" || fail "taskkill のあともラッパー(PID $pid)が残っている"

  side="$(win_codex_side_pids "$run_id")"
  printf '%s\n' "$side" | grep -Fxq -- "$fake_pid" \
    || fail "実行 ID で探した Codex 側のプロセスに偽 codex(PID $fake_pid)が無い: [$(printf '%s' "$side" | tr '\n' ' ')]"
  for p in $side; do
    taskkill //T //F //PID "$p" >>"$root/taskkill.out" 2>&1
  done
  for p in $fake_pid $children; do
    if ! wait_until 5 win_pid_gone "$p"; then
      fail "手順のあとも Codex 側のプロセス(PID $p)が残っている"
      taskkill //T //F //PID "$p" >/dev/null 2>&1
    fi
  done
  finish_bg_wrapper
  if grep -q '^codex-agent: result=' "$root/out"; then
    fail "止めたラッパーが標準出力に result= の行を出している"
  fi
  if grep -q '^codex-agent: result=' "$(out_log_path "$root/out")"; then
    fail "止めたラッパーのログに result= の行がある"
  fi
}

# .last と .out は、強制終了で EXIT の trap が動かなかった実行の残りであり、.log と同じ規則で落とす。
t_log_prune() {
  mkdir -p "$(logs_dir)"
  local ext
  for ext in log last out; do
    : >"$(logs_dir)/old-9days.$ext"
    : >"$(logs_dir)/recent-6days.$ext"
    touch -d '9 days ago' "$(logs_dir)/old-9days.$ext"
    touch -d '6 days ago' "$(logs_dir)/recent-6days.$ext"
  done
  run_wrapper "$AGENT"
  expect_rc 0
  for ext in log last out; do
    [ ! -e "$(logs_dir)/old-9days.$ext" ] || fail "9 日前の .$ext が消えていない"
    [ -e "$(logs_dir)/recent-6days.$ext" ] || fail "6 日前の .$ext が消えている"
  done
}

# 依頼文の置き場は、定義がスクラッチパッドを使えないときの退避先であり、ログと同じ期限で落とす。
t_prompts_prune() {
  local dir="$root/home/.claude/codex-agent/prompts"
  mkdir -p "$dir"
  : >"$dir/old-9days.md"
  : >"$dir/recent-6days.md"
  touch -d '9 days ago' "$dir/old-9days.md"
  touch -d '6 days ago' "$dir/recent-6days.md"
  run_wrapper "$AGENT"
  expect_rc 0
  [ -d "$dir" ] || fail "依頼文の置き場が無い"
  [ ! -e "$dir/old-9days.md" ] || fail "9 日前の依頼文が消えていない"
  [ -e "$dir/recent-6days.md" ] || fail "6 日前の依頼文が消えている"
  rm -rf "$dir"
  run_wrapper "$AGENT"
  expect_rc 0
  [ -d "$dir" ] || fail "依頼文の置き場が作られていない"
}

t_no_leftover_temp() {
  fake_set last_message '報告
'
  run_wrapper "$AGENT"
  expect_rc 0
  fake_set exit_code 1
  fake_set stderr 'ERROR: failed
'
  run_wrapper "$AGENT"
  expect_rc 1
  local left
  left="$(ls "$(logs_dir)"/*.last 2>/dev/null)"
  [ -z "$left" ] || fail "*.last が残っている: $left"
  left="$(ls "$(logs_dir)"/*.out 2>/dev/null)"
  [ -z "$left" ] || fail "*.out が残っている: $left"
  expect_eq "ログファイルの数" "2" "$(ls "$(logs_dir)"/*.log 2>/dev/null | wc -l | tr -d ' ')"
  left="$(ls -A "$root/tmp" 2>/dev/null)"
  [ -z "$left" ] || fail "TMPDIR に一時ファイルが残っている: $left"
}

t_log_permissions() {
  : >"$root/probe"
  chmod 600 "$root/probe" 2>/dev/null
  local probe
  probe="$(stat -c '%a' "$root/probe" 2>/dev/null)"
  if [ "$probe" != "600" ]; then
    skip "chmod 600 が効かない環境である(chmod 後の権限: ${probe:-取得不可})"
    return
  fi
  run_wrapper "$AGENT"
  expect_rc 0
  local log
  log="$(only_log_file)"
  expect_eq "ログファイルの権限" "600" "$(stat -c '%a' "$log" 2>/dev/null)"
  expect_eq "ログ置き場の権限" "700" "$(stat -c '%a' "$(logs_dir)" 2>/dev/null)"
}

t_simulate_rate_limit() {
  EXTRA_ENV=(CODEX_AGENT_SIMULATE_RATE_LIMIT=1)
  run_wrapper "$AGENT"
  expect_rc 75
  expect_eq "最後の行" "codex-agent: result=rate-limited (simulated)" "$(last_out_line)"
  expect_no_exec
}

t_simulate_unavailable() {
  EXTRA_ENV=(CODEX_AGENT_SIMULATE_UNAVAILABLE=1)
  run_wrapper "$AGENT"
  expect_rc 75
  expect_eq "最後の行" "codex-agent: result=unavailable (simulated)" "$(last_out_line)"
  expect_no_exec
}

t_help() {
  run_wrapper -h
  expect_rc 0
  grep -q '^用法: ' "$root/out" || fail "標準出力に用法が無い"
  expect_no_codex_call
}

t_cross_review_contract() {
  if ! command -v node >/dev/null 2>&1; then
    skip "node が無い"
    return
  fi
  local result
  result="$(node -e '
    const fs = require("fs");
    const m = require(process.argv[1]);
    process.stdout.write(String(m.scriptPinsApprovalNever(fs.readFileSync(process.argv[2], "utf8"))));
  ' "$(norm_path "$CROSS_REVIEW_JS")" "$(norm_path "$WRAPPER")" 2>"$root/err")"
  : >"$root/out"
  expect_eq "scriptPinsApprovalNever の戻り値" "true" "$result"
}

# ---------------------------------------------------------------------------
# 書き込み担当の目印
# ---------------------------------------------------------------------------

# テストの中で使う git。ユーザの設定に左右されないよう、HOME を一時ルートに向け、システムの設定を読まない。
git_t() {
  env HOME="$root/home" GIT_CONFIG_NOSYSTEM=1 "$GIT_BIN_DIR/git" \
    -c user.name=cxa-test -c user.email=cxa-test@example.invalid "$@"
}

# 目印を扱うケースの準備。git が見つからなければ SKIP にして 1 を返す。
# work を git のリポジトリにし、空のコミットを 1 つ作る。git worktree add に HEAD が要るためである。
setup_git_work() {
  if [ -z "$GIT_BIN_DIR" ]; then
    skip "git が見つからない(目印は git の管理下でだけ扱う)"
    return 1
  fi
  EXTRA_PATH="$GIT_BIN_DIR"
  if ! git_t init -q "$root/work" >/dev/null 2>&1 \
    || ! git_t -C "$root/work" commit -q --allow-empty -m init >/dev/null 2>&1; then
    fail "一時の git リポジトリを作れない"
    return 1
  fi
  return 0
}

# 作業ディレクトリの worktree 固有の git ディレクトリにある、目印の置き場。
# runs_dir_of <作業ディレクトリ>
runs_dir_of() {
  printf '%s/codex-agent/runs' "$(git_t -C "$1" rev-parse --absolute-git-dir | tr -d '\r')"
}

# 他の実行の目印を置く。中身はラッパーが置く目印と同じ 3 行の形にする。
# plant_marker <置き場> <実行 ID> <ログのパス>
plant_marker() {
  mkdir -p "$1"
  {
    printf 'codex-agent: run=%s pid=1 started=2026-01-01T00:00:00Z\n' "$2"
    printf 'codex-agent: log=%s\n' "$3"
    printf 'codex-agent: agent=cxa-other sandbox=workspace-write\n'
  } >"$1/$2.run"
}

count_markers() {
  ls "$1"/*.run 2>/dev/null | wc -l | tr -d ' '
}

expect_no_warning() {
  if grep -q '^codex-agent: warning=' "$1"; then
    fail "警告の行が出ている: [$(grep '^codex-agent: warning=' "$1" | tr '\n' ' ')]"
  fi
}

# 書き込み可能な起動は、実行中に目印を置き、正常終了で消す。
t_marker_lifecycle() {
  setup_git_work || return
  local runs run_id marker
  runs="$(runs_dir_of "$root/work")"
  set_running_fake 3
  start_bg_wrapper "$AGENT"
  if ! wait_until 10 running_log_has '経過: 実行中の印'; then
    fail "実行中のログに印の行が現れない"
    finish_bg_wrapper
    return
  fi
  run_id="$(sed -n 's/^codex-agent: run=\([^ ]*\) .*/\1/p' "$root/out" | head -n 1)"
  marker="$runs/$run_id.run"
  if [ -z "$run_id" ] || [ ! -f "$marker" ]; then
    fail "実行中に目印が無い: [$marker]"
  else
    expect_eq "目印の 1 行目" "$(sed -n 2p "$root/out")" "$(sed -n 1p "$marker")"
    expect_eq "目印の 2 行目" "$(sed -n 3p "$root/out")" "$(sed -n 2p "$marker")"
    expect_eq "目印の 3 行目" "codex-agent: agent=$AGENT sandbox=workspace-write" "$(sed -n 3p "$marker")"
    expect_eq "目印の行数" "3" "$(wc -l <"$marker" | tr -d ' ')"
  fi
  finish_bg_wrapper
  expect_rc 0
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
  [ ! -e "$marker" ] || fail "終了後に目印が残っている: $marker"
  expect_eq "終了後の置き場のファイル数" "0" "$(ls -A "$runs" 2>/dev/null | wc -l | tr -d ' ')"
  expect_no_warning "$root/out"
}

# 同じ worktree で書き込み可能な起動を重ねると、2 つ目にだけ 1 つ目の実行 ID を含む警告が出る。
t_marker_same_worktree_warns() {
  setup_git_work || return
  local id1 log1 log2 warning
  set_running_fake 3
  start_bg_wrapper "$AGENT"
  if ! wait_until 10 running_log_has '経過: 実行中の印'; then
    fail "実行中のログに印の行が現れない"
    finish_bg_wrapper
    return
  fi
  id1="$(sed -n 's/^codex-agent: run=\([^ ]*\) .*/\1/p' "$root/out" | head -n 1)"
  log1="$(out_log_path "$root/out")"
  warning="codex-agent: warning=concurrent-writer run=$id1 log=$log1"
  RUN_OUT="$root/out2"
  run_wrapper "$AGENT"
  RUN_OUT=""
  expect_eq "2 つ目の終了コード" "0" "$RC"
  expect_eq "2 つ目の最後の行" "codex-agent: result=ok" "$(tail -n 1 "$root/out2")"
  grep -Fxq -- "$warning" "$root/out2" || fail "2 つ目の出力に警告の行が無い: [$warning]"
  expect_eq "2 つ目の 4 行目(log= の行の後)" "$warning" "$(sed -n 4p "$root/out2")"
  log2="$(out_log_path "$root/out2")"
  grep -Fxq -- "$warning" "$log2" || fail "2 つ目のログに警告の行が無い"
  finish_bg_wrapper
  expect_rc 0
  expect_eq "1 つ目の最後の行" "codex-agent: result=ok" "$(last_out_line)"
  expect_no_warning "$root/out"
  expect_eq "終了後の目印の数" "0" "$(count_markers "$(runs_dir_of "$root/work")")"
}

# 同じリポジトリの別の worktree で重ねた起動には、警告が出ない。
t_marker_other_worktree_no_warn() {
  setup_git_work || return
  if ! git_t -C "$root/work" worktree add -q --detach "$root/work2" >/dev/null 2>&1; then
    fail "2 つ目の worktree を作れない"
    return
  fi
  set_running_fake 3
  start_bg_wrapper "$AGENT"
  if ! wait_until 10 running_log_has '経過: 実行中の印'; then
    fail "実行中のログに印の行が現れない"
    finish_bg_wrapper
    return
  fi
  RUN_OUT="$root/out2"
  run_wrapper "$AGENT" -C "$root/work2"
  RUN_OUT=""
  expect_eq "2 つ目の終了コード" "0" "$RC"
  expect_eq "2 つ目の最後の行" "codex-agent: result=ok" "$(tail -n 1 "$root/out2")"
  expect_no_warning "$root/out2"
  finish_bg_wrapper
  expect_rc 0
  expect_no_warning "$root/out"
}

# ログの最後の行が result= でない目印(途中で止められた実行)が残っていると、警告を出し、その目印を消さない。
t_marker_interrupted_warns() {
  setup_git_work || return
  local runs stale_log="$root/stale.log"
  runs="$(runs_dir_of "$root/work")"
  printf 'codex-agent: run=cxa-stale-1 pid=1 started=2026-01-01T00:00:00Z\n経過の行\n' >"$stale_log"
  plant_marker "$runs" cxa-stale-1 "$stale_log"
  fake_set last_message '報告
'
  run_wrapper "$AGENT"
  expect_rc 0
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
  expect_out_line "codex-agent: warning=concurrent-writer run=cxa-stale-1 log=$stale_log"
  grep -Fxq -- "codex-agent: warning=concurrent-writer run=cxa-stale-1 log=$stale_log" "$(out_log_path "$root/out")" \
    || fail "ログに警告の行が無い"
  [ -f "$runs/cxa-stale-1.run" ] || fail "途中で止められた実行の目印が消された"
  expect_eq "終了後の目印の数" "1" "$(count_markers "$runs")"
}

# ログの最後の行が result= の目印と、ログが存在しない目印は、起動時に消され、警告は出ない。
t_marker_finished_removed() {
  setup_git_work || return
  local runs done_log="$root/done.log"
  runs="$(runs_dir_of "$root/work")"
  printf 'codex-agent: run=cxa-done-1 pid=1 started=2026-01-01T00:00:00Z\n経過の行\ncodex-agent: result=ok\n' >"$done_log"
  plant_marker "$runs" cxa-done-1 "$done_log"
  plant_marker "$runs" cxa-gone-1 "$root/no-such.log"
  fake_set last_message '報告
'
  run_wrapper "$AGENT"
  expect_rc 0
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
  expect_no_warning "$root/out"
  [ ! -e "$runs/cxa-done-1.run" ] || fail "終わった実行の目印が残っている"
  [ ! -e "$runs/cxa-gone-1.run" ] || fail "ログが無い目印が残っている"
  expect_eq "終了後の目印の数" "0" "$(count_markers "$runs")"
}

# read-only の起動は目印を置かず、残った目印があっても調べない。
t_marker_read_only() {
  setup_git_work || return
  local runs stale_log="$root/stale.log"
  runs="$(runs_dir_of "$root/work")"
  write_def "$(work_def)" 'codex_home: ~/.codex-test
codex_model: model-test
codex_reasoning_effort: low
codex_sandbox: read-only' "$DEFAULT_BODY"
  printf 'codex-agent: run=cxa-stale-1 pid=1 started=2026-01-01T00:00:00Z\n経過の行\n' >"$stale_log"
  plant_marker "$runs" cxa-stale-1 "$stale_log"
  set_running_fake 2
  start_bg_wrapper "$AGENT"
  if ! wait_until 10 running_log_has '経過: 実行中の印'; then
    fail "実行中のログに印の行が現れない"
    finish_bg_wrapper
    return
  fi
  expect_eq "実行中の目印の数" "1" "$(count_markers "$runs")"
  finish_bg_wrapper
  expect_rc 0
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
  expect_no_warning "$root/out"
  [ -f "$runs/cxa-stale-1.run" ] || fail "read-only の起動が残った目印を消した"
  expect_eq "終了後の置き場のファイル数" "1" "$(ls -A "$runs" | wc -l | tr -d ' ')"
}

# git の管理下に無い作業ディレクトリでも、従来どおり起動して成功し、目印も警告も無い。
# 一時ディレクトリの上位にあるリポジトリを見つけないよう、git の探索を一時ルートで止める。
t_marker_not_git() {
  if [ -z "$GIT_BIN_DIR" ]; then
    skip "git が見つからない(git がある環境で管理下に無い場合を確かめるケースである)"
    return
  fi
  EXTRA_PATH="$GIT_BIN_DIR"
  EXTRA_ENV=(GIT_CEILING_DIRECTORIES="$(norm_path "$root")")
  fake_set last_message '報告
'
  run_wrapper "$AGENT"
  expect_rc 0
  expect_eq "最後の行" "codex-agent: result=ok" "$(last_out_line)"
  expect_no_warning "$root/out"
  local found
  found="$(find "$root" -path '*codex-agent/runs*' 2>/dev/null)"
  [ -z "$found" ] || fail "git の管理下に無いのに目印の置き場がある: $found"
}

# 警告の行に 429 を含む実行 ID とパスが出ても、利用上限と判定しない。警告の行は標準出力に 1 回だけ出る。
t_marker_warning_not_classified() {
  setup_git_work || return
  local runs stale_log="$root/stale-429.log" warning
  runs="$(runs_dir_of "$root/work")"
  printf 'codex-agent: run=cxa-429-429 pid=429 started=2026-01-01T00:00:00Z\n経過の行\n' >"$stale_log"
  plant_marker "$runs" cxa-429-429 "$stale_log"
  warning="codex-agent: warning=concurrent-writer run=cxa-429-429 log=$stale_log"
  fake_set stderr 'ERROR: something went wrong
'
  fake_set exit_code 1
  run_wrapper "$AGENT"
  expect_rc 1
  expect_eq "最後の行" "codex-agent: result=failed exit=1" "$(last_out_line)"
  expect_out_no_match "evidence:"
  expect_eq "標準出力の警告の行数" "1" "$(grep -Fxc -- "$warning" "$root/out")"
}

# リポジトリに置く GPT 側の定義(出荷既定値)が、CLAUDE.md の「認証ホームの配置」と「権限の固定」に従う。
# codex-review だけが通常利用のアカウント(~/.codex)で read-only に動き、残る 4 定義はサブエージェント専用の
# アカウント(~/.codex-subagent)で書き込み可能に動く。
# 設定コンソールが書き換えるのはユーザ側の定義なので、リポジトリ側の値はここで固定してよい。
# 値の取り出し方はラッパーの fm_get と揃える(行末コメントと引用符を除く)。
shipped_fm_get() {
  sed 's/\r$//' "$1" \
    | awk 'NR == 1 { if ($0 != "---") exit; next } $0 == "---" { exit } { print }' \
    | sed -n "s/^$2:[[:space:]]*//p" | head -n 1 \
    | sed -e 's/[[:space:]][[:space:]]*#.*$//' -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' \
          -e 's/^"\(.*\)"$/\1/' -e "s/^'\(.*\)'\$/\1/"
}

t_shipped_definitions() {
  local name def want_home want_sandbox
  while read -r name want_home want_sandbox; do
    def="$repo_root/.claude/gpt-agents/$name.md"
    if [ ! -f "$def" ]; then
      fail "定義が無い: $def"
      continue
    fi
    expect_eq "$name の codex_home" "$want_home" "$(shipped_fm_get "$def" codex_home)"
    expect_eq "$name の codex_sandbox" "$want_sandbox" "$(shipped_fm_get "$def" codex_sandbox)"
  done <<'TABLE'
codex-review ~/.codex read-only
codex-subagent ~/.codex-subagent workspace-write
impl-hard ~/.codex-subagent workspace-write
impl-light ~/.codex-subagent workspace-write
impl-standard ~/.codex-subagent workspace-write
TABLE
}

t_real_home_untouched() {
  local dir="$REAL_HOME/.claude/codex-agent/logs" found
  found="$(ls -A "$dir" 2>/dev/null | grep -F -- "$AGENT")"
  [ -z "$found" ] || fail "実ホームのログ置き場にテスト用のログがある: $found"
  : >"$root/out"
  : >"$root/err"
}

# ---------------------------------------------------------------------------
# 実行
# ---------------------------------------------------------------------------

N=0
PASSED=0
FAILED=0
SKIPPED=0

run_case() {
  local name="$1" fn="$2"
  N=$((N + 1))
  FAILS=""
  SKIP_REASON=""
  RC=""
  new_case_root
  : >"$root/out"
  : >"$root/err"
  "$fn"
  if [ -n "$SKIP_REASON" ]; then
    printf 'ok %d - %s # SKIP %s\n' "$N" "$name" "$SKIP_REASON"
    SKIPPED=$((SKIPPED + 1))
  elif [ -z "$FAILS" ]; then
    printf 'ok %d - %s\n' "$N" "$name"
    PASSED=$((PASSED + 1))
  else
    printf 'not ok %d - %s\n' "$N" "$name"
    printf '%s' "$FAILS" | sed 's/^/#   /'
    printf '#   終了コード: %s\n' "$RC"
    printf '#   標準出力(先頭 20 行):\n'
    head -n 20 "$root/out" | sed 's/^/#     | /'
    printf '#   標準エラー(先頭 10 行):\n'
    head -n 10 "$root/err" | sed 's/^/#     | /'
    FAILED=$((FAILED + 1))
  fi
  rm -rf "$root"
}

run_case "認証ホーム: exec --help と exec に定義の CODEX_HOME が渡る" t_auth_home
run_case "起動引数: sandbox、-m、effort、-C、approval_policy=never が 1 回" t_args_basic
run_case "起動引数: --effort high が定義の値より優先する" t_args_effort_override
run_case "起動引数: effort の既定は medium、sandbox の既定は read-only" t_args_defaults
run_case "起動引数: -C <dir> の値が渡る" t_args_workdir
run_case "依頼文: 役割文、---、## 依頼、依頼文の順で渡る" t_prompt_with_role
run_case "依頼文: 役割文が無い定義では依頼文だけが渡る" t_prompt_without_role
run_case "成功時の出力: 監査行、run=、log=、最終報告、result=ok の順で、経過はログにだけ残る" t_success_output
run_case "--output-last-message が無い版: -o を渡さず標準出力の末尾 40 行を報告にする" t_no_output_last_message
run_case "--output-last-message の判定: ヘルプの途中で読み手が終わっても有る版と判定する" t_help_detect_late_writer
run_case "最終報告が空: 標準出力の末尾を報告にする" t_empty_last_message
run_case "最終報告が改行で終わらない: result=ok が独立した行になる" t_last_message_no_newline
run_case "利用上限(stderr): 75、evidence の直後に result=rate-limited" t_rate_limit_stderr
run_case "利用上限(stdout): 75、evidence の直後に result=rate-limited" t_rate_limit_stdout
run_case "利用上限(根拠 2 行): evidence を 1 行ずつ接頭辞付きで result の直前に並べる" t_rate_limit_multi_evidence
run_case "モデルの混雑: 75、evidence の直後に result=unavailable" t_unavailable
run_case "ツール接続の失敗(終了コード 0、成功行なし): 75、evidence の直後に result=unavailable" t_tool_handshake_failed_exit0
run_case "ツール接続の失敗後に復旧(終了コード 0、成功行あり): result=ok" t_tool_handshake_recovered_exit0
run_case "通常の失敗: 1、result=failed exit=1" t_plain_failure
run_case "Codex 自身の 75: 1 に写像し result=failed exit=75" t_codex_exit_75
run_case "成功の本文に上限の語があっても result=ok" t_success_body_mentions_limit
run_case "ログの中ほどだけにある上限の語では分類しない" t_limit_word_mid_log
run_case "429 の単語境界: id=14290 は上限と見なさない" t_429_id_not_matched
run_case "429 の単語境界: HTTP 429 は上限と見なす" t_429_word_matched
run_case "終了コード 2: エージェント名なし" t_exit2_no_agent
run_case "終了コード 2: 不明なオプション" t_exit2_unknown_option
run_case "終了コード 2: / を含むエージェント名" t_exit2_slash_in_name
run_case "終了コード 2: 不正な --effort" t_exit2_bad_effort_option
run_case "終了コード 2: 定義の不正な codex_reasoning_effort" t_exit2_bad_effort_def
run_case "終了コード 2: 不正な codex_sandbox" t_exit2_bad_sandbox
run_case "終了コード 2: 不正な codex_enabled" t_exit2_bad_enabled
run_case "終了コード 2: codex_home キーなし" t_exit2_no_codex_home_key
run_case "終了コード 2: codex_home のディレクトリが無い" t_exit2_codex_home_missing
run_case "終了コード 2: -C のディレクトリが無い" t_exit2_workdir_missing
run_case "終了コード 2: 空白だけの依頼文" t_exit2_blank_request
run_case "終了コード 3: 定義が見つからない" t_exit3_no_def
run_case "終了コード 3: codex が PATH に無い" t_exit3_no_codex
run_case "終了コード 3: codex_enabled: false" t_exit3_disabled
run_case "終了コード 3: codex_model キーなし" t_exit3_no_model_key
run_case "終了コード 3: codex_model が空" t_exit3_empty_model
run_case "定義の探索: カレントの定義がホームより優先する" t_lookup_work_first
run_case "定義の探索: カレントに無ければホームの定義を使う" t_lookup_home_fallback
run_case "フロントマター: 行末コメントを除く" t_fm_trailing_comment
run_case "フロントマター: ダブルクォートを除く" t_fm_double_quotes
run_case "フロントマター: シングルクォートを除く" t_fm_single_quotes
run_case "ログ: 同時起動でログ名が衝突せず、WARNING と hook: の行を除く" t_log_concurrent
run_case "実行中の出力: Codex の実行中に run= と log= の行が出ており、実行 ID と started= の形が正しい" t_running_out_lines
run_case "実行中のログ: 先頭が run= の行で、stderr の行が既に書かれ、WARNING と hook: の行が無い" t_running_log
run_case "完了後のログ(成功): 最後の行が標準出力の result=ok と一致し、result= は 1 回" t_log_result_ok
run_case "完了後のログ(利用上限): 最後の行が標準出力の result=rate-limited と一致し、result= は 1 回" t_log_result_rate_limited
run_case "完了後のログ(通常の失敗): 最後の行が標準出力の result=failed と一致し、result= と run= は 1 回" t_log_result_failed
run_case "停止: run= の pid と実行 ID を含む Codex 側を taskkill /T /F で止めると、偽 codex まで止まり result= が残らない" t_kill_by_run_pid
run_case "ログ: 9 日前の .log、.last、.out を消し、6 日前のものを残す" t_log_prune
run_case "依頼文の置き場: 作られ、9 日前のファイルを消し、6 日前のものを残す" t_prompts_prune
run_case "一時ファイル: 成功と失敗のあとに *.last、*.out、TMPDIR の一時ファイルが残らず、ログは残る" t_no_leftover_temp
run_case "ログの権限: ログファイル 600、ログ置き場 700" t_log_permissions
run_case "試験用フック: CODEX_AGENT_SIMULATE_RATE_LIMIT" t_simulate_rate_limit
run_case "試験用フック: CODEX_AGENT_SIMULATE_UNAVAILABLE" t_simulate_unavailable
run_case "-h: 終了コード 0 で用法を出す" t_help
run_case "目印: 書き込み可能な起動は実行中に <git ディレクトリ>/codex-agent/runs/<実行 ID>.run を置き、正常終了で消す" t_marker_lifecycle
run_case "目印: 同じ worktree で重ねると、2 つ目の出力とログにだけ 1 つ目の実行 ID を含む警告が出て、どちらも result=ok" t_marker_same_worktree_warns
run_case "目印: 同じリポジトリの別の worktree で重ねた起動には警告が出ない" t_marker_other_worktree_no_warn
run_case "目印: ログの最後の行が result= でない目印は警告を出し、消さない" t_marker_interrupted_warns
run_case "目印: ログの最後の行が result= の目印とログが無い目印は消し、警告を出さない" t_marker_finished_removed
run_case "目印: read-only の起動は目印を置かず、残った目印に警告を出さない" t_marker_read_only
run_case "目印: git の管理下に無い作業ディレクトリでは目印も警告も無く成功する" t_marker_not_git
run_case "目印: 警告の行の 429 で利用上限と判定せず、警告の行は標準出力に 1 回" t_marker_warning_not_classified
run_case "ai-cross-review との契約: scriptPinsApprovalNever が true を返す" t_cross_review_contract
run_case "出荷既定の定義: 5 定義の codex_home と codex_sandbox が CLAUDE.md の対応に従う" t_shipped_definitions
run_case "環境の分離: 実ホームのログ置き場にテスト用のログが無い" t_real_home_untouched

printf '# 合計 %d 件: 成功 %d、失敗 %d、SKIP %d\n' "$N" "$PASSED" "$FAILED" "$SKIPPED"
[ "$FAILED" -eq 0 ]
