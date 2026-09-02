#!/usr/bin/env bash
#
# Claude Code のサブエージェント定義(.claude/agents/<name>.md)を読み、
# そのフロントマターの指定に従って Codex CLI(codex exec)を起動する。
#
# 用法:
#   bash tools/codex-agent.sh <agent-name> [-C <workdir>] [--effort <level>] < prompt.txt
#
# 依頼文は標準入力から読む。引数に埋め込むと引用符の扱いで壊れやすいためである。
# 呼び出し側はヒアドキュメントで渡す。

set -u
set -o pipefail

usage() {
  cat <<'USAGE'
用法: bash tools/codex-agent.sh <agent-name> [-C <workdir>] [--effort <level>] < prompt.txt

  <agent-name>      .claude/agents/<agent-name>.md の名前
  -C <workdir>      Codex の作業ディレクトリ(既定はカレントディレクトリ)
  --effort <level>  推論 effort を定義ファイルの値より優先して指定する

依頼文は標準入力から読む。
USAGE
}

die() {
  printf 'codex-agent: %s\n' "$1" >&2
  exit 2
}

agent_name=""
workdir=""
effort_override=""

while [ $# -gt 0 ]; do
  case "$1" in
    -C)
      [ $# -ge 2 ] || die "-C には作業ディレクトリが必要である"
      workdir="$2"
      shift 2
      ;;
    --effort)
      [ $# -ge 2 ] || die "--effort には値が必要である"
      effort_override="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    -*)
      die "不明なオプションである: $1"
      ;;
    *)
      [ -z "$agent_name" ] || die "エージェント名は 1 つだけ指定する: $1"
      agent_name="$1"
      shift
      ;;
  esac
done

[ -n "$agent_name" ] || { usage >&2; die "エージェント名が指定されていない"; }

home_dir="${USERPROFILE:-$HOME}"

# Windows のパスを Git Bash が扱える形(スラッシュ区切り)に揃える。
to_slash() {
  local p="$1"
  printf '%s' "${p//\\//}"
}

# 定義ファイルを探す。プロジェクト定義がユーザ定義を上書きする。
def_file=""
for candidate in "$PWD/.claude/agents/$agent_name.md" "$(to_slash "$home_dir")/.claude/agents/$agent_name.md"; do
  if [ -f "$candidate" ]; then
    def_file="$candidate"
    break
  fi
done
[ -n "$def_file" ] || die "エージェント定義が見つからない: $agent_name (.claude/agents/$agent_name.md)"

# フロントマター(先頭の --- から次の --- まで)を取り出す。
front_matter="$(sed 's/\r$//' "$def_file" | awk '
  NR == 1 { if ($0 != "---") exit; next }
  $0 == "---" { exit }
  { print }
')"
[ -n "$front_matter" ] || die "フロントマターが読めない: $def_file"

# フロントマターから 1 キーの値を取り出し、行末の YAML コメント、前後の空白、引用符を除く。
fm_get() {
  local raw
  raw="$(printf '%s\n' "$front_matter" | sed -n "s/^$1:[[:space:]]*//p" | head -n 1)"
  raw="${raw%%#*}"
  raw="$(printf '%s' "$raw" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
  case "$raw" in
    \"*\") raw="${raw#\"}"; raw="${raw%\"}" ;;
    \'*\') raw="${raw#\'}"; raw="${raw%\'}" ;;
  esac
  printf '%s' "$raw"
}

codex_home="$(fm_get codex_home)"
codex_model="$(fm_get codex_model)"
codex_effort="$(fm_get codex_reasoning_effort)"
codex_sandbox="$(fm_get codex_sandbox)"

[ -n "$codex_home" ] || die "フロントマターに codex_home が無い: $def_file"
[ -n "$codex_model" ] || die "フロントマターに codex_model が無い: $def_file"
[ -n "$codex_effort" ] || codex_effort="medium"
[ -n "$codex_sandbox" ] || codex_sandbox="workspace-write"

case "$codex_sandbox" in
  read-only|workspace-write) ;;
  *) die "codex_sandbox の値が不正である: $codex_sandbox (read-only または workspace-write)" ;;
esac

[ -z "$effort_override" ] || codex_effort="$effort_override"

# codex_home の ~ と USERPROFILE を実パスへ展開する。
case "$codex_home" in
  "~") codex_home="$home_dir" ;;
  "~/"*) codex_home="$home_dir/${codex_home#\~/}" ;;
esac
codex_home="${codex_home//\$USERPROFILE/$home_dir}"
codex_home="${codex_home//%USERPROFILE%/$home_dir}"
codex_home="$(to_slash "$codex_home")"

[ -d "$codex_home" ] || die "codex_home が存在しない: $codex_home (docs/setup.md のログイン手順を参照)"

[ -n "$workdir" ] || workdir="$PWD"
[ -d "$workdir" ] || die "作業ディレクトリが存在しない: $workdir"

# 「## Codex への指示」見出しから末尾までを役割文として取り出す。
role_body=""
role_start="$(sed 's/\r$//' "$def_file" | grep -n '^## Codex への指示' | head -n 1 | cut -d: -f1)"
if [ -n "$role_start" ]; then
  role_body="$(sed 's/\r$//' "$def_file" | tail -n +"$((role_start + 1))" | sed -e '/./,$!d')"
fi

request="$(cat)"

if [ -n "$role_body" ]; then
  prompt="$role_body

---

## 依頼

$request"
else
  prompt="$request"
fi

printf 'codex-agent: agent=%s model=%s effort=%s sandbox=%s codex_home=%s workdir=%s\n' \
  "$agent_name" "$codex_model" "$codex_effort" "$codex_sandbox" "$codex_home" "$workdir"

# 認証ホームは常に明示する。既定の ~/.codex への暗黙依存を作らない。
# --dangerously-bypass-approvals-and-sandbox は付けない。
CODEX_HOME="$codex_home" codex exec \
  --skip-git-repo-check \
  --sandbox "$codex_sandbox" \
  -m "$codex_model" \
  -c "model_reasoning_effort=$codex_effort" \
  -C "$workdir" \
  - <<<"$prompt" 2>&1 | grep -v -e '^WARNING' -e '^hook:'

exit "${PIPESTATUS[0]}"
