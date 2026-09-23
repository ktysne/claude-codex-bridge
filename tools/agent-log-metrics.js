#!/usr/bin/env node
//
// Claude Code のセッション記録から、GPT 系サブエージェントの運用の指標を数える。
//
// 用法:
//   node tools/agent-log-metrics.js [--since <日付か日時>] [--until <日付か日時>] [--json]
//
// --since と --until には、日付(YYYY-MM-DD)か、タイムゾーン付きの日時を指定する。
// 日時は YYYY-MM-DDTHH:MM、YYYY-MM-DDTHH:MM:SS、YYYY-MM-DDTHH:MM:SS.sss のいずれかに、Z か ±HH:MM を付ける。
// 日付は UTC の日として解釈し、--since はその日の 00:00:00.000 から、--until はその日の最後のミリ秒までを含む。
// 日時では、--since はその時刻を含み、--until はその時刻を含まない。ある時刻を境に前後 2 回数えたとき、
// 境界の記録を二重に数えないためである。
// どちらも省くと、現在までの直近 7 日を見る。
// 記録の場所は %USERPROFILE%\.claude\projects(環境変数 CLAUDE_PROJECTS_DIR で変えられる)。
//
// 数え方の約束(出力の「数え方の版」は、ここに書いた約束の版である):
//   - 起動は Bash の呼び出し 1 件を 1 件と数え、tool_use の識別子でまとめる。会話を引き継いだセッションは
//     前のセッションの記録を識別子ごと写すため、同じ呼び出しが別の記録に現れることがある。
//     待つための Bash と委譲も、同じく tool_use の識別子でまとめる。
//     時刻ではまとめない。まとめると、同じ分に並行して起動した別々の実行が失われる。
//   - 依頼文はヒアドキュメントでコマンドに埋め込まれる。照合の前にその本文を落とす。
//     落とさないと、依頼文が話題にしている語を実行したものとして数える。
//   - 起動の判定は、コマンドを実行単位へ切り出してから行う。区切りは `;`、`&`、`|`、改行である。
//     引用符の中とコメントの中にある区切りは区切りとして扱わない。
//   - bash や sh の短いオプションのまとまり(`-` 1 つで始まる語)に n を含む実行単位は、起動と数えない。
//     構文を検査するだけで実行しないためである。`--norc` のような `--` で始まる長いオプションは対象にしない。
//   - スクリプトパスより後ろに、ちょうど `-h` か `--help` の語がある実行単位は、起動と数えない。
//     ラッパーは引数のどこにこれがあっても用法を出して終わり、Codex を起動しないためである。
//     語はシェルと同じく引用符を外して比べ、ヒアストリング(`<<<`)の本文は比べない。
//   - セッションが作業ディレクトリを移ると、同じセッションの記録が別のプロジェクト置き場にも書かれる。
//     プロジェクト置き場より後ろの相対パスが同じ記録を複製の候補とし、最も大きい記録を残す
//     (同じ大きさならパスの辞書順で先のもの)。ほかの記録は、残した記録の先頭と全バイトが一致する場合に限って除く。
//     一致しない記録は除かずに数え、「前方一致しない複製」としてパスを出す。前提が破れたときに値を黙って変えず、
//     件数で知らせるためである。残した記録の脇に meta ファイルが無ければ、除いた複製の脇のものを使う。
//   - result 行は、行頭から行末までが `codex-agent: result=<値>` の行である。値は ok、rate-limited、unavailable、
//     failed exit=<n> のいずれかで、後ろに ` (simulated)` が付くことがある。本文にいくつもあるときは最後の行を使う。
//     Codex の最終報告が result 行を引用することがあり、ラッパーは自分の result 行を出力の最後に出すためである。
//     行頭に `grep -n` の行番号(`32:` か `32-`)が付いた行も result 行とする。出力を `grep -n` で絞った起動の結果を
//     読むためである。行の途中にある result 行は拾わない。最終報告が行の途中で引用することがあるためである。
//   - 最後の result 行に ` (simulated)` が付く起動は、試験用フックが Codex を起動せずに出したものである。
//     実起動にも結果の内訳にも入れず、「疑似の起動」として別に数える。期間の判定は実起動と同じである。
//     委譲の結果、Codex 未呼出、待つための Bash の条件でいう「Codex を起動した」は、疑似でない起動があることである。
//   - 実起動の内訳は、本文の最後の result 行の値で分ける。出力が退避された本文は「結果行なし」とする。
//     退避された本文は冒頭の抜粋で、最終報告が引用した result 行が入りうるためである。
//   - 結果がその場で分からない起動は実起動に数えない。上限を超えてバックグラウンドへ移った起動
//     (本文に「moved to the background」が出るもの)と、run_in_background で最初からバックグラウンドに置いた起動
//     (本文が「Command running in background with ID:」で始まるもの)を、それぞれ別に数える。
//     前者は Codex の実行時間が Bash の上限を超えた回数を表し、後者は起動した側の選択を表すためである。
//   - 待つための Bash は、Codex を起動したサブエージェントの記録にある、Codex の起動でない Bash の呼び出しで、
//     ヒアドキュメントを落としたコマンドが次のどちらかに当たるものである。1 回の呼び出しは、両方に当たっても 1 件と数える。
//     1 つは、パス区切り(`/` か `\`)に続く語が `.output` で終わるものを含むことである。
//     Claude Code のバックグラウンドの出力は `.../tasks/<ID>.output` の形であるためである。
//     もう 1 つは、実行単位の先頭から `do`、`then`、`else`、`{`、`(`、`!` の語を読み飛ばした後の最初の語が、
//     ちょうど `sleep` か `until` であることである。語を含むだけのコマンド(`--until` を渡す実行や、
//     `sleep` を含む行を編集する実行)を待機と取り違えないためである。
//   - 委譲 1 件は Agent の呼び出し 1 件である。同じ依頼文を出し直した場合も、
//     それぞれ別の実行を伴うので別の委譲として数える。
//   - サブエージェント起動は、子の記録にある Agent ツールの呼び出しで数える。
//     孫の記録は親の tool_use と紐付かないため、孫が Codex を起動しても親から見た委譲は Codex 未呼出のままになる。
//   - 委譲と実行の紐付けは、サブエージェントの記録の脇にある `<名前>.meta.json` が持つ
//     親の tool_use の識別子で行う。依頼文の一致で推測すると、同じ依頼文を出した
//     別の定義や別の時期の実行と取り違える。
//   - 委譲の期間は親の起動時刻で選ぶ。子の実行が日付をまたぐことがあるため、子の側では絞らない。
//   - 対応する実行を特定できない委譲は「紐付け不明」として数え、未呼出には加えない。
//     別の実行の状態を流用しないためである。
//   - 出た値はすべて下限である。Codex の実行はバックグラウンドへ移ることがあり、
//     その出力が記録に残らない場合があるためである。
//   - 分類は終了コードと result 行だけで行う。依頼を果たせないまま 0 で終わった実行は数えられない。
//   - 委譲の結果は、紐付けできた委譲 1 件をちょうど 1 つの分類に数える。分類は GPT で実行、
//     未設定(result=failed exit=3)、GPT 使用不能(rate-limited と unavailable)、GPT で失敗、
//     拒否、結果不明、未起動である。子の起動がすべて疑似の起動なら、その委譲は未起動である。
//   - 委譲を止める指定は、依頼文の最初の空でない行を前後の空白を除いて比較する。
//     2 行目以降やコードブロック内の文字列は指定に数えない。定義や文書を引用した依頼を
//     誤って指定として数えないためである。
//   - 分類には、子が codex-agent.sh を起動した Bash の呼び出しのうち、疑似でない最後のものの結果を使う。
//     子は失敗や上限の後に起動し直すことがあり、委譲の行き先を決めたのは最後の起動だからである。
//     子が複数ある委譲では、すべての子の起動を時刻で並べて最後のものを使う。記録順はファイルを読んだ順で
//     記録どうしの前後を表さないため、時刻を読めない起動があるときと、最後の時刻に別の子の起動が並ぶときは、
//     子ごとの最後の起動の結果がすべて同じならその分類、食い違えば結果不明とする。
//   - 拒否は、tool_result に is_error が付き、本文が `Exit code` で始まらず、`codex-agent: ` の行も
//     含まないもので判定する。拒否の文言は拒否した仕組みごとに違い、版によっても変わるため、
//     本文の文言では判定しない。
//   - 起動の結果は、退避された出力、result 行、バックグラウンドへの移行の順に見る。退避された出力の本文は
//     冒頭の抜粋で、最終報告が引用した result 行が入りうるため、本文の result 行では確定しない。
//     退避された出力と見なすのは、本文が <persisted-output> で始まり退避先の行を持つものだけである。
//     最終報告がこのタグを引用しただけの本文を取り違えないためである。
//     result 行をバックグラウンドの文言より先に見るのは、最終報告の本文がその文言に触れていることがあるためである。
//   - 起動がバックグラウンドへ移った場合と、出力が退避された場合は、その起動を追跡する。
//     手がかりは、バックグラウンドの ID と出力ファイルのパス、退避先のパスである。
//     同じ子の記録で後に現れる tool_use のうち、入力が手がかりを含むもの(種類は問わない)を
//     その起動に結び付け、その tool_result の最後の result 行で起動の結果を確定する。
//     パスは区切り文字 `\` と `/` の違いを吸収して照合する。is_error の付いた結果と、
//     最後の result 行が疑似のものは確定に使わない。
//   - 手がかりで結び付かない読み取りの result 行は使わない。差分や文書、別の実行のログを読んだ結果にも
//     result 行が現れるため、それを拾うと別の起動の結果を流用する。
//   - 追跡しても確定しなかったもの、結果が記録に無いもの、どの分類にも当たらないものは
//     結果不明とする。
//   - 読めなかった場所は握りつぶさず末尾に出す。測れなかったことと、実績が無いことは違う。

const fs = require('fs');
const path = require('path');

const WRAPPER_AGENTS = ['impl-hard', 'impl-light', 'impl-standard', 'codex-review', 'codex-subagent'];

// 数え方の約束を変えたら上げる。運用記録の値がどの規則で数えたものかを、値の脇に残すためである。
const COUNTING_RULES_VERSION = 2;

const DAY = 24 * 3600 * 1000;

function takeValue(argv, i, name) {
  const v = argv[i];
  if (v === undefined || v.startsWith('--')) throw new Error(`${name} に値が無い`);
  return v;
}

function parseArgs(argv) {
  const opts = { json: false };
  for (let i = 0; i < argv.length; i += 1) {
    const a = argv[i];
    if (a === '--since') opts.since = takeValue(argv, ++i, '--since');
    else if (a === '--until') opts.until = takeValue(argv, ++i, '--until');
    else if (a === '--json') opts.json = true;
    else if (a === '-h' || a === '--help') opts.help = true;
    else throw new Error(`不明なオプションである: ${a}`);
  }
  return opts;
}

// 日付は UTC で解釈し、その日の 00:00:00.000 を数値で返す。
// Date.UTC は 2026-02-30 のような日付を繰り上げて受け入れるため、年月日の一致まで確かめる。
function parseDay(value, name) {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(value));
  if (!m) throw new Error(`${name} は YYYY-MM-DD で指定する: ${value}`);
  const y = Number(m[1]);
  const mo = Number(m[2]);
  const d = Number(m[3]);
  const t = Date.UTC(y, mo - 1, d);
  const back = new Date(t);
  if (back.getUTCFullYear() !== y || back.getUTCMonth() !== mo - 1 || back.getUTCDate() !== d) {
    throw new Error(`${name} に実在しない日付が指定された: ${value}`);
  }
  return t;
}

const DATETIME = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{3}))?)?(Z|[+-]\d{2}:\d{2})?$/;

// 期間の境界を解釈し、時刻の数値と、日付で指定したかどうかを返す。
// 日付は parseDay と同じく UTC の日の先頭である。含み方は呼び出し側が isDay で決める。
// 日時はタイムゾーンを必須とする。無いと、実行した環境の時差で境界が動くためである。
// Date.UTC は 2026-02-30 や 24:00 を繰り上げて受け入れるため、指定したタイムゾーンでの
// 年月日時分秒ミリ秒が往復で一致するかを確かめ、一致しなければ実在しない日時として拒否する。
function parseBoundary(value, name) {
  const text = String(value);
  if (/^\d{4}-\d{2}-\d{2}$/.test(text)) return { time: parseDay(text, name), isDay: true };
  const m = DATETIME.exec(text);
  if (!m) {
    throw new Error(
      `${name} は YYYY-MM-DD か、タイムゾーン付きの YYYY-MM-DDTHH:MM[:SS[.sss]](Z か ±HH:MM)で指定する: ${value}`,
    );
  }
  if (m[8] === undefined) throw new Error(`${name} の日時にはタイムゾーン(Z か ±HH:MM)を付ける: ${value}`);
  const [y, mo, d, h, mi] = [1, 2, 3, 4, 5].map((i) => Number(m[i]));
  const s = Number(m[6] || 0);
  const ms = Number(m[7] || 0);
  let offsetMinutes = 0;
  if (m[8] !== 'Z') {
    const oh = Number(m[8].slice(1, 3));
    const om = Number(m[8].slice(4, 6));
    if (oh > 23 || om > 59) throw new Error(`${name} に実在しない日時が指定された: ${value}`);
    offsetMinutes = (m[8][0] === '-' ? -1 : 1) * (oh * 60 + om);
  }
  // 指定したタイムゾーンでの壁時計の時刻を、いったん UTC の数値として組み立てて往復を確かめる。
  const wall = Date.UTC(y, mo - 1, d, h, mi, s, ms);
  const back = new Date(wall);
  if (
    back.getUTCFullYear() !== y
    || back.getUTCMonth() !== mo - 1
    || back.getUTCDate() !== d
    || back.getUTCHours() !== h
    || back.getUTCMinutes() !== mi
    || back.getUTCSeconds() !== s
    || back.getUTCMilliseconds() !== ms
  ) {
    throw new Error(`${name} に実在しない日時が指定された: ${value}`);
  }
  return { time: wall - offsetMinutes * 60 * 1000, isDay: false };
}

function projectsDir() {
  if (process.env.CLAUDE_PROJECTS_DIR) return process.env.CLAUDE_PROJECTS_DIR;
  const home = process.env.USERPROFILE || process.env.HOME;
  if (!home) throw new Error('ホームディレクトリを特定できない');
  return path.join(home, '.claude', 'projects');
}

// 読めなかった場所は握りつぶさず数える。測れなかったことと、実績が無いことを区別するためである。
function walk(dir, out = [], failures = []) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch (err) {
    failures.push(`${dir}: ${err.code || err.message}`);
    return out;
  }
  for (const e of entries) {
    const p = path.join(dir, e.name).split(path.sep).join('/');
    if (e.isDirectory()) walk(p, out, failures);
    else if (e.name.endsWith('.jsonl')) out.push(p);
  }
  return out;
}

// 照合のためにパスの区切りをそろえる。記録の JSON では `\` が `\\` になるため、連続もまとめる。
const normalizeClue = (s) => s.replace(/[\\/]+/g, '/');

// 別のプロジェクト置き場へ複製された同じセッションの記録を 1 本にする。
// セッションが作業ディレクトリを移ると、移る前の記録が移った先にも書かれ、その後ろに追記される。
// 候補は `<projectsDir>/<プロジェクト>/` より後ろの相対パスが同じ記録である。その階層に当たらない記録は候補にしない。
// 候補の組では最も大きい記録(同じ大きさならパスの辞書順で先のもの)を残し、ほかの記録は、
// 残した記録の先頭と自分の全バイトが一致する場合に限って除く。
// 一致しない記録は除かずに残して divergent に入れる。前提が破れたときに値を黙って変えず、件数で知らせるためである。
// 読めない記録は候補から外して残す。読めなかったことは collect が報告する。
// 返り値の files は入力の順を保ち、copiesOf は残した記録のパスから除いた複製のパスの並びへの対応である。
function dedupeSessionCopies(files, projectsDir) {
  const root = `${normalizeClue(String(projectsDir)).replace(/\/$/, '')}/`;
  const groups = new Map();
  for (const file of files) {
    const normalized = normalizeClue(file);
    if (!normalized.startsWith(root)) continue;
    const rest = normalized.slice(root.length);
    const slash = rest.indexOf('/');
    if (slash <= 0 || slash === rest.length - 1) continue;
    const rel = rest.slice(slash + 1);
    const group = groups.get(rel) || [];
    group.push(file);
    groups.set(rel, group);
  }

  const dropped = new Set();
  const divergent = [];
  const copiesOf = new Map();
  for (const group of groups.values()) {
    if (group.length < 2) continue;
    const records = [];
    for (const file of group) {
      try {
        records.push({ file, bytes: fs.readFileSync(file) });
      } catch {
        // 読めない記録は残す。collect が読めなかった場所として報告する。
      }
    }
    if (records.length < 2) continue;
    records.sort((a, b) => (b.bytes.length - a.bytes.length)
      || (a.file < b.file ? -1 : (a.file > b.file ? 1 : 0)));
    const [kept, ...others] = records;
    for (const other of others) {
      if (kept.bytes.subarray(0, other.bytes.length).equals(other.bytes)) {
        dropped.add(other.file);
        const copies = copiesOf.get(kept.file) || [];
        copies.push(other.file);
        copiesOf.set(kept.file, copies);
      } else {
        divergent.push(other.file);
      }
    }
  }
  return {
    files: files.filter((file) => !dropped.has(file)),
    dropped: dropped.size,
    divergent,
    copiesOf,
  };
}

// 依頼文はヒアドキュメントでコマンドに埋め込まれる。
// その本文まで照合の対象にすると、依頼文が話題にしている語(`git commit` など)を
// 実行したものとして数えてしまう。照合の前に本文を落とす。
function stripHeredocs(cmd) {
  return cmd
    .replace(/<<-\s*(['"]?)([A-Za-z_][A-Za-z0-9_]*)\1[\s\S]*?^\t*\2$/gm, '<<HEREDOC')
    .replace(/<<(?!-)\s*(['"]?)([A-Za-z_][A-Za-z0-9_]*)\1[\s\S]*?^\2$/gm, '<<HEREDOC');
}

// コマンド文字列をシェルの区切りで実行単位へ切り出す。
// 区切りは `;`、`&`、`|`、改行である。
// 引用符の中とコメントの中にある区切りは区切りとして扱わない。
// 引用符を追わずに改行だけで切ると、複数行の引用文字列に書いた例を実行と取り違える。
function splitCommands(cmd) {
  const out = [];
  let cur = '';
  let quote = null;
  for (let i = 0; i < cmd.length; i += 1) {
    const ch = cmd[i];
    if (quote) {
      if (ch === '\\' && quote === '"') { cur += ch + (cmd[i + 1] || ''); i += 1; continue; }
      if (ch === quote) quote = null;
      cur += ch;
      continue;
    }
    if (ch === "'" || ch === '"') { quote = ch; cur += ch; continue; }
    // 行継続。次の行は同じ実行単位である。
    if (ch === '\\' && cmd[i + 1] === '\n') { cur += ' '; i += 1; continue; }
    if (ch === '\\') { cur += ch + (cmd[i + 1] || ''); i += 1; continue; }
    // 語の先頭に来た `#` から行末まではコメントである。
    if (ch === '#' && /(^|\s)$/.test(cur)) {
      while (i < cmd.length && cmd[i] !== '\n') i += 1;
      out.push(cur);
      cur = '';
      continue;
    }
    if (ch === ';' || ch === '&' || ch === '|' || ch === '\n') { out.push(cur); cur = ''; continue; }
    cur += ch;
  }
  out.push(cur);
  return out;
}

// 実行単位が codex-agent.sh の起動の形かを判定する。
// 先頭に並ぶ環境変数の代入と、bash 自身のオプションは読み飛ばす。オプションの並びは 1 番目の捕獲に入る。
// `cat tools/codex-agent.sh` のように読むだけのコマンドは起動と数えない。
const INVOCATION = /^(?:[A-Za-z_][A-Za-z0-9_]*=\S*\s+)*(?:bash|sh)\s+((?:-\S+\s+)*)["']?\S*codex-agent\.sh["']?(?:\s|$)/;

// 文字列をシェルと同じ規則で語に分け、語ごとに引用符とエスケープを外した値(value)と、書かれたままの形(raw)を返す。
// 引用符の中の空白は語を区切らない。
function shellWords(text) {
  const words = [];
  let value = '';
  let raw = '';
  let inWord = false;
  let quote = null;
  const flush = () => {
    if (inWord) words.push({ value, raw });
    value = '';
    raw = '';
    inWord = false;
  };
  for (let i = 0; i < text.length; i += 1) {
    const ch = text[i];
    if (quote === "'") {
      if (ch === "'") quote = null;
      else value += ch;
      raw += ch;
      continue;
    }
    if (quote === '"') {
      if (ch === '\\' && /["\\$`]/.test(text[i + 1] || '')) {
        value += text[i + 1];
        raw += ch + text[i + 1];
        i += 1;
        continue;
      }
      if (ch === '"') quote = null;
      else value += ch;
      raw += ch;
      continue;
    }
    if (/\s/.test(ch)) {
      flush();
      continue;
    }
    inWord = true;
    raw += ch;
    if (ch === "'" || ch === '"') {
      quote = ch;
    } else if (ch === '\\') {
      value += text[i + 1] || '';
      raw += text[i + 1] || '';
      i += 1;
    } else {
      value += ch;
    }
  }
  flush();
  return words;
}

// 起動の判定は 1 か所に置く。実起動、未呼出、待機の集計で同じ判定を使う。
// 起動の形でも、Codex を起動しない実行は数えない。
// bash の短いオプションのまとまりに n を含む実行は構文を検査するだけで、スクリプトを実行しない。
// スクリプトパスより後ろにちょうど -h か --help の語があれば、ラッパーは用法を出して終わる。
// 語はシェルと同じく引用符を外して比べる。シェルは `"--help"` の引用符を外してラッパーへ渡すためである。
// ヒアストリング(`<<<` と、その本文の語)は標準入力で、ラッパーの引数ではないので比べない。
// `<<<` の前にはファイル記述子の番号(`0<<<`)が付くことがある。
function isCodexInvocation(cmd) {
  return splitCommands(stripHeredocs(cmd)).some((seg) => {
    const trimmed = seg.trim();
    const m = INVOCATION.exec(trimmed);
    if (!m) return false;
    const options = m[1].split(/\s+/).filter(Boolean);
    if (options.some((option) => /^-[^-\s]*n/.test(option))) return false;
    const words = shellWords(trimmed.slice(m[0].length));
    for (let i = 0; i < words.length; i += 1) {
      const { value, raw } = words[i];
      if (/^\d*<<<$/.test(raw)) {
        i += 1; // 次の語はヒアストリングの本文である。
        continue;
      }
      if (/^\d*<<</.test(raw)) continue; // 本文が `<<<` に続けて書かれている。
      if (value === '-h' || value === '--help') return false;
    }
    return true;
  });
}

// 出力ファイルを読む Bash の目印。Claude Code のバックグラウンドの出力は `.../tasks/<ID>.output` の形である。
// パス区切りに続く語に限るのは、`grep '\.output'` のように語を検索するだけの実行を数えないためである。
const OUTPUT_FILE = /[\\/][\w.-]+\.output\b/;
// 実行単位の先頭で読み飛ばす語。ループや条件の本体、グループ、否定の中に置いた待機を見つけるためである。
// `(` は後ろに空白が無くても語になるので、ほかの語と分けて扱う。
const LEADING_WORD = /^(?:(?:do|then|else|\{|!)(?=\s|$)|\()/;

// 待つためだけの Bash かを判定する。cmd はヒアドキュメントを落とした後のコマンドである。
// sleep と until は実行単位の先頭の語だけを見る。部分一致で見ると、`--until` を渡す実行や、
// `sleep` を含む行を編集する実行を待機と取り違える。
function isWaitCommand(cmd) {
  if (OUTPUT_FILE.test(cmd)) return true;
  return splitCommands(cmd).some((seg) => {
    let rest = seg.replace(/^\s+/, '');
    for (let lead = LEADING_WORD.exec(rest); lead; lead = LEADING_WORD.exec(rest)) {
      rest = rest.slice(lead[0].length).replace(/^\s+/, '');
    }
    const word = /^[^\s()]*/.exec(rest)[0];
    return word === 'sleep' || word === 'until';
  });
}

const isSub = (file) => file.includes('/subagents/');

// サブエージェントの記録の脇には `<名前>.meta.json` があり、その実行を起こした親の
// tool_use の識別子(`toolUseId`)と定義名を持つ。これで親子を一意に結ぶ。
// 記録の脇に無ければ、除いた複製の脇を順に探す。複製の meta は移る前の置き場にだけ残ることがあるためである。
// 読めない場合は結ばない。依頼文の一致で代用すると、同じ依頼文を出した別の実行と取り違える。
function readAgentMeta(file, m, copies = []) {
  for (const candidate of [file, ...copies]) {
    const metaPath = candidate.replace(/\.jsonl$/, '.meta.json');
    if (!fs.existsSync(metaPath)) continue;
    try {
      return JSON.parse(fs.readFileSync(metaPath, 'utf8'));
    } catch (err) {
      m.unreadable.push(`${metaPath}: ${err.code || err.message}`);
      return null;
    }
  }
  return null;
}

const textOf = (c) => {
  if (typeof c.content === 'string') return c.content;
  if (Array.isArray(c.content)) return c.content.map((x) => (x && x.text) || '').join('\n');
  return '';
};

const RESULT_LINE = /^(?:\d+[:-])?codex-agent: result=(ok|rate-limited|unavailable|failed exit=(\d+))( \(simulated\))?[ \t\r]*$/gm;

// 本文の result 行のうち最後のものを読む。無ければ null を返す。
// 行頭から行末までが result 行の形である行だけを見る。最終報告が行の途中で引用したものを拾わないためである。
// 行頭の `grep -n` の行番号は読み飛ばす。出力を `grep -n` で絞った起動の結果も読むためである。
// 最後の行を使うのは、最終報告が前のほうで result 行を引用することがあり、ラッパーは自分の行を最後に出すためである。
// 実起動の内訳、委譲の結果、バックグラウンドの追跡のすべてでこの読み取りを使い、判定を食い違わせない。
// value は ok、rate-limited、unavailable、failed exit=<n> のいずれかで、simulated は試験用フックの出力かを表す。
function lastResultLine(t) {
  let last = null;
  for (const r of t.matchAll(RESULT_LINE)) last = r;
  if (!last) return null;
  return { value: last[1], exitCode: last[2], simulated: last[3] !== undefined };
}

// result 行を委譲の結果の分類へ写す。疑似の result 行は呼び出し側で除いてある。
function outcomeOfResult(r) {
  if (r.value === 'ok') return 'gptRan';
  if (r.value === 'rate-limited' || r.value === 'unavailable') return 'gptUnavailable';
  return r.exitCode === '3' ? 'notConfigured' : 'gptFailed';
}

// ツールが出力を退避したときの本文か。本文が <persisted-output> の外枠で始まり、退避先の行を持つものに限る。
// 最終報告がこのタグを引用しただけの本文を、退避された出力と取り違えないためである。
const isPersistedOutput = (t) => /^\s*<persisted-output>/.test(t) && /Full output saved to: /.test(t);

// 試験用フックが Codex を起動せずに出した本文か。フックは値の後ろに ` (simulated)` を付けた result 行を出す。
// 退避された本文の抜粋にある result 行は最終報告の引用でありうるため、疑似とは判定しない。
function isSimulatedOutput(t) {
  if (isPersistedOutput(t)) return false;
  const r = lastResultLine(t);
  return r !== null && r.simulated;
}

// 起動の結果がその場で分からない場合に、後で子が読む出力を見分ける手がかりを返す。
// バックグラウンドへ移った起動は ID と出力ファイルのパス、退避された出力は保存先のパスである。
function trackingClues(t) {
  const clues = [];
  if (/moved to the background|Command running in background with ID:/.test(t)) {
    for (const r of t.matchAll(/\bID: ([A-Za-z0-9_-]+)/g)) clues.push(r[1]);
    for (const r of t.matchAll(/Output is being written to: (\S+?)\.?(?=\s|$)/g)) clues.push(r[1]);
  }
  if (isPersistedOutput(t)) {
    for (const r of t.matchAll(/Full output saved to: (\S+?)\.?(?=\s|$)/g)) clues.push(r[1]);
  }
  return clues.map(normalizeClue);
}

// codex-agent.sh を起動した Bash の tool_result 1 件を、委譲の結果の分類へ写す。
// 疑似の起動は呼び出し側で除いてあり、ここへは渡らない。
function classifyInvocation(c) {
  const t = textOf(c);
  // 退避された出力は冒頭の抜粋しか本文に無く、抜粋には最終報告が引用した result 行が入ることがある。
  // 本文の result 行では確定せず、退避先を読んだ結果で確定する。
  if (isPersistedOutput(t)) return 'unknown';
  // ラッパーは result 行を出力の最後に出すので、退避されていない本文の result 行は確定した結果である。
  // バックグラウンドの文言より先に見るのは、最終報告の本文がその文言に触れていることがあるためである。
  const r = lastResultLine(t);
  if (r) return outcomeOfResult(r);
  if (/moved to the background|Command running in background with ID:/.test(t)) return 'unknown';
  if (/^Exit code \d+/.test(t)) return 'gptFailed';
  // 拒否は文言でなく構造で見分ける。文言は拒否した仕組みごとに違うためである。
  if (c.is_error === true && !/^Exit code/.test(t) && !/codex-agent: /.test(t)) return 'denied';
  return 'unknown';
}

const OUTCOME_KEYS = ['gptRan', 'notConfigured', 'gptUnavailable', 'gptFailed', 'denied', 'unknown', 'notInvoked'];
const DESIGNATION = '委譲: Claude 側で実装';

// 依頼文の最初の空でない行だけを指定とみなす。後続行やコードブロックの引用を数えないためである。
function isDesignatedPrompt(prompt) {
  if (typeof prompt !== 'string') return false;
  const firstNonEmptyLine = prompt.split(/\r\n|\n/).find((line) => line.trim() !== '');
  return firstNonEmptyLine !== undefined && firstNonEmptyLine.trim() === DESIGNATION;
}

// 委譲に属するすべての子の起動から最後のものを選び、その分類を返す。
// 1 つの子の記録の中では、記録順の後ろが後の起動である。
// 別の子の記録にまたがるときは時刻で決める。記録順はファイルを読んだ順でしかなく、記録どうしの前後を表さない。
// 時刻を読めない起動があるときと、最後の時刻に別の記録の起動が並ぶときは前後を決められない。
// そのときは記録ごとの最後の起動を候補とし、候補の結果がすべて同じならその分類、食い違えば結果不明とする。
function lastInvocationOutcome(subs) {
  const all = subs.flatMap((s, file) => s.invocations.map((inv) => ({ ...inv, file })));
  if (all.length === 0) return 'notInvoked';
  const lastOf = (list) => list.reduce((a, b) => (b.seq > a.seq ? b : a));
  const lastPerFile = (list) => [...new Set(list.map((inv) => inv.file))]
    .map((file) => lastOf(list.filter((inv) => inv.file === file)));
  const agreed = (list) => (new Set(list.map((inv) => inv.outcome)).size === 1 ? list[0].outcome : 'unknown');
  const candidates = lastPerFile(all);
  if (candidates.length === 1) return candidates[0].outcome;
  const times = all.map((inv) => (typeof inv.ts === 'string' ? Date.parse(inv.ts) : NaN));
  if (times.some((t) => Number.isNaN(t))) return agreed(candidates);
  const latest = Math.max(...times);
  return agreed(lastPerFile(all.filter((inv, i) => times[i] === latest)));
}

// start 以上 endExclusive 未満を期間とする。文字列で比べると、
// ミリ秒を持つ時刻(`...T00:00:00.000Z`)が `...T00:00:00Z` より小さくなり、開始日の先頭が落ちる。
// options.copiesOf は dedupeSessionCopies の返り値で、残した記録の meta ファイルが無いときに複製の脇を探すために使う。
function collect(files, start, endExclusive, options = {}) {
  const copiesOf = options.copiesOf || new Map();
  const seen = new Set();
  // at は「ファイル名と行番号」である。日時が必要な集計だけがこれを呼ぶ。
  // 日時を持たない記録や読めない記録は、黙って実績 0 へ混ぜず件数を出す。
  // 数える鍵を記録単位にするのは、同じ記録を何度判定しても 1 件とし、
  // 別の記録が同じ不正値を持つときは別々に数えるためである。
  const inRange = (ts, at) => {
    const t = typeof ts === 'string' ? Date.parse(ts) : NaN;
    if (Number.isNaN(t)) {
      if (once(`badts|${at}`)) m.badTimestamps += 1;
      return false;
    }
    return t >= start && t < endExclusive;
  };
  const once = (key) => {
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  };

  const m = {
    runs: 0,
    results: {},
    simulated: 0,
    background: 0,
    startedInBackground: 0,
    offloaded: [],
    waitCalls: 0,
    byAgent: {},
    unreadable: [],
    unparseableLines: new Map(),
    badTimestamps: 0,
    subByToolUse: new Map(),
    agentCalls: [],
  };

  // 起動の記録順。子が複数ある委譲で、時刻が同じ起動の前後を決めるために使う。
  let invocationSeq = 0;

  for (const file of files) {
    let lines;
    try {
      lines = fs.readFileSync(file, 'utf8').split('\n');
    } catch (err) {
      m.unreadable.push(`${file}: ${err.code || err.message}`);
      continue;
    }
    const pending = new Map();
    let committed = 0;
    let spawnedAgents = 0;
    const waitKeys = [];
    // 子の記録にある codex-agent.sh の起動を出現順に持つ。結果の分類は期間で絞らない。
    // 疑似かどうかは tool_result で分かるので、ファイルを読み終えてから除く。
    const invocations = [];
    const invocationById = new Map();
    // 結果がその場で分からず、後の読み取りで確定を待つ起動と、その手がかり。
    const tracking = [];
    // 手がかりで起動に結び付けた tool_use の識別子と、結び付いた起動の並び。
    const linkedReads = new Map();
    let lineNo = 0;

    for (const line of lines) {
      lineNo += 1;
      if (!line.trim()) continue;
      let o;
      try {
        o = JSON.parse(line);
      } catch {
        // 書き込み中のファイルでは、末尾の 1 行が途中で切れていることがある。
        m.unparseableLines.set(file, (m.unparseableLines.get(file) || 0) + 1);
        continue;
      }
      const at = `${file}:${lineNo}`;
      const msg = o.message;
      if (!msg || !Array.isArray(msg.content)) continue;

      for (const c of msg.content) {
        if (c.type === 'tool_use' && tracking.length > 0) {
          // 種類を問わず、入力が追跡中の起動の手がかりを含む tool_use をその起動に結び付ける。
          const input = normalizeClue(JSON.stringify(c.input === undefined ? null : c.input));
          const hits = tracking.filter((tr) => tr.clues.some((clue) => input.includes(clue)));
          if (hits.length > 0) linkedReads.set(c.id, hits);
        }
        if (c.type === 'tool_result' && linkedReads.has(c.tool_use_id)) {
          const hits = linkedReads.get(c.tool_use_id);
          linkedReads.delete(c.tool_use_id);
          const r = c.is_error === true ? null : lastResultLine(textOf(c));
          if (r && !r.simulated) {
            const outcome = outcomeOfResult(r);
            for (const tr of hits) {
              const idx = tracking.indexOf(tr);
              if (idx < 0) continue; // 先に別の読み取りで確定した。
              tr.inv.outcome = outcome;
              tracking.splice(idx, 1);
            }
          }
        }
        if (c.type === 'tool_use' && c.name === 'Bash') {
          const raw = String((c.input && c.input.command) || '');
          const cmd = stripHeredocs(raw);
          if (isCodexInvocation(raw)) {
            pending.set(c.id, { ts: o.timestamp, at, file });
            if (isSub(file)) {
              const inv = { ts: o.timestamp, seq: invocationSeq, outcome: 'unknown', simulated: false };
              invocationSeq += 1;
              invocations.push(inv);
              invocationById.set(c.id, inv);
            }
          } else if (isSub(file) && isWaitCommand(cmd) && inRange(o.timestamp, at)) {
            // 待つためだけの Bash。定義に待ち方を書く前は、これが毎回繰り返されていた。
            // Codex の起動そのものは待機に数えない。
            waitKeys.push(`wait|${c.id}`);
          }
          // 委譲は親の起動時刻で期間を選ぶ。子の実行が日付をまたぐことがあるため、
          // 子の側では期間で絞らない。
          if (/git commit/.test(cmd) && isSub(file)) committed += 1;
        }
        if (c.type === 'tool_use' && c.name === 'Agent' && isSub(file) && once(`spawned-agent|${file}|${c.id}`)) {
          spawnedAgents += 1;
        }
        if (c.type === 'tool_use' && c.name === 'Agent' && !isSub(file)) {
          const st = String((c.input && c.input.subagent_type) || '');
          // 会話を引き継いだセッションの記録には、前のセッションの委譲が同じ識別子で写っている。
          if (WRAPPER_AGENTS.includes(st) && inRange(o.timestamp, at) && once(`agent|${c.id}`)) {
            m.agentCalls.push({
              type: st,
              toolUseId: c.id,
              designated: isDesignatedPrompt(c.input && c.input.prompt),
            });
          }
        }
        if (c.type === 'tool_result' && invocationById.has(c.tool_use_id)) {
          const inv = invocationById.get(c.tool_use_id);
          const t = textOf(c);
          if (isSimulatedOutput(t)) {
            // Codex を起動していないので、委譲の結果の起動にしない。追跡もしない。
            inv.simulated = true;
          } else {
            inv.outcome = classifyInvocation(c);
            if (inv.outcome === 'unknown') {
              const clues = trackingClues(t);
              if (clues.length > 0) tracking.push({ inv, clues });
            }
          }
        }
        if (c.type === 'tool_result' && pending.has(c.tool_use_id)) {
          const run = pending.get(c.tool_use_id);
          pending.delete(c.tool_use_id);
          if (!inRange(run.ts, run.at)) continue;
          const t = textOf(c);
          // 起動は Bash の呼び出しごとに 1 件で、識別子でまとめる。会話を引き継いだセッションの記録には、
          // 前のセッションの呼び出しが同じ識別子で写っている。時刻でまとめると、
          // 同じ分に並行して起動した別々の実行が失われる。
          if (!once(`run|${c.tool_use_id}`)) continue;

          if (isSimulatedOutput(t)) {
            // 試験用フックは Codex を起動しない。実起動と分けて数える。
            m.simulated += 1;
            continue;
          }
          if (/moved to the background/.test(t)) {
            m.background += 1;
            continue; // 結果はこの時点では分からない。
          }
          // Bash の run_in_background で最初からバックグラウンドに置いた起動も、結果がこの時点では分からない。
          // 上限を超えて移った起動とは分けて数える。移った件数は Codex の実行時間を表すためである。
          if (/^Command running in background with ID:/.test(t)) {
            m.startedInBackground += 1;
            continue;
          }
          m.runs += 1;
          // 退避された本文の抜粋にある result 行は最終報告の引用でありうるので、内訳には使わない。
          // 委譲の結果の分類が退避された本文の result 行で確定しないのと揃える。
          const r = isPersistedOutput(t) ? null : lastResultLine(t);
          const res = r ? r.value : '(結果行なし)';
          m.results[res] = (m.results[res] || 0) + 1;
          const big = /Output too large \(([0-9.]+)KB\)/.exec(t);
          if (big) m.offloaded.push(Number(big[1]));
        }
      }
    }

    // 疑似の起動は Codex を起動していないので、Codex を呼んだかの判定と委譲の結果から除く。
    const realInvocations = invocations.filter((inv) => !inv.simulated);
    const calledCodex = realInvocations.length;
    // 待ち方を数えるのは、実際に Codex を呼んだサブエージェントだけである。
    if (calledCodex > 0) {
      for (const key of waitKeys) {
        if (once(key)) m.waitCalls += 1;
      }
    }
    if (isSub(file)) {
      // 記録の脇にある meta ファイルが、その実行を起こした親の tool_use を持つ。
      // これで親子を一意に結べるので、依頼文の一致で推測しない。
      const link = readAgentMeta(file, m, copiesOf.get(file));
      if (link && link.toolUseId) {
        const list = m.subByToolUse.get(link.toolUseId) || [];
        list.push({ calledCodex, committed, spawnedAgents, invocations: realInvocations });
        m.subByToolUse.set(link.toolUseId, list);
      }
    }
  }

  // 親から見た委譲を、親の tool_use の識別子でサブエージェントの記録と突き合わせる。
  // 委譲 1 件は Agent の呼び出し 1 件である。同じ依頼文を出し直した場合も、
  // それぞれ別の実行を伴うので別の委譲として数える。
  for (const call of m.agentCalls) {
    const row = (m.byAgent[call.type] = m.byAgent[call.type]
      || {
        calls: 0,
        noCodex: 0,
        committed: 0,
        spawnedAgents: 0,
        unlinked: 0,
        designated: 0,
        designatedNotInvoked: 0,
        outcomes: Object.fromEntries(OUTCOME_KEYS.map((k) => [k, 0])),
      });
    row.calls += 1;
    if (call.designated) row.designated += 1;
    const subs = m.subByToolUse.get(call.toolUseId);
    if (!subs || subs.length === 0) {
      // 対応する実行を特定できない。別の実行の状態を流用せず、不明として数える。
      row.unlinked += 1;
      continue;
    }
    // 未起動と Codex 未呼出は、どちらも疑似を除いた起動が子のどこにも無いことで決まり、件数が一致する。
    if (subs.every((s) => s.calledCodex === 0)) row.noCodex += 1;
    if (subs.some((s) => s.committed > 0)) row.committed += 1;
    if (subs.some((s) => s.spawnedAgents > 0)) row.spawnedAgents += 1;
    const outcome = lastInvocationOutcome(subs);
    if (call.designated && outcome === 'notInvoked') row.designatedNotInvoked += 1;
    row.outcomes[outcome] += 1;
  }
  delete m.subByToolUse;
  delete m.agentCalls;
  return m;
}

function main() {
  const opts = parseArgs(process.argv.slice(2));
  if (opts.help) {
    // 冒頭の用法コメントだけを出す。行数を固定すると、コメントを足したときにずれる。
    const head = fs.readFileSync(__filename, 'utf8').split('\n');
    const end = head.findIndex((line, i) => i > 2 && !line.startsWith('//'));
    console.log(head.slice(2, end).join('\n').replace(/^\/\/ ?/gm, '').trimEnd());
    return;
  }
  // 終了は「未満」で持つ。日付で指定した終了日は翌日の 00:00:00.000 未満とし、最後のミリ秒まで含める。
  // 日時で指定した終了はその時刻を含まない。
  const until = opts.until === undefined ? null : parseBoundary(opts.until, '--until');
  const endExclusive = until === null ? Date.now() : until.time + (until.isDay ? DAY : 0);
  const start = opts.since === undefined ? endExclusive - 7 * DAY : parseBoundary(opts.since, '--since').time;
  if (start >= endExclusive) throw new Error('--since が --until と同じか後になっている');

  const dir = projectsDir();
  if (!fs.existsSync(dir)) throw new Error(`記録の置き場が無い: ${dir}`);
  const failures = [];
  // 別のプロジェクト置き場へ複製された同じセッションの記録を 1 本にする。両方を読むと二重に数える。
  // 更新時刻で絞る前に行う。先に絞ると古い置き場の複製だけが落ち、その脇の meta ファイルを使えなくなるためである。
  const deduped = dedupeSessionCopies(walk(dir, [], failures), dir);
  // ファイルの更新時刻で粗く絞る。個々の出来事の時刻は collect が見る。
  const cutoff = new Date(start - 2 * DAY);
  const files = deduped.files.filter((f) => {
    try {
      return fs.statSync(f).mtime >= cutoff;
    } catch (err) {
      failures.push(`${f}: ${err.code || err.message}`);
      return false;
    }
  });

  const m = collect(files, start, endExclusive, { copiesOf: deduped.copiesOf });
  // 表示は UTC で行う。終了は「未満」なので、最後に含まれる瞬間を出す。
  const shown = `${new Date(start).toISOString()} 〜 ${new Date(endExclusive - 1).toISOString()}`;
  const unreadable = failures.concat(m.unreadable);
  const unparseableLines = {
    件数: Array.from(m.unparseableLines.values()).reduce((sum, count) => sum + count, 0),
    ファイル数: m.unparseableLines.size,
  };
  const offloaded = m.offloaded;
  const summary = {
    期間: shown,
    数え方の版: COUNTING_RULES_VERSION,
    対象ファイル数: files.length,
    複製として除いた記録: deduped.dropped,
    前方一致しない複製: deduped.divergent,
    実起動: m.runs,
    結果の内訳: m.results,
    疑似の起動: m.simulated,
    バックグラウンドへの移行: m.background,
    バックグラウンドで起動: m.startedInBackground,
    出力の退避: {
      回数: offloaded.length,
      最大KB: offloaded.length ? Math.max(...offloaded) : 0,
    },
    待つためのBash: m.waitCalls,
    委譲の内訳: m.byAgent,
    解析できなかった行: unparseableLines,
    日時が読めなかった記録: m.badTimestamps,
    読めなかった場所: unreadable,
  };

  if (opts.json) {
    console.log(JSON.stringify(summary, null, 2));
    return;
  }
  console.log(`期間: ${shown}`);
  console.log(`数え方の版: ${COUNTING_RULES_VERSION}`);
  console.log(`対象ファイル: ${files.length} 本`);
  console.log(`複製として除いた記録: ${deduped.dropped} 本`);
  if (deduped.divergent.length) {
    console.log(`前方一致しない複製: ${deduped.divergent.length} 本(除かずに数えた)`);
    for (const x of deduped.divergent.slice(0, 5)) console.log(`  ${x}`);
  }
  console.log('');
  console.log(`codex-agent.sh の実起動(結果が記録に残ったもの): ${m.runs}`);
  for (const [k, v] of Object.entries(m.results).sort((a, b) => b[1] - a[1])) {
    console.log(`  ${k.padEnd(20)} ${v}`);
  }
  console.log(`疑似の起動(試験用フックの結果で、実起動に含めない): ${m.simulated}`);
  console.log(`バックグラウンドへ移された起動: ${m.background}`);
  console.log(`最初からバックグラウンドで起動した起動(実起動に含めない): ${m.startedInBackground}`);
  console.log(
    `出力が退避された回数: ${offloaded.length}` +
      (offloaded.length ? ` (最大 ${Math.max(...offloaded)}KB)` : ''),
  );
  console.log(`待つためだけの Bash: ${m.waitCalls}`);
  console.log('');
  console.log('委譲の内訳(親から見た委譲 / Codex 未呼出 / git commit を実行 / サブエージェント起動 / 紐付け不明)');
  for (const [k, v] of Object.entries(m.byAgent).sort((a, b) => b[1].calls - a[1].calls)) {
    console.log(
      `  ${k.padEnd(16)} ${String(v.calls).padStart(4)} ${String(v.noCodex).padStart(6)}`
        + ` ${String(v.committed).padStart(6)} ${String(v.spawnedAgents).padStart(6)} ${String(v.unlinked).padStart(6)}`,
    );
  }
  console.log('');
  console.log('委譲の結果(GPT で実行 / 未設定 / GPT 使用不能 / GPT で失敗 / 拒否 / 結果不明 / 未起動)');
  for (const [k, v] of Object.entries(m.byAgent).sort((a, b) => b[1].calls - a[1].calls)) {
    console.log(`  ${k.padEnd(16)}${OUTCOME_KEYS.map((key) => ` ${String(v.outcomes[key]).padStart(6)}`).join('')}`);
  }
  console.log('');
  console.log('委譲を止める指定(指定あり / うち Codex 未起動)');
  for (const [k, v] of Object.entries(m.byAgent).sort((a, b) => b[1].calls - a[1].calls)) {
    console.log(`  ${k.padEnd(16)} ${String(v.designated).padStart(6)} ${String(v.designatedNotInvoked).padStart(12)}`);
  }
  console.log('');
  console.log(`解析できなかった行: ${unparseableLines.件数} 件(ファイル ${unparseableLines.ファイル数} 本)`);
  console.log(`日時が読めなかった記録: ${m.badTimestamps} 件(この分は数えられていない)`);
  if (unreadable.length) {
    console.log('');
    console.log(`読めなかった場所: ${unreadable.length} 件(この分は数えられていない)`);
    for (const x of unreadable.slice(0, 5)) console.log(`  ${x}`);
  }
}

// 直接起動したときだけ実行する。読み込んだときは判定の部品だけを渡す。
// 判定を外から確かめられるようにするためである。
if (require.main === module) {
  try {
    main();
  } catch (err) {
    console.error(`agent-log-metrics: ${err.message}`);
    process.exit(2);
  }
}

module.exports = {
  stripHeredocs,
  splitCommands,
  isCodexInvocation,
  parseDay,
  parseBoundary,
  dedupeSessionCopies,
  collect,
};
