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
//   - 親セッションとサブエージェントの記録は同じ出力を二重に持つため、
//     セッション(サブエージェントの記録は親のディレクトリへ畳む)と分単位の時刻で重複を除く。
//   - 出た値はすべて下限である。Codex の実行はバックグラウンドへ移ることがあり、
//     その出力が記録に残らない場合があるためである。
//   - 分類は終了コードと result 行だけで行う。依頼を果たせないまま 0 で終わった実行は数えられない。

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

function walk(dir, out = []) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return out;
  }
  for (const e of entries) {
    const p = path.join(dir, e.name).split(path.sep).join('/');
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith('.jsonl')) out.push(p);
  }
  return out;
}

// サブエージェントの記録は親セッションのディレクトリへ畳む。重複の除去に使う。
const sessionOf = (file) => file.replace(/\/subagents\/.*$/, '');
const isSub = (file) => file.includes('/subagents/');

const textOf = (c) => {
  if (typeof c.content === 'string') return c.content;
  if (Array.isArray(c.content)) return c.content.map((x) => (x && x.text) || '').join('\n');
  return '';
};

const norm = (s) => String(s || '').replace(/\s+/g, ' ').trim().slice(0, 120);

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
    subPrompts: new Map(),
    agentCalls: [],
  };

  for (const file of files) {
    let lines;
    try {
      lines = fs.readFileSync(file, 'utf8').split('\n');
    } catch {
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
        continue;
      }
      if (!firstPrompt && isSub(file) && o.type === 'user' && o.message && typeof o.message.content === 'string') {
        firstPrompt = norm(o.message.content);
      }
      const msg = o.message;
      if (!msg || !Array.isArray(msg.content)) continue;

      for (const c of msg.content) {
        if (c.type === 'tool_use' && c.name === 'Bash') {
          const cmd = String((c.input && c.input.command) || '');
          // スクリプトを読むだけの `cat tools/codex-agent.sh` などを起動と数えない。
          if (/(^|[;&|]\s*)(bash|sh)\s+[^;|&]*codex-agent\.sh(\s|$)/.test(cmd)) {
            pending.set(c.id, { ts: o.timestamp, file });
            if (isSub(file)) calledCodex += 1;
          }
          if (/git commit/.test(cmd) && isSub(file) && inRange(o.timestamp)) committed += 1;
          // 待つためだけの Bash。定義に待ち方を書く前は、これが毎回繰り返されていた。
          if (isSub(file) && inRange(o.timestamp) && /\.output|\bsleep\b|\buntil\b/.test(cmd)) {
            waitKeys.push(`wait|${file}|${o.timestamp}`);
          }
        }
        if (c.type === 'tool_use' && c.name === 'Agent' && !isSub(file) && inRange(o.timestamp)) {
          const st = String((c.input && c.input.subagent_type) || '');
          if (WRAPPER_AGENTS.includes(st)) {
            m.agentCalls.push({ type: st, prompt: norm(c.input.prompt) });
          }
        }
        if (c.type === 'tool_result' && pending.has(c.tool_use_id)) {
          const run = pending.get(c.tool_use_id);
          pending.delete(c.tool_use_id);
          if (!inRange(run.ts)) continue;
          const t = textOf(c);
          const key = `run|${sessionOf(run.file)}|${String(run.ts).slice(0, 16)}`;
          if (!once(key)) continue;

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
      m.subPrompts.set(firstPrompt, { calledCodex, committed });
    }
  }

  // 親から見た委譲を、依頼文でサブエージェントの記録と突き合わせる。
  const counted = new Set();
  for (const call of m.agentCalls) {
    if (counted.has(call.prompt)) continue;
    counted.add(call.prompt);
    const sub = m.subPrompts.get(call.prompt);
    if (!sub) continue;
    const row = (m.byAgent[call.type] = m.byAgent[call.type] || { calls: 0, noCodex: 0, committed: 0 });
    row.calls += 1;
    if (sub.calledCodex === 0) row.noCodex += 1;
    if (sub.committed > 0) row.committed += 1;
  }
  delete m.subPrompts;
  delete m.agentCalls;
  return m;
}

function main() {
  const opts = parseArgs(process.argv.slice(2));
  if (opts.help) {
    console.log(fs.readFileSync(__filename, 'utf8').split('\n').slice(2, 18).join('\n').replace(/^\/\/ ?/gm, ''));
    return;
  }
  const until = opts.until ? `${opts.until}T23:59:59Z` : new Date().toISOString();
  const since = opts.since
    ? `${opts.since}T00:00:00Z`
    : new Date(Date.parse(until) - 7 * 24 * 3600 * 1000).toISOString();

  const dir = projectsDir();
  // ファイルの更新時刻で粗く絞る。個々の出来事の時刻は collect が見る。
  const cutoff = new Date(Date.parse(since) - 2 * 24 * 3600 * 1000);
  const files = walk(dir).filter((f) => {
    try {
      return fs.statSync(f).mtime >= cutoff;
    } catch {
      return false;
    }
  });

  const m = collect(files, since, until);
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
  console.log('委譲の内訳(親から見た起動 / Codex 未呼出 / git commit を実行)');
  for (const [k, v] of Object.entries(m.byAgent).sort((a, b) => b[1].calls - a[1].calls)) {
    console.log(`  ${k.padEnd(16)} ${String(v.calls).padStart(4)} ${String(v.noCodex).padStart(6)} ${String(v.committed).padStart(6)}`);
  }
}

try {
  main();
} catch (err) {
  console.error(`agent-log-metrics: ${err.message}`);
  process.exit(2);
}
