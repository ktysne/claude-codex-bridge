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
# 標準出力(Codex を起動する場合):
#   codex-agent: agent=... model=... effort=... sandbox=... codex_home=... workdir=...   監査行
#   codex-agent: run=<実行 ID> pid=<ラッパーの PID> started=<UTC の ISO 8601>            Codex の起動前に出す
#   codex-agent: log=<ログのパス>                                                         Codex の起動前に出す
#   codex-agent: warning=concurrent-writer run=<相手の実行 ID> log=<相手のログのパス>      該当する目印ごとに 1 行(無ければ出さない)
#   <最終報告>(失敗時はログの末尾と根拠の行)
#   codex-agent: result=...
# run= と log= の行を起動前に出すのは、呼び出し側がバックグラウンドへ移ったあとも、
# 実行中にログの場所と止める対象を特定できるようにするためである。
# 実行 ID はログファイル名から拡張子を除いたものである。
# PID は Git Bash では /proc/$$/winpid の Windows の PID、読めない環境では $$ である。
# ラッパーは停止のシグナルを受けて子を止める処理を持たない。
# Claude Code の Bash ツールがバックグラウンドのコマンドを止めても、ラッパーにシグナルが届かないためである。
# Windows で止めるときは、この pid を taskkill /T /F で止めたあと、
# コマンドラインに <実行 ID>.last(-o に渡す最終報告の受け皿)を含む Codex 側のプロセスも止める。
# Git Bash が Git Bash 系のプログラム(npm のシムの sh など)を起動すると Windows 上の親子関係が途切れ、
# ラッパーの pid への taskkill /T だけでは Codex まで届かないためである。手順は docs/gpt-agents.md の「既知の制約」にある。
#
# 実行ログ(~/.claude/codex-agent/logs/<実行 ID>.log):
#   1 行目は run= の行と同じ内容である。標準出力へ warning= の行を出すときは、続けて同じ行を書く。
#   Codex の標準エラーは実行中から行ごとに追記する。続けて、終了後に Codex の標準出力を追記する。
#   最後の行は標準出力へ出すのと同じ result= の行である。ログだけで完了と結果を判定できるようにするためである。
#   Codex の起動前に止まる経路(終了コード 2、3、試験用フック)ではログを作らない。
#
# 書き込み担当の目印(<worktree 固有の git ディレクトリ>/codex-agent/runs/<実行 ID>.run):
#   1 つの worktree に同時に書き込む担当は 1 つとする。
#   ファイルを分けても、ビルドの生成物、テストの実行、git の索引は共有されるためである。
#   codex_sandbox が workspace-write の起動だけが、作業ディレクトリの worktree に目印を置き、終了時に消す。
#   中身は run= の行、log= の行、agent= と sandbox= の行の 3 行である。
#   目印を置く前に同じ置き場の他の目印を調べる。
#   相手のログが無いか、ログの最後の行が result= の行なら、終わった実行の目印として消す。
#   それ以外は実行中か、強制終了で残った実行の目印なので、消さずに warning=concurrent-writer の行を出す。
#   警告は観測の補助であり、起動は止めない。
#   強制終了で残った目印を実行中と区別できないため、止める形にすると、その worktree の委譲が誤って止まりうるためである。
#   作業ディレクトリが git の管理下に無い場合と、目印の読み書きに失敗した場合は、目印を扱わずに起動する。
#   同じ worktree を編集するメインセッションは、このラッパーを通らないため目印に現れない。
#
# 終了コード:
#   0   Codex が正常に終了した
#   2   引数、定義ファイルの内容、環境の不備でスクリプトが起動しなかった
#   3   GPT 側が未導入、無効化、または未設定である
#       (codex コマンドが無い、定義ファイルが無い、codex_enabled: false、または codex_model が無いか空)
#       呼び出し側は Claude へフォールバックする
#   75  呼び出し側では直せない GPT 側の事情で実行できなかった(呼び出し側は Claude へフォールバックする)
#       利用上限なら result=rate-limited、モデルの混雑など他の事情なら result=unavailable
#   他  Codex の終了コードをそのまま返す
#       ただし Codex 自身が 75 で終了した場合は 75 の意味と衝突するため 1 に写像し、
#       元の値は codex-agent: result=failed exit=75 の行に残す

set -u
set -o pipefail

# 本体を main 関数に包み、末尾で 1 回だけ呼ぶ。
# bash はスクリプトを読みながら実行するため、Codex がこのファイル自身を書き換える依頼を
# 処理すると、実行中の bash が書き換え後の内容を途中から読んで構文エラーになる。
# 関数に包むと呼び出し前に全体を読み終えるので、実行中の書き換えに影響されない。
main() {

usage() {
  cat <<'USAGE'
用法: bash tools/codex-agent.sh <agent-name> [-C <workdir>] [--effort <level>] < prompt.txt

  <agent-name>      .claude/gpt-agents/<agent-name>.md の名前
  -C <workdir>      Codex の作業ディレクトリ(既定はカレントディレクトリ)
  --effort <level>  推論 effort を定義ファイルの値より優先して指定する
                    (low|medium|high|xhigh|max|ultra)

依頼文は標準入力から読む。

標準出力は、監査行、run= の行、log= の行、最終報告、result= の行の順である。
run= の行と log= の行は Codex の起動前に出す。
書き込み可能な定義では、同じ worktree に書き込み可能な別の実行が残っていると、
log= の行の後に codex-agent: warning=concurrent-writer の行を出す(起動は止めない)。
実行中の委譲を止める手順は docs/gpt-agents.md の「既知の制約」にある。

終了コード:
  0   Codex が正常に終了した
  2   引数、定義ファイルの内容、環境の不備でスクリプトが起動しなかった
  3   GPT 側が未導入、無効化、または未設定である
      (codex コマンドが無い、定義ファイルが無い、codex_enabled: false、または codex_model が無いか空)
  75  呼び出し側では直せない GPT 側の事情で実行できなかった
      (利用上限なら result=rate-limited、モデルの混雑など他の事情なら result=unavailable)
  他  Codex の終了コードをそのまま返す(Codex 自身の 75 は 1 に写像する)
USAGE
}

die() {
  printf 'codex-agent: %s\n' "$1" >&2
  exit 2
}

# GPT 側が未導入、無効化、または未設定であることを示す。呼び出し側はこの終了コードで Claude へフォールバックする。
die_missing() {
  printf 'codex-agent: %s\n' "$1" >&2
  printf 'codex-agent: result=failed exit=3\n'
  exit 3
}

# effort は Codex が受け付ける値だけを通す。誤った値を渡すと codex 側で失敗するためである。
validate_effort() {
  case "$1" in
    low|medium|high|xhigh|max|ultra) ;;
    *) die "effort の値が不正である: $1 (low|medium|high|xhigh|max|ultra)" ;;
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

# Codex へ渡す作業ディレクトリをドライブ文字付きの Windows 形式へ揃える。
# Git Bash の /d/... 形式のままだと Codex 側が解決できないためである。
# cygpath が無い環境では区切り文字の変換だけを行う。
to_windows_path() {
  local p
  p="$(to_slash "$1")"
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -m "$p" 2>/dev/null || printf '%s' "$p"
  else
    printf '%s' "$p"
  fi
}

# 定義ファイルを探す。プロジェクト定義がユーザ定義を上書きする。
def_file=""
for candidate in "$PWD/.claude/gpt-agents/$agent_name.md" "$(to_slash "$home_dir")/.claude/gpt-agents/$agent_name.md"; do
  if [ -f "$candidate" ]; then
    def_file="$candidate"
    break
  fi
done
[ -n "$def_file" ] || die_missing "エージェント定義が見つからない: $agent_name (.claude/gpt-agents/$agent_name.md)"

# codex 本体の有無も、定義の有無と同じ「GPT 側が未導入」として扱う。
command -v codex >/dev/null 2>&1 || die_missing "codex コマンドが PATH に無い (docs/setup.md の導入手順を参照)"

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
codex_enabled="$(fm_get codex_enabled)"

# キーが無いときだけ true を補う。値を書きかけた指定は不正として止める。
# 他のキーと違い、既定が Codex を起動する側に倒れるためである。
grep -q '^codex_enabled:' <<<"$front_matter" || codex_enabled="true"
case "$codex_enabled" in
  true) ;;
  false) die_missing "GPT 側が無効化されている (codex_enabled: false): $def_file" ;;
  *) die "codex_enabled の値が不正である: $codex_enabled (true または false)" ;;
esac

[ -n "$codex_home" ] || die "フロントマターに codex_home が無い: $def_file"
# codex_model が無い、または空の定義は「GPT 側を使わない」設定として扱う。
# 区分ごとに GPT 経路の有無を切り替える手段であり、codex_enabled(3 定義をまとめて止める切替)とは役割が異なる。
# キー名の誤記もここで Claude 側へ倒れるため、呼び出し側は報告の冒頭に理由を書く。
[ -n "$codex_model" ] || die_missing "codex_model が未設定である(GPT 側を使わない): $def_file"
[ -n "$codex_effort" ] || codex_effort="medium"
# 既定は安全側の read-only。書き込みが必要な定義だけが明示する。
[ -n "$codex_sandbox" ] || codex_sandbox="read-only"

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
codex_home="$(to_windows_path "$codex_home")"

[ -d "$codex_home" ] || die "codex_home が存在しない: $codex_home (docs/setup.md のログイン手順を参照)"

[ -n "$workdir" ] || workdir="$PWD"
workdir="$(to_windows_path "$workdir")"
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

# 空白だけの依頼文は Codex を起動しても意味がないので、ここで止める。
case "$request" in
  *[![:space:]]*) ;;
  *) die "依頼文が空である" ;;
esac

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
if [ "${CODEX_AGENT_SIMULATE_UNAVAILABLE:-}" = "1" ]; then
  printf 'codex-agent: result=unavailable (simulated)\n'
  exit 75
fi

# 実行ログの置き場。Codex は推論の経過と実行したコマンドを標準エラーへ流すため、
# 受け取った全文をそのまま返すと呼び出し側のツール結果が肥大してファイルへ退避され、
# 報告を取り出せなくなる。全文はここへ残し、標準出力へは最終報告と監査用の行だけを出す。
# ログには Codex が読んだファイルの中身や実行結果が入りうる。
# 共有される一時ディレクトリ(`/tmp` など)に置くと、他の利用者から読まれる余地と、
# 先回りして置かれたシンボリックリンク越しに別のファイルを切り詰める余地が残る。
# ホーム配下は他の利用者が書き込めないため、置き場をここに取る。
log_dir="$(to_slash "$home_dir")/.claude/codex-agent/logs"
mkdir -p "$log_dir" || die "実行ログの置き場を作れない: $log_dir"
# POSIX 権限が効く環境ではさらに本人だけが読める形に落とす。多重防御である。
chmod 700 "$log_dir" 2>/dev/null || true
# 残し続けると 1 回あたり数百 KB が溜まるため、古いものを落とす。
# .last と .out は通常は終了時に消すが、taskkill /F などで強制終了されると EXIT の trap が動かずに残るため、同じ規則で落とす。
find "$log_dir" -maxdepth 1 -type f \( -name '*.log' -o -name '*.last' -o -name '*.out' \) -mtime +7 -delete 2>/dev/null || true

# 定義がスクラッチパッドを使えないときに依頼文を置く場所。ログと同じ期限で消す。
# 置き場が無くても実行自体は続けられるため、作成に失敗しても止めない。
prompts_dir="$(to_slash "$home_dir")/.claude/codex-agent/prompts"
mkdir -p "$prompts_dir" 2>/dev/null || true
chmod 700 "$prompts_dir" 2>/dev/null || true
find "$prompts_dir" -maxdepth 1 -type f -mtime +7 -delete 2>/dev/null || true

# 実行 ID はログファイル名から拡張子を除いたものにする。run= の行と log= の行を突き合わせられるようにするためである。
run_id="$agent_name-$(date +%Y%m%d-%H%M%S)-$$"
log_base="$log_dir/$run_id"
log_file="$log_base.log"
# Codex の標準出力(最終回答)の受け皿。共有の一時ディレクトリではなくログ置き場に置く。
# 強制終了で残っても、実行 ID から特定して消せるうえ、古いものは上の削除で落ちるためである。
out_file="$log_base.out"
last_msg_file="$log_base.last"
# 書き込み担当の目印のパス。置けたときだけ入る。
marker_file=""
# ログ本体だけを残す。他は標準出力へ出すかログへ写した時点で役目を終える。
# 目印も終了時に消す。強制終了で残った目印は、次の起動が古い目印の規則で扱う。
trap 'rm -f "$out_file" "$last_msg_file"; [ -z "$marker_file" ] || rm -f "$marker_file" 2>/dev/null' EXIT
# 先に作って権限を落とす。あとの書き込みは truncate か追記なので、この権限が残る。
: >"$log_file" || die "実行ログを作れない: $log_file"
: >"$last_msg_file" || die "最終報告の受け皿を作れない: $last_msg_file"
: >"$out_file" || die "標準出力の受け皿を作れない: $out_file"
chmod 600 "$log_file" "$last_msg_file" "$out_file" 2>/dev/null || true

# 止める対象を特定できるよう、Git Bash では Windows の PID を出す。
# taskkill が受け付けるのは Windows の PID であり、$$ は Git Bash 内の番号で一致しないためである。
run_pid="$$"
if [ -r "/proc/$$/winpid" ]; then
  winpid="$(cat "/proc/$$/winpid" 2>/dev/null)"
  case "$winpid" in
    ''|*[!0-9]*) ;;
    *) run_pid="$winpid" ;;
  esac
fi
run_line="codex-agent: run=$run_id pid=$run_pid started=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
printf '%s\n' "$run_line" >>"$log_file"

# Codex の起動前に出す。実行中に呼び出し側がログの場所と止める対象を知るための行である。
printf '%s\n' "$run_line"
log_line="codex-agent: log=$(to_windows_path "$log_file")"
printf '%s\n' "$log_line"

# 書き込み担当の目印の置き場を出す。git が無い、または作業ディレクトリが git の管理下に無いときは何も出さない。
# worktree ごとに置き場を分けるため、共通の git ディレクトリではなく worktree 固有の git ディレクトリを使う。
run_marker_dir() {
  local git_dir
  command -v git >/dev/null 2>&1 || return 0
  git_dir="$(git -C "$workdir" rev-parse --absolute-git-dir 2>/dev/null | tr -d '\r')"
  [ -n "$git_dir" ] && [ -d "$git_dir" ] || return 0
  printf '%s/codex-agent/runs' "$(to_slash "$git_dir")"
}

# 置き場にある他の目印を調べ、終わった実行の目印を消し、残っている実行ごとに警告の行を出す。
# 終わったかどうかは相手のログで見分ける。ログの最後の行は、終了した実行に限って result= の行になるためである。
# ログが無い目印(7 日を過ぎたログの削除で消えた場合を含む)は、見分ける手がかりが無いので終わった実行として消す。
# check_run_markers <置き場>
check_run_markers() {
  local m other_id other_log last warning
  for m in "$1"/*.run; do
    [ -f "$m" ] || continue
    other_id="$(basename "$m" .run)"
    [ "$other_id" != "$run_id" ] || continue
    other_log="$(sed -n 's/\r$//; s/^codex-agent: log=//p' "$m" 2>/dev/null | head -n 1)"
    if [ -z "$other_log" ] || [ ! -f "$other_log" ]; then
      rm -f "$m" 2>/dev/null
      continue
    fi
    last="$(tail -n 1 "$other_log" 2>/dev/null | tr -d '\r')"
    case "$last" in
      "codex-agent: result="*)
        rm -f "$m" 2>/dev/null
        continue
        ;;
    esac
    warning="codex-agent: warning=concurrent-writer run=$other_id log=$other_log"
    printf '%s\n' "$warning"
    printf '%s\n' "$warning" >>"$log_file"
  done
  return 0
}

# 自分の目印を置く。一時ファイルに書いてから名前を変え、他の起動が書きかけの目印を読まないようにする。
# 置けなかったときは marker_file を空のままにし、起動は続ける。
# place_run_marker <置き場>
place_run_marker() {
  local tmp="$1/$run_id.run.tmp"
  if {
    printf '%s\n' "$run_line"
    printf '%s\n' "$log_line"
    printf 'codex-agent: agent=%s sandbox=%s\n' "$agent_name" "$codex_sandbox"
  } 2>/dev/null >"$tmp" && mv -f "$tmp" "$1/$run_id.run" 2>/dev/null; then
    marker_file="$1/$run_id.run"
  else
    rm -f "$tmp" 2>/dev/null
  fi
  return 0
}

# 目印を扱うのは書き込み可能な起動だけである。
# read-only の起動は worktree を書き換えないため、目印を置かず、他の目印も調べない。
# 目印の処理に失敗しても起動は止めない。目印は観測の補助であり、委譲そのものより優先しないためである。
if [ "$codex_sandbox" = "workspace-write" ]; then
  runs_dir="$(run_marker_dir)"
  if [ -n "$runs_dir" ] && mkdir -p "$runs_dir" 2>/dev/null; then
    check_run_markers "$runs_dir"
    place_run_marker "$runs_dir"
  fi
fi

# 失敗時と、最終報告を取り出せない場合に出すログの行数。
tail_lines=40

# 出したファイルが改行で終わっていないと、続く codex-agent: の行が同じ行に繋がって読めなくなる。
# Codex の最終報告は改行で終わらないことがある。
end_newline() {
  [ -s "$1" ] || return 0
  if [ -n "$(tail -c 1 "$1")" ]; then
    printf '\n'
  fi
  return 0
}

# result 行を標準出力とログの末尾の両方へ出して終わる。
# ログの最後の行を標準出力の最後の行と同じにし、ログだけで完了と結果を判定できるようにする。
# finish <result の値> <終了コード>
finish() {
  printf 'codex-agent: result=%s\n' "$1"
  printf 'codex-agent: result=%s\n' "$1" >>"$log_file"
  exit "$2"
}

# ログから先頭の run= の行と warning= の行を除いた本文を出す。
# 失敗時に標準出力へ出す末尾と、失敗の判定の対象には、これらの行を含めない。
# 前者は標準出力に同じ行を 2 回出さないため、後者は実行 ID、PID、ログのパスの数字が判定の語(429 など)に一致しうるためである。
log_body() {
  awk 'NR > 1 && !/^codex-agent: warning=/' "$log_file"
}

# --output-last-message は Codex の版によって無い。無い版ではログの末尾を報告の代わりに出す。
# 認証ホームはこの確認でも明示する。codex を呼ぶ経路に既定の ~/.codex への暗黙依存を残さない。
# ヘルプは変数に受けてから調べる。pipefail の下で grep -q へパイプすると、一致した時点で grep が終わり、
# 残りを書こうとした codex が SIGPIPE で失敗して、有る版でも無い版と判定されることがあるためである。
output_last_message=0
codex_help="$(CODEX_HOME="$codex_home" codex exec --help 2>/dev/null)"
case "$codex_help" in
  *--output-last-message*) output_last_message=1 ;;
esac

codex_args=(
  --skip-git-repo-check
  --sandbox "$codex_sandbox"
  -m "$codex_model"
  -c "model_reasoning_effort=\"$codex_effort\""
  -C "$workdir"
)
if [ "$output_last_message" -eq 1 ]; then
  codex_args+=(-o "$last_msg_file")
fi

# 認証ホームは常に明示する。既定の ~/.codex への暗黙依存を作らない。
# --dangerously-bypass-approvals-and-sandbox は付けない。
# 承認方針は never に固定する。このスクリプトは非対話の委譲専用で承認を返す相手がいないため、
# 呼び出し側の config.toml が on-request 等でも承認待ちで止まらないようにする。
# -c approval_policy=never は codex_args に入れず、この起動行に直接書く。
# ai-cross-review の cross-review.js は、codex exec の起動行の文字列(\ で続けた行を含む論理行)に
# この指定がある場合に限って bridge 経由を選ぶ。配列の中身までは追わないため、配列へ移すと直接起動へ戻ってしまう。
#
# 標準エラーはパイプで受け、実行中から行ごとにログへ追記する。実行中にログの末尾で経過を読めるようにするためである。
# WARNING で始まる行と hook: で始まる行は、ログへ書く時点で除く。
# grep --line-buffered は、1 行ごとに書き出してログへの反映を遅らせないために付ける。
# 標準出力(最終回答)はログ置き場の <実行 ID>.out に受け、終了後にログへ追記する。
# プロセス置換でなくパイプラインにするのは、書き込み側の終了を待ってから次へ進むためである。
# Codex の終了コードは PIPESTATUS の先頭から取る。grep の終了コード(除いた結果が空なら 1)は使わない。
CODEX_HOME="$codex_home" codex exec -c approval_policy=never "${codex_args[@]}" - <<<"$prompt" 2>&1 >"$out_file" \
  | grep --line-buffered -v -e '^WARNING' -e '^hook:' >>"$log_file"
codex_status="${PIPESTATUS[0]}"

# ツール接続の判定に使う経過(標準エラー)は、最終回答を写す前に取り出す。
# 最終回答も含めて判定すると、回答の本文に書かれた語で判定が反転する。
stderr_body="$(log_body)"

# 経過(標準エラー)に続けて最終回答(標準出力)をログへ写す。
cat "$out_file" >>"$log_file"
# 最終回答が改行で終わらなくても、あとで追記する result= の行が独立した行になるようにする。
end_newline "$log_file" >>"$log_file"

# 根拠は 1 行ずつ接頭辞を付けて出す。
# 呼び出し側の定義は result 行の直前に evidence 行が来ることを前提にしているため、
# 接頭辞の無い行を間に挟まない。
emit_evidence() {
  printf '%s\n' "$2" | while IFS= read -r line; do
    [ -n "$line" ] || continue
    printf 'codex-agent: %s evidence: %s\n' "$1" "$line"
  done
}

# 終了コード 0 でも、ツール接続が一度も成立しなかった実行は依頼を果たしていないため 75 に倒す。
# この ERROR 行は先頭にタイムスタンプが付くため、行頭でなく行内の ERROR で照合する。
# ツール実行の成功行(" succeeded in ")が 1 行でもあれば、接続が復旧して作業できたとみなし ok のままにする。
# 本文は変数に受けてから調べる。pipefail の下で grep -q へパイプすると、書き手が SIGPIPE で失敗して判定が反転しうるためである。
if [ "$codex_status" -eq 0 ]; then
  matched="$(printf '%s\n' "$stderr_body" | grep -E 'ERROR' | grep -F 'code-mode host exited during handshake' | head -n 3)"
  if [ -n "$matched" ] && [[ "$stderr_body" != *' succeeded in '* ]]; then
    log_body | tail -n "$tail_lines"
    emit_evidence 'unavailable' "$matched"
    finish unavailable 75
  fi
fi

if [ "$codex_status" -eq 0 ]; then
  if [ -s "$last_msg_file" ]; then
    cat "$last_msg_file"
    end_newline "$last_msg_file"
  else
    # --output-last-message が無い版では、最終回答が流れる標準出力を報告の代わりに出す。
    tail -n "$tail_lines" "$out_file"
    end_newline "$out_file"
  fi
  finish ok 0
fi

# 失敗したときは原因を追えるようにする。ログの末尾だけを出す。
# 全文を出すと、肥大を避けるためにログへ移した意味がなくなる。
# この時点のログにはまだ result= の行が無いため、標準出力の result= の行は最後の 1 回だけになる。
# ログは直前に改行で終わる形に揃えてあるため、続く行が繋がらない。
log_body | tail -n "$tail_lines"

# 判定の対象は、失敗を告げる行と出力の末尾に絞る。
# ログには Codex が読んだファイルの中身も流れるため、全文を対象にすると
# テストデータに含まれる文字列で誤検出する。
# 末尾も残すのは、失敗の通知が ERROR で始まらない版があり得るためである。
# 末尾に読み込んだ内容が来ていれば誤検出は残るが、誤検出の結果は Claude 側での実装であり、
# 検出漏れ(作業がそこで止まる)より軽い。
# 抽出と末尾で同じ行が二重に入るため、重複は落とす。
evidence="$(
  {
    log_body | grep -iE '^[[:space:]]*(ERROR|stream error)'
    log_body | tail -n 10
  } 2>/dev/null | awk '!seen[$0]++'
)"

# 利用上限の通知は標準出力に出ることも標準エラーに出ることもあるため、両方を見る。
# 429 は単語境界で照合する。ID や桁数の一致で誤検出しないためである。
# 一致した行を残し、フォールバックの根拠を報告から追えるようにする。
matched="$(printf '%s\n' "$evidence" | grep -iE 'usage limit|rate limit|too many requests|\b429\b' | head -n 3)"
if [ -n "$matched" ]; then
  emit_evidence 'rate-limit' "$matched"
  finish rate-limited 75
fi

# 利用上限のほかにも、呼び出し側では直せない GPT 側の事情で実行できないことがある。
# これらは引数や定義の不備と違って呼び出し側で直せないため、利用上限と同じ終了コード 75 を返し、
# 呼び出し側が Claude へ倒せるようにする。理由は result 行で区別する。
# 並べる語は実際に観測したものだけにする。広く取ると、Codex の通常の失敗まで倒れてしまう。
matched="$(printf '%s\n' "$evidence" | grep -iE 'at capacity' | head -n 3)"
if [ -n "$matched" ]; then
  emit_evidence 'unavailable' "$matched"
  finish unavailable 75
fi

# Codex 自身の 75 は、GPT 側が使えないことを示す 75 と区別できないため 1 に写像する。
# 元の値は result 行に残す。
if [ "$codex_status" -eq 75 ]; then
  finish "failed exit=$codex_status" 1
fi
finish "failed exit=$codex_status" "$codex_status"
}

# main の後ろにコードを置かない。この行までを読み終えてから実行が始まる。
main "$@"; exit $?
