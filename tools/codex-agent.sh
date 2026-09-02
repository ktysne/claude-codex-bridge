#!/usr/bin/env bash
#
# GPT 系エージェント定義(.claude/gpt-agents/<name>.md)を読み、
# そのフロントマターの指定に従って Codex CLI(codex exec)を起動する。
#
# 用法:
#   bash tools/codex-agent.sh <agent-name> [-C <workdir>] [--effort <level>] < prompt.txt
#
# 依頼文は標準入力から読む。引数に埋め込むと引用符の扱いで壊れやすいためである。
# 呼び出し側はヒアドキュメントで渡す。
#
# 終了コード:
#   0   Codex が正常に終了した
#   2   引数、定義ファイル、環境の不備でスクリプトが起動しなかった
#   75  Codex がレートリミットで実行できなかった(呼び出し側は Claude へフォールバックする)
#   他  Codex の終了コードをそのまま返す

set -u
set -o pipefail

usage() {
  cat <<'USAGE'
用法: bash tools/codex-agent.sh <agent-name> [-C <workdir>] [--effort <level>] < prompt.txt

  <agent-name>      .claude/gpt-agents/<agent-name>.md の名前
  -C <workdir>      Codex の作業ディレクトリ(既定はカレントディレクトリ)
  --effort <level>  推論 effort を定義ファイルの値より優先して指定する
                    (low|medium|high|xhigh|max)

依頼文は標準入力から読む。
USAGE
}

die() {
  printf 'codex-agent: %s\n' "$1" >&2
  exit 2
}

# effort は Codex が受け付ける値だけを通す。誤った値を渡すと codex 側で失敗するためである。
validate_effort() {
  case "$1" in
    low|medium|high|xhigh|max) ;;
    *) die "effort の値が不正である: $1 (low|medium|high|xhigh|max)" ;;
  esac
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

# エージェント名はそのままパスに埋め込むため、定義ディレクトリの外へ出る形を拒む。
case "$agent_name" in
  */*|*\\*|.*) die "エージェント名に / \\ と先頭の . は使えない: $agent_name" ;;
esac

[ -z "$effort_override" ] || validate_effort "$effort_override"

home_dir="${USERPROFILE:-$HOME}"

# Windows のパスを Git Bash が扱える形(スラッシュ区切り)に揃える。
to_slash() {
  local p="$1"
  printf '%s' "${p//\\//}"
}

# 定義ファイルを探す。プロジェクト定義がユーザ定義を上書きする。
def_file=""
for candidate in "$PWD/.claude/gpt-agents/$agent_name.md" "$(to_slash "$home_dir")/.claude/gpt-agents/$agent_name.md"; do
  if [ -f "$candidate" ]; then
    def_file="$candidate"
    break
  fi
done
[ -n "$def_file" ] || die "エージェント定義が見つからない: $agent_name (.claude/gpt-agents/$agent_name.md)"

# フロントマター(先頭の --- から次の --- まで)を取り出す。
front_matter="$(sed 's/\r$//' "$def_file" | awk '
  NR == 1 { if ($0 != "---") exit; next }
  $0 == "---" { exit }
  { print }
')"
[ -n "$front_matter" ] || die "フロントマターが読めない: $def_file"

# フロントマターから 1 キーの値を取り出し、行末の YAML コメント、前後の空白、引用符を除く。
# コメントと見なすのは「空白 + #」以降だけである。値の内部の # を巻き込まないためである。
fm_get() {
  local raw
  raw="$(printf '%s\n' "$front_matter" | sed -n "s/^$1:[[:space:]]*//p" | head -n 1)"
  raw="$(printf '%s' "$raw" | sed -e 's/[[:space:]][[:space:]]*#.*$//' -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
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

validate_effort "$codex_effort"

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
workdir="$(to_slash "$workdir")"
[ -d "$workdir" ] || die "作業ディレクトリが存在しない: $workdir"

# フロントマター直後から末尾までを役割文として渡す。先頭の空行は落とす。
role_body="$(sed 's/\r$//' "$def_file" | awk '
  NR == 1 { if ($0 != "---") exit; state = 1; next }
  state == 1 && $0 == "---" { state = 2; next }
  state == 2 { print }
' | sed -e '/./,$!d')"

# 依頼文は標準入力から読む。端末から起動されたときは待ち続けてしまうので先に止める。
[ -t 0 ] && die "依頼文が標準入力から渡されていない"
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

# 試験用フック。フォールバック経路(終了コード 75)の確認にだけ使う。
# 通常の運用では設定しない。
if [ "${CODEX_AGENT_SIMULATE_RATE_LIMIT:-}" = "1" ]; then
  printf 'codex-agent: result=rate-limited (simulated)\n'
  exit 75
fi

out_file="$(mktemp)"
err_file="$(mktemp)"
trap 'rm -f "$out_file" "$err_file"' EXIT

# 認証ホームは常に明示する。既定の ~/.codex への暗黙依存を作らない。
# --dangerously-bypass-approvals-and-sandbox は付けない。
CODEX_HOME="$codex_home" codex exec \
  --skip-git-repo-check \
  --sandbox "$codex_sandbox" \
  -m "$codex_model" \
  -c "model_reasoning_effort=\"$codex_effort\"" \
  -C "$workdir" \
  - <<<"$prompt" >"$out_file" 2>"$err_file"
codex_status=$?

# 標準エラーからはフックと警告の行だけを除く。
# 標準出力は Codex の回答本文なので、フィルタを掛けずにそのまま出す。
grep -v -e '^WARNING' -e '^hook:' "$err_file"
cat "$out_file"

if [ "$codex_status" -ne 0 ]; then
  if grep -qiE 'usage limit|rate limit|too many requests|429' "$err_file"; then
    printf 'codex-agent: result=rate-limited\n'
    exit 75
  fi
  printf 'codex-agent: result=failed exit=%s\n' "$codex_status"
  exit "$codex_status"
fi

printf 'codex-agent: result=ok\n'
