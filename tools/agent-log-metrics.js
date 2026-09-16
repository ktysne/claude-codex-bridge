#!/usr/bin/env node
//
// Claude Code のセッション記録から、GPT 系サブエージェントの運用の指標を数える。
//
// 用法:
//   node tools/agent-log-metrics.js [--since <YYYY-MM-DD>] [--until <YYYY-MM-DD>] [--json]
//
// 既定の期間は直近 7 日である。
// 記録の場所は %USERPROFILE%\.claude\projects(環境変数 CLAUDE_PROJECTS_DIR で変えられる)。
//
// 数え方の約束:
//   - 起動は Bash の呼び出し 1 件を 1 件と数える。同じ呼び出しが親とサブエージェントの
//     両方の記録に現れることは無いので、時刻でまとめる重複除去は行わない。
//     まとめると、同じ分に並行して起動した別々の実行が失われる。
//   - 依頼文はヒアドキュメントでコマンドに埋め込まれる。照合の前にその本文を落とす。
//     落とさないと、依頼文が話題にしている語を実行したものとして数える。
//   - 委譲の紐付けは、親セッションと依頼文の全文で行う。先頭だけで照合すると、共通の前置きから
//     始まる別の依頼が衝突する。親セッションを鍵に含めないと、別のセッションが同じ依頼文を
//     出していたときに、その結果で上書きされる。
//   - 再送をまとめる単位は「親セッション、定義名、依頼文」である。別のセッションや別の定義への
//     同じ依頼文は、独立した委譲として数える。
//   - 委譲の期間は親の起動時刻で選ぶ。子の実行が日付をまたぐことがあるため、子の側では絞らない。
//   - 対応する実行を特定できない委譲は「紐付け不明」として数え、未呼出には加えない。
//     別の実行の状態を流用しないためである。
//   - 出た値はすべて下限である。Codex の実行はバックグラウンドへ移ることがあり、
//     その出力が記録に残らない場合があるためである。
//   - 分類は終了コードと result 行だけで行う。依頼を果たせないまま 0 で終わった実行は数えられない。
//   - 読めなかった場所は握りつぶさず末尾に出す。測れなかったことと、実績が無いことは違う。

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const WRAPPER_AGENTS = ['impl-hard', 'impl-light', 'impl-standard', 'codex-review', 'codex-subagent'];

function parseArgs(argv) {
  const opts = { json: false };
  for (let i = 0; i < argv.length; i += 1) {
    const a = argv[i];
    if (a === '--since') opts.since = argv[++i];
    else if (a === '--until') opts.until = argv[++i];
    else if (a === '--json') opts.json = true;
    else if (a === '-h' || a === '--help') opts.help = true;
    else throw new Error(`不明なオプションである: ${a}`);
  }
  return opts;
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

// 依頼文の同一性は全文で判断する。先頭だけで照合すると、共通の前置きから始まる別の依頼が衝突する。
const promptKey = (s) => crypto.createHash('sha1').update(String(s ?? '')).digest('hex');

const isSub = (file) => file.includes('/subagents/');

// 親セッションの記録は `<プロジェクト>/<セッション>.jsonl`、
// サブエージェントの記録は `<プロジェクト>/<セッション>/subagents/agent-*.jsonl` にある。
// 両者から同じ鍵を作り、委譲の突き合わせを親セッションの中に閉じる。
const sessionOf = (file) => file.replace(/\/subagents\/.*$/, '').replace(/\.jsonl$/, '');

const textOf = (c) => {
  if (typeof c.content === 'string') return c.content;
  if (Array.isArray(c.content)) return c.content.map((x) => (x && x.text) || '').join('\n');
  return '';
};

function collect(files, since, until) {
  const inRange = (ts) => typeof ts === 'string' && ts >= since && ts <= until;
  const seen = new Set();
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
    subPrompts: new Map(),
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
    let firstPrompt = null;
    let calledCodex = 0;
    let committed = 0;
    const waitKeys = [];

    for (const line of lines) {
      if (!line.trim()) continue;
      let o;
      try {
        o = JSON.parse(line);
      } catch {
        // 書き込み中のファイルでは、末尾の 1 行が途中で切れていることがある。
        m.unparseableLines.set(file, (m.unparseableLines.get(file) || 0) + 1);
        continue;
      }
      if (!firstPrompt && isSub(file) && o.type === 'user' && o.message && typeof o.message.content === 'string') {
        firstPrompt = promptKey(o.message.content);
      }
      const msg = o.message;
      if (!msg || !Array.isArray(msg.content)) continue;

      for (const c of msg.content) {
        if (c.type === 'tool_use' && c.name === 'Bash') {
          const cmd = stripHeredocs(String((c.input && c.input.command) || ''));
          // スクリプトを読むだけの `cat tools/codex-agent.sh` などを起動と数えない。
          if (/(^|[;&|]\s*)(bash|sh)\s+[^;|&]*codex-agent\.sh(\s|$)/.test(cmd)) {
            pending.set(c.id, { ts: o.timestamp, file });
            if (isSub(file)) calledCodex += 1;
          } else if (isSub(file) && inRange(o.timestamp) && /\.output|\bsleep\b|\buntil\b/.test(cmd)) {
            // 待つためだけの Bash。定義に待ち方を書く前は、これが毎回繰り返されていた。
            // Codex の起動そのものは待機に数えない。
            waitKeys.push(`wait|${file}|${c.id}`);
          }
          // 委譲は親の起動時刻で期間を選ぶ。子の実行が日付をまたぐことがあるため、
          // 子の側では期間で絞らない。
          if (/git commit/.test(cmd) && isSub(file)) committed += 1;
        }
        if (c.type === 'tool_use' && c.name === 'Agent' && !isSub(file) && inRange(o.timestamp)) {
          const st = String((c.input && c.input.subagent_type) || '');
          if (WRAPPER_AGENTS.includes(st)) {
            m.agentCalls.push({ type: st, prompt: promptKey(c.input.prompt), session: sessionOf(file) });
          }
        }
        if (c.type === 'tool_result' && pending.has(c.tool_use_id)) {
          const run = pending.get(c.tool_use_id);
          pending.delete(c.tool_use_id);
          if (!inRange(run.ts)) continue;
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
    if (isSub(file) && firstPrompt) {
      // 同じ親セッションで同じ依頼文の記録が複数あることがある。
      // 後から読んだもので上書きせず、候補として並べる。
      const key = `${sessionOf(file)}|${firstPrompt}`;
      const list = m.subPrompts.get(key) || [];
      list.push({ calledCodex, committed });
      m.subPrompts.set(key, list);
    }
  }

  // 親から見た委譲を、サブエージェントの記録と突き合わせる。
  // 突き合わせの鍵は親セッションと依頼文である。依頼文だけを鍵にすると、
  // 別のセッションが同じ依頼文を出していたときに、その結果で上書きされる。
  // 同じ親セッションから同じ定義へ出した同じ依頼文は、再送とみなして 1 件の委譲として数える。
  for (const call of m.agentCalls) {
    if (!once(`unit|${call.session}|${call.type}|${call.prompt}`)) continue;
    const row = (m.byAgent[call.type] = m.byAgent[call.type]
      || { calls: 0, noCodex: 0, committed: 0, unlinked: 0 });
    row.calls += 1;
    const subs = m.subPrompts.get(`${call.session}|${call.prompt}`);
    if (!subs || subs.length === 0) {
      // 対応する実行を特定できない。別の実行の状態を流用せず、不明として数える。
      row.unlinked += 1;
      continue;
    }
    // 紐付いた実行のどれかが Codex を呼んでいれば、未呼出には数えない。
    if (subs.every((s) => s.calledCodex === 0)) row.noCodex += 1;
    if (subs.some((s) => s.committed > 0)) row.committed += 1;
  }
  delete m.subPrompts;
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
  const until = opts.until ? `${opts.until}T23:59:59Z` : new Date().toISOString();
  const since = opts.since
    ? `${opts.since}T00:00:00Z`
    : new Date(Date.parse(until) - 7 * 24 * 3600 * 1000).toISOString();

  const dir = projectsDir();
  if (!fs.existsSync(dir)) throw new Error(`記録の置き場が無い: ${dir}`);
  const failures = [];
  // ファイルの更新時刻で粗く絞る。個々の出来事の時刻は collect が見る。
  const cutoff = new Date(Date.parse(since) - 2 * 24 * 3600 * 1000);
  const files = walk(dir, [], failures).filter((f) => {
    try {
      return fs.statSync(f).mtime >= cutoff;
    } catch (err) {
      failures.push(`${f}: ${err.code || err.message}`);
      return false;
    }
  });

  const m = collect(files, since, until);
  const unreadable = failures.concat(m.unreadable);
  const unparseableLines = {
    件数: Array.from(m.unparseableLines.values()).reduce((sum, count) => sum + count, 0),
    ファイル数: m.unparseableLines.size,
  };
  const offloaded = m.offloaded;
  const summary = {
    期間: `${since} 〜 ${until}`,
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
    読めなかった場所: unreadable,
  };

  if (opts.json) {
    console.log(JSON.stringify(summary, null, 2));
    return;
  }
  console.log(`期間: ${since} 〜 ${until}`);
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
  if (unreadable.length) {
    console.log('');
    console.log(`読めなかった場所: ${unreadable.length} 件(この分は数えられていない)`);
    for (const x of unreadable.slice(0, 5)) console.log(`  ${x}`);
  }
}

try {
  main();
} catch (err) {
  console.error(`agent-log-metrics: ${err.message}`);
  process.exit(2);
}
