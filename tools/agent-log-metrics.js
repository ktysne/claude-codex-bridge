#!/usr/bin/env node
//
// Claude Code のセッション記録から、GPT 系サブエージェントの運用の指標を数える。
//
// 用法:
//   node tools/agent-log-metrics.js [--since <YYYY-MM-DD>] [--until <YYYY-MM-DD>] [--json]
//
// --since と --until は UTC の日付として解釈する。
// --since はその日の 00:00:00.000 から、--until はその日の最後のミリ秒までを含む。
// どちらも省くと、現在までの直近 7 日を見る。
// 記録の場所は %USERPROFILE%\.claude\projects(環境変数 CLAUDE_PROJECTS_DIR で変えられる)。
//
// 数え方の約束:
//   - 起動は Bash の呼び出し 1 件を 1 件と数える。同じ呼び出しが親とサブエージェントの
//     両方の記録に現れることは無いので、時刻でまとめる重複除去は行わない。
//     まとめると、同じ分に並行して起動した別々の実行が失われる。
//   - 依頼文はヒアドキュメントでコマンドに埋め込まれる。照合の前にその本文を落とす。
//     落とさないと、依頼文が話題にしている語を実行したものとして数える。
//   - 起動の判定は、コマンドを実行単位へ切り出してから行う。区切りは `;`、`&`、`|`、改行である。
//     引用符の中とコメントの中にある区切りは区切りとして扱わない。
//   - 委譲 1 件は Agent の呼び出し 1 件である。同じ依頼文を出し直した場合も、
//     それぞれ別の実行を伴うので別の委譲として数える。
//   - 委譲と実行の紐付けは、サブエージェントの記録の脇にある `<名前>.meta.json` が持つ
//     親の tool_use の識別子で行う。依頼文の一致で推測すると、同じ依頼文を出した
//     別の定義や別の時期の実行と取り違える。
//   - 委譲の期間は親の起動時刻で選ぶ。子の実行が日付をまたぐことがあるため、子の側では絞らない。
//   - 対応する実行を特定できない委譲は「紐付け不明」として数え、未呼出には加えない。
//     別の実行の状態を流用しないためである。
//   - 出た値はすべて下限である。Codex の実行はバックグラウンドへ移ることがあり、
//     その出力が記録に残らない場合があるためである。
//   - 分類は終了コードと result 行だけで行う。依頼を果たせないまま 0 で終わった実行は数えられない。
//   - 読めなかった場所は握りつぶさず末尾に出す。測れなかったことと、実績が無いことは違う。

const fs = require('fs');
const path = require('path');

const WRAPPER_AGENTS = ['impl-hard', 'impl-light', 'impl-standard', 'codex-review', 'codex-subagent'];

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

// 実行単位が codex-agent.sh の起動かを判定する。
// 先頭に並ぶ環境変数の代入と、bash 自身のオプションは読み飛ばす。
// `cat tools/codex-agent.sh` のように読むだけのコマンドは起動と数えない。
const INVOCATION = /^(?:[A-Za-z_][A-Za-z0-9_]*=\S*\s+)*(?:bash|sh)\s+(?:-\S+\s+)*["']?\S*codex-agent\.sh["']?(?:\s|$)/;

// 起動の判定は 1 か所に置く。実起動、未呼出、待機の集計で同じ判定を使う。
function isCodexInvocation(cmd) {
  return splitCommands(stripHeredocs(cmd)).some((seg) => INVOCATION.test(seg.trim()));
}

const isSub = (file) => file.includes('/subagents/');

// サブエージェントの記録の脇には `<名前>.meta.json` があり、その実行を起こした親の
// tool_use の識別子(`toolUseId`)と定義名を持つ。これで親子を一意に結ぶ。
// 読めない場合は結ばない。依頼文の一致で代用すると、同じ依頼文を出した別の実行と取り違える。
function readAgentMeta(file, m) {
  const metaPath = file.replace(/\.jsonl$/, '.meta.json');
  if (!fs.existsSync(metaPath)) return null;
  try {
    return JSON.parse(fs.readFileSync(metaPath, 'utf8'));
  } catch (err) {
    m.unreadable.push(`${metaPath}: ${err.code || err.message}`);
    return null;
  }
}

const textOf = (c) => {
  if (typeof c.content === 'string') return c.content;
  if (Array.isArray(c.content)) return c.content.map((x) => (x && x.text) || '').join('\n');
  return '';
};

// start 以上 endExclusive 未満を期間とする。文字列で比べると、
// ミリ秒を持つ時刻(`...T00:00:00.000Z`)が `...T00:00:00Z` より小さくなり、開始日の先頭が落ちる。
function collect(files, start, endExclusive) {
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
    background: 0,
    offloaded: [],
    waitCalls: 0,
    byAgent: {},
    unreadable: [],
    unparseableLines: new Map(),
    badTimestamps: 0,
    subByToolUse: new Map(),
    agentCalls: [],
  };

  for (const file of files) {
    let lines;
    try {
      lines = fs.readFileSync(file, 'utf8').split('\n');
    } catch (err) {
      m.unreadable.push(`${file}: ${err.code || err.message}`);
      continue;
    }
    const pending = new Map();
    let calledCodex = 0;
    let committed = 0;
    const waitKeys = [];
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
        if (c.type === 'tool_use' && c.name === 'Bash') {
          const raw = String((c.input && c.input.command) || '');
          const cmd = stripHeredocs(raw);
          if (isCodexInvocation(raw)) {
            pending.set(c.id, { ts: o.timestamp, at, file });
            if (isSub(file)) calledCodex += 1;
          } else if (isSub(file) && /\.output|\bsleep\b|\buntil\b/.test(cmd) && inRange(o.timestamp, at)) {
            // 待つためだけの Bash。定義に待ち方を書く前は、これが毎回繰り返されていた。
            // Codex の起動そのものは待機に数えない。
            waitKeys.push(`wait|${file}|${c.id}`);
          }
          // 委譲は親の起動時刻で期間を選ぶ。子の実行が日付をまたぐことがあるため、
          // 子の側では期間で絞らない。
          if (/git commit/.test(cmd) && isSub(file)) committed += 1;
        }
        if (c.type === 'tool_use' && c.name === 'Agent' && !isSub(file)) {
          const st = String((c.input && c.input.subagent_type) || '');
          if (WRAPPER_AGENTS.includes(st) && inRange(o.timestamp, at)) {
            m.agentCalls.push({ type: st, toolUseId: c.id });
          }
        }
        if (c.type === 'tool_result' && pending.has(c.tool_use_id)) {
          const run = pending.get(c.tool_use_id);
          pending.delete(c.tool_use_id);
          if (!inRange(run.ts, run.at)) continue;
          const t = textOf(c);
          // 起動は Bash の呼び出しごとに 1 件である。時刻でまとめると、
          // 同じ分に並行して起動した別々の実行が失われる。
          if (!once(`run|${run.file}|${c.tool_use_id}`)) continue;

          if (/moved to the background/.test(t)) {
            m.background += 1;
            continue; // 結果はこの時点では分からない。
          }
          m.runs += 1;
          const res = (/codex-agent: result=(ok|rate-limited|unavailable|failed exit=\d+)/.exec(t) || [])[1]
            || '(結果行なし)';
          m.results[res] = (m.results[res] || 0) + 1;
          const big = /Output too large \(([0-9.]+)KB\)/.exec(t);
          if (big) m.offloaded.push(Number(big[1]));
        }
      }
    }

    // 待ち方を数えるのは、実際に Codex を呼んだサブエージェントだけである。
    if (calledCodex > 0) {
      for (const key of waitKeys) {
        if (once(key)) m.waitCalls += 1;
      }
    }
    if (isSub(file)) {
      // 記録の脇にある meta ファイルが、その実行を起こした親の tool_use を持つ。
      // これで親子を一意に結べるので、依頼文の一致で推測しない。
      const link = readAgentMeta(file, m);
      if (link && link.toolUseId) {
        const list = m.subByToolUse.get(link.toolUseId) || [];
        list.push({ calledCodex, committed });
        m.subByToolUse.set(link.toolUseId, list);
      }
    }
  }

  // 親から見た委譲を、親の tool_use の識別子でサブエージェントの記録と突き合わせる。
  // 委譲 1 件は Agent の呼び出し 1 件である。同じ依頼文を出し直した場合も、
  // それぞれ別の実行を伴うので別の委譲として数える。
  for (const call of m.agentCalls) {
    const row = (m.byAgent[call.type] = m.byAgent[call.type]
      || { calls: 0, noCodex: 0, committed: 0, unlinked: 0 });
    row.calls += 1;
    const subs = m.subByToolUse.get(call.toolUseId);
    if (!subs || subs.length === 0) {
      // 対応する実行を特定できない。別の実行の状態を流用せず、不明として数える。
      row.unlinked += 1;
      continue;
    }
    if (subs.every((s) => s.calledCodex === 0)) row.noCodex += 1;
    if (subs.some((s) => s.committed > 0)) row.committed += 1;
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
  // 終了日は「翌日の 00:00:00.000 未満」とする。終了日の最後のミリ秒まで含める。
  const endExclusive = opts.until === undefined ? Date.now() : parseDay(opts.until, '--until') + DAY;
  const start = opts.since === undefined ? endExclusive - 7 * DAY : parseDay(opts.since, '--since');
  if (start >= endExclusive) throw new Error('--since が --until より後になっている');

  const dir = projectsDir();
  if (!fs.existsSync(dir)) throw new Error(`記録の置き場が無い: ${dir}`);
  const failures = [];
  // ファイルの更新時刻で粗く絞る。個々の出来事の時刻は collect が見る。
  const cutoff = new Date(start - 2 * DAY);
  const files = walk(dir, [], failures).filter((f) => {
    try {
      return fs.statSync(f).mtime >= cutoff;
    } catch (err) {
      failures.push(`${f}: ${err.code || err.message}`);
      return false;
    }
  });

  const m = collect(files, start, endExclusive);
  // 表示は指定と同じ UTC で行う。終了は「未満」なので、最後に含まれる瞬間を出す。
  const shown = `${new Date(start).toISOString()} 〜 ${new Date(endExclusive - 1).toISOString()}`;
  const unreadable = failures.concat(m.unreadable);
  const unparseableLines = {
    件数: Array.from(m.unparseableLines.values()).reduce((sum, count) => sum + count, 0),
    ファイル数: m.unparseableLines.size,
  };
  const offloaded = m.offloaded;
  const summary = {
    期間: shown,
    対象ファイル数: files.length,
    実起動: m.runs,
    結果の内訳: m.results,
    バックグラウンドへの移行: m.background,
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
  console.log(`対象ファイル: ${files.length} 本`);
  console.log('');
  console.log(`codex-agent.sh の実起動(結果が記録に残ったもの): ${m.runs}`);
  for (const [k, v] of Object.entries(m.results).sort((a, b) => b[1] - a[1])) {
    console.log(`  ${k.padEnd(20)} ${v}`);
  }
  console.log(`バックグラウンドへ移された起動: ${m.background}`);
  console.log(
    `出力が退避された回数: ${offloaded.length}` +
      (offloaded.length ? ` (最大 ${Math.max(...offloaded)}KB)` : ''),
  );
  console.log(`待つためだけの Bash: ${m.waitCalls}`);
  console.log('');
  console.log('委譲の内訳(親から見た委譲 / Codex 未呼出 / git commit を実行 / 紐付け不明)');
  for (const [k, v] of Object.entries(m.byAgent).sort((a, b) => b[1].calls - a[1].calls)) {
    console.log(
      `  ${k.padEnd(16)} ${String(v.calls).padStart(4)} ${String(v.noCodex).padStart(6)}`
        + ` ${String(v.committed).padStart(6)} ${String(v.unlinked).padStart(6)}`,
    );
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

module.exports = { stripHeredocs, splitCommands, isCodexInvocation, parseDay, collect };
