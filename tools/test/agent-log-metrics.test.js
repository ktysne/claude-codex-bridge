'use strict';

const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');

const {
  stripHeredocs,
  splitCommands,
  isCodexInvocation,
  parseDay,
  collect,
} = require('../agent-log-metrics.js');

const METRICS_SCRIPT = path.resolve(__dirname, '..', 'agent-log-metrics.js');

function withTempDir(fn) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-log-metrics-test-'));
  try {
    return fn(dir);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

function logPath(...parts) {
  return path.join(...parts).split(path.sep).join('/');
}

function writeText(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text, 'utf8');
  return file;
}

function writeJsonl(file, rows) {
  const text = rows
    .map((row) => (typeof row === 'string' ? row : JSON.stringify(row)))
    .join('\n');
  return writeText(file, `${text}\n`);
}

function event(timestamp, ...content) {
  return { timestamp, message: { content } };
}

function bashUse(id, command) {
  return { type: 'tool_use', name: 'Bash', id, input: { command } };
}

function bashEvent({ timestamp, id, command, result }) {
  const content = [bashUse(id, command)];
  if (result !== undefined) content.push({ type: 'tool_result', tool_use_id: id, content: result });
  return event(timestamp, ...content);
}

function agentUse(id, subagentType, prompt) {
  return {
    type: 'tool_use',
    name: 'Agent',
    id,
    input: { subagent_type: subagentType, prompt },
  };
}

function agentEvent(timestamp, id, subagentType, prompt) {
  return event(timestamp, agentUse(id, subagentType, prompt));
}

function writeMeta(jsonlFile, toolUseId) {
  return writeText(
    jsonlFile.replace(/\.jsonl$/, '.meta.json'),
    `${JSON.stringify({ toolUseId })}\n`,
  );
}

const OUTCOME_KEYS = ['gptRan', 'notConfigured', 'gptUnavailable', 'gptFailed', 'denied', 'unknown', 'notInvoked'];

// 指定しない分類を 0 で埋めた outcomes を作る。
function outcomesOf(values) {
  return Object.fromEntries(OUTCOME_KEYS.map((k) => [k, values[k] || 0]));
}

const INVOKE = "bash ~/.claude/tools/codex-agent.sh impl-standard <<'EOF'\n依頼文\nEOF";

// 起動の Bash 1 件。isError を与えると tool_result に is_error を付ける。
// result を省くと tool_result を記録に残さない。
function invokeEvent(timestamp, id, result, isError) {
  const content = [bashUse(id, INVOKE)];
  if (result !== undefined) {
    const r = { type: 'tool_result', tool_use_id: id, content: result };
    if (isError !== undefined) r.is_error = isError;
    content.push(r);
  }
  return event(timestamp, ...content);
}

// 親の記録に委譲を並べ、委譲ごとに子の記録と meta を書く。
// children は [toolUseId, events] の並びで、同じ toolUseId を複数回書くと子が複数ある委譲になる。
function writeDelegations(root, calls, children) {
  const sessionDir = path.join(root, 'project', 'session-outcomes');
  const parentFile = writeJsonl(logPath(sessionDir, 'session.jsonl'), calls.map(([id, type]) => (
    agentEvent('2026-09-10T10:00:00.000Z', id, type, `依頼 ${id}`)
  )));
  const files = [parentFile];
  children.forEach(([toolUseId, rows], i) => {
    const file = writeJsonl(logPath(sessionDir, 'subagents', `agent-${i}.jsonl`), rows);
    writeMeta(file, toolUseId);
    files.push(file);
  });
  return files;
}

function assertOutcomeInvariants(byAgent) {
  for (const row of Object.values(byAgent)) {
    const total = Object.values(row.outcomes).reduce((sum, v) => sum + v, 0);
    assert.equal(total, row.calls - row.unlinked);
    assert.equal(row.outcomes.notInvoked, row.noCodex);
  }
}

test('collect は委譲ごとの結果を最後の起動で 7 つの分類に分ける', () => {
  // 分類の取り違えが値の差として現れるよう、定義ごとに異なる件数を置く。
  withTempDir((root) => {
    const ts = '2026-09-10T10:01:00.000Z';
    const files = writeDelegations(
      root,
      [
        ['ran-1', 'impl-standard'],
        ['not-configured-1', 'impl-standard'],
        ['not-configured-2', 'impl-standard'],
        ['unavailable-1', 'impl-light'],
        ['unavailable-2', 'impl-light'],
        ['unavailable-3', 'impl-light'],
        ['failed-1', 'impl-hard'],
        ['failed-2', 'impl-hard'],
        ['denied-1', 'impl-light'],
        ['unknown-1', 'impl-hard'],
        ['not-invoked-1', 'impl-standard'],
        ['unlinked-1', 'impl-standard'],
      ],
      [
        ['ran-1', [invokeEvent(ts, 'b1', 'final\ncodex-agent: result=ok')]],
        ['not-configured-1', [invokeEvent(ts, 'b2', 'Exit code 3\ncodex-agent: result=failed exit=3', true)]],
        ['not-configured-2', [invokeEvent(ts, 'b3', 'codex-agent: result=failed exit=3')]],
        ['unavailable-1', [invokeEvent(ts, 'b4', 'Exit code 75\ncodex-agent: result=rate-limited', true)]],
        ['unavailable-2', [invokeEvent(ts, 'b5', 'codex-agent: result=unavailable')]],
        ['unavailable-3', [invokeEvent(ts, 'b6', 'codex-agent: result=rate-limited')]],
        ['failed-1', [invokeEvent(ts, 'b7', 'codex-agent: result=failed exit=1')]],
        ['failed-2', [invokeEvent(ts, 'b8', 'Exit code 2\n定義が見つからない')]],
        ['denied-1', [invokeEvent(ts, 'b9', 'Permission for this action was denied.', true)]],
        ['unknown-1', [invokeEvent(ts, 'b10')]],
        ['not-invoked-1', [bashEvent({ timestamp: ts, id: 'b11', command: 'echo 自分で実装した' })]],
      ],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ gptRan: 1, notConfigured: 2, notInvoked: 1 }));
    assert.deepEqual(metrics.byAgent['impl-light'].outcomes, outcomesOf({ gptUnavailable: 3, denied: 1 }));
    assert.deepEqual(metrics.byAgent['impl-hard'].outcomes, outcomesOf({ gptFailed: 2, unknown: 1 }));
    assert.equal(metrics.byAgent['impl-standard'].unlinked, 1);
    assertOutcomeInvariants(metrics.byAgent);
  });
});

test('collect は起動し直した委譲を最後の起動の結果で数える', () => {
  // 上限の後に起動し直して成功した委譲は、GPT で実行したものである。
  // 子が複数ある委譲でも、すべての子の起動を並べて最後のものを使う。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['retry', 'impl-standard'], ['two-children', 'impl-light']],
      [
        ['retry', [
          invokeEvent('2026-09-10T10:01:00.000Z', 'r1', 'codex-agent: result=rate-limited'),
          invokeEvent('2026-09-10T10:02:00.000Z', 'r2', 'codex-agent: result=ok'),
        ]],
        ['two-children', [invokeEvent('2026-09-10T10:05:00.000Z', 'c2', 'codex-agent: result=unavailable')]],
        ['two-children', [invokeEvent('2026-09-10T10:01:00.000Z', 'c1', 'codex-agent: result=ok')]],
      ],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ gptRan: 1 }));
    assert.deepEqual(metrics.byAgent['impl-light'].outcomes, outcomesOf({ gptUnavailable: 1 }));
    assertOutcomeInvariants(metrics.byAgent);
  });
});

test('collect は is_error でも Exit code で始まる結果を拒否に数えない', () => {
  // 拒否は構造で判定する。終了コードを持つ結果はラッパーが動いた後の失敗である。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['exit-2', 'impl-standard']],
      [['exit-2', [invokeEvent('2026-09-10T10:01:00.000Z', 'e1', 'Exit code 2\n不明なエージェント', true)]]],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ gptFailed: 1 }));
  });
});

test('collect は最後の起動がバックグラウンドへ移った委譲を結果不明に数える', () => {
  // 先の起動が成功していても、最後の起動の結果が記録に無ければ行き先は分からない。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['bg', 'impl-standard']],
      [['bg', [
        invokeEvent('2026-09-10T10:01:00.000Z', 'g1', 'codex-agent: result=ok'),
        invokeEvent('2026-09-10T10:02:00.000Z', 'g2', 'Command running in background. The command was moved to the background.'),
      ]]],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ unknown: 1 }));
  });
});

// 任意の tool_use 1 件と、その tool_result。
function toolEvent(timestamp, id, name, input, result, isError) {
  const r = { type: 'tool_result', tool_use_id: id, content: result };
  if (isError !== undefined) r.is_error = isError;
  return event(timestamp, { type: 'tool_use', name, id, input }, r);
}

const BG_TEXT = 'Command did not complete within its 600s timeout and was moved to the background (ID: bgx123abc).'
  + ' Output is being written to: E:\\Temp\\claude\\proj\\tasks\\bgx123abc.output. You will be notified when it completes.';
const PERSISTED_TEXT = '<persisted-output>\nOutput too large (318.7KB). Full output saved to: '
  + 'C:\\Users\\someone\\.claude\\projects\\proj\\tool-results\\toolu_persist.txt\n\nPreview (first 2KB):\ncodex-agent: agent=impl-light\n...\n</persisted-output>';

test('collect はバックグラウンドへ移った起動を、ID を含む読み取りの結果で確定する', () => {
  // バックグラウンドの出力は後で子が読む。手がかりで結び付けた読み取りの result 行を起動自身の結果とする。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['bg-ok', 'impl-standard']],
      [['bg-ok', [
        invokeEvent('2026-09-10T10:01:00.000Z', 'i1', BG_TEXT),
        toolEvent('2026-09-10T10:12:00.000Z', 't1', 'Bash', { command: 'tail -n 20 "$TMP/tasks/bgx123abc.output"' },
          '最終報告\ncodex-agent: result=ok'),
      ]]],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ gptRan: 1 }));
    // 補うのは outcomes だけで、既存の数え方は変えない。
    assert.equal(metrics.background, 1);
    assert.deepEqual(metrics.results, {});
  });
});

test('collect は退避された出力を、区切り文字の違うパスの読み取りで確定する', () => {
  // 記録上の区切りは `\` でも、読み取りの入力では `/` になることがある。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['persisted', 'impl-light']],
      [['persisted', [
        invokeEvent('2026-09-10T10:01:00.000Z', 'p1', PERSISTED_TEXT),
        toolEvent('2026-09-10T10:02:00.000Z', 'p2', 'Read',
          { file_path: 'C:/Users/someone/.claude/projects/proj/tool-results/toolu_persist.txt', offset: 3000 },
          '...\ncodex-agent: result=rate-limited'),
      ]]],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-light'].outcomes, outcomesOf({ gptUnavailable: 1 }));
  });
});

test('collect は最終報告がバックグラウンドの文言に触れていても、result 行で確定する', () => {
  // 退避されていない本文の result 行はラッパーが最後に出した確定結果である。
  // 最終報告の本文がバックグラウンドへの移行を話題にしていても、結果不明にしない。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['mentions-bg', 'impl-standard']],
      [['mentions-bg', [
        invokeEvent('2026-09-10T10:01:00.000Z', 'm1',
          'codex-agent: log=C:/logs/x.log\nBash ツールの上限で moved to the background と出る場合の手順を書いた。\ncodex-agent: result=ok'),
      ]]],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ gptRan: 1 }));
  });
});

test('collect は退避の抜粋に引用された result 行では確定せず、退避先の読み取りで確定する', () => {
  // 抜粋は出力の冒頭だけで、最終報告が引用した result 行が入ることがある。本来の result 行は退避先の末尾にある。
  withTempDir((root) => {
    const quoted = PERSISTED_TEXT.replace('...\n', '報告の例:\ncodex-agent: result=rate-limited\n');
    const files = writeDelegations(
      root,
      [['persisted-quote', 'impl-light']],
      [['persisted-quote', [
        invokeEvent('2026-09-10T10:01:00.000Z', 'q1', quoted),
        toolEvent('2026-09-10T10:02:00.000Z', 'q2', 'Read',
          { file_path: 'C:\\Users\\someone\\.claude\\projects\\proj\\tool-results\\toolu_persist.txt' },
          '...\ncodex-agent: result=ok'),
      ]]],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-light'].outcomes, outcomesOf({ gptRan: 1 }));
  });
});

test('collect は最終報告が persisted-output のタグを引用していても、result 行で確定する', () => {
  // 退避された出力と見なすのは、本文がタグの外枠で始まるものだけである。引用しただけの本文は通常の出力として読む。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['quotes-tag', 'impl-standard']],
      [['quotes-tag', [
        invokeEvent('2026-09-10T10:01:00.000Z', 'k1',
          'codex-agent: log=C:/logs/x.log\n退避された出力は <persisted-output> で始まり、Full output saved to: の行を持つ。\ncodex-agent: result=ok'),
      ]]],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ gptRan: 1 }));
  });
});

test('collect は子の記録をまたいで順序を確定できなくても、候補の結果がすべて同じならその分類に数える', () => {
  // どの起動が最後でも分類が変わらないなら、結果不明に落とさない。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['bad-time-same', 'impl-standard'], ['same-time-same', 'impl-light']],
      [
        ['bad-time-same', [invokeEvent('not-a-date', 'v1', 'codex-agent: result=ok')]],
        ['bad-time-same', [invokeEvent('2026-09-10T10:01:00.000Z', 'v2', 'codex-agent: result=ok')]],
        ['same-time-same', [invokeEvent('2026-09-10T10:03:00.000Z', 'w1', 'codex-agent: result=unavailable')]],
        ['same-time-same', [invokeEvent('2026-09-10T10:03:00.000Z', 'w2', 'codex-agent: result=rate-limited')]],
      ],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ gptRan: 1 }));
    assert.deepEqual(metrics.byAgent['impl-light'].outcomes, outcomesOf({ gptUnavailable: 1 }));
  });
});

test('collect は子の記録をまたいで順序を確定できない委譲を結果不明にし、ファイルの順に左右されない', () => {
  // 記録順はファイルを読んだ順でしかない。時刻が読めない起動や、最後の時刻が別の記録で並ぶ起動は、前後を決められない。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['bad-time', 'impl-standard'], ['same-time', 'impl-light'], ['ordered', 'impl-hard']],
      [
        ['bad-time', [invokeEvent('not-a-date', 'x1', 'codex-agent: result=ok')]],
        ['bad-time', [invokeEvent('2026-09-10T10:01:00.000Z', 'x2', 'codex-agent: result=unavailable')]],
        ['same-time', [invokeEvent('2026-09-10T10:03:00.000Z', 'y1', 'codex-agent: result=ok')]],
        ['same-time', [invokeEvent('2026-09-10T10:03:00.000Z', 'y2', 'codex-agent: result=unavailable')]],
        ['ordered', [invokeEvent('2026-09-10T10:01:00.000Z', 'z1', 'codex-agent: result=unavailable')]],
        ['ordered', [invokeEvent('2026-09-10T10:04:00.000Z', 'z2', 'codex-agent: result=ok')]],
      ],
    );
    const range = [parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000];

    const forward = collect(files, ...range);
    const reversed = collect([files[0], ...files.slice(1).reverse()], ...range);

    assert.deepEqual(forward.byAgent['impl-standard'].outcomes, outcomesOf({ unknown: 1 }));
    assert.deepEqual(forward.byAgent['impl-light'].outcomes, outcomesOf({ unknown: 1 }));
    assert.deepEqual(forward.byAgent['impl-hard'].outcomes, outcomesOf({ gptRan: 1 }));
    assert.deepEqual(reversed.byAgent, forward.byAgent);
  });
});

test('collect は手がかりを含まない読み取りや is_error の結果では確定しない', () => {
  // 差分や文書を読んだ結果の result 行や、失敗した読み取りを起動の結果として拾わない。
  withTempDir((root) => {
    const files = writeDelegations(
      root,
      [['unrelated-read', 'impl-standard'], ['error-read', 'impl-light']],
      [
        ['unrelated-read', [
          invokeEvent('2026-09-10T10:01:00.000Z', 'u1', BG_TEXT),
          toolEvent('2026-09-10T10:02:00.000Z', 'u2', 'Read', { file_path: 'docs/gpt-agents.md' },
            '例:\ncodex-agent: result=ok'),
        ]],
        ['error-read', [
          invokeEvent('2026-09-10T10:01:00.000Z', 'e1', BG_TEXT),
          toolEvent('2026-09-10T10:02:00.000Z', 'e2', 'Bash', { command: 'cat E:/Temp/claude/proj/tasks/bgx123abc.output' },
            'codex-agent: result=ok', true),
        ]],
      ],
    );

    const metrics = collect(files, parseDay('2026-09-10', '--since'), parseDay('2026-09-10', '--until') + 24 * 3600 * 1000);

    assert.deepEqual(metrics.byAgent['impl-standard'].outcomes, outcomesOf({ unknown: 1 }));
    assert.deepEqual(metrics.byAgent['impl-light'].outcomes, outcomesOf({ unknown: 1 }));
  });
});

test('--json の委譲の内訳は各定義に outcomes を含む', () => {
  // JSON を読む利用者が結果の内訳を取り出せることを、CLI を通して確かめる。
  withTempDir((root) => {
    writeDelegations(
      root,
      [['json-1', 'impl-standard'], ['json-2', 'impl-light']],
      [
        ['json-1', [invokeEvent('2026-09-10T10:01:00.000Z', 'j1', 'codex-agent: result=ok')]],
        ['json-2', [invokeEvent('2026-09-10T10:01:00.000Z', 'j2', 'denied', true)]],
      ],
    );

    const result = spawnSync(
      process.execPath,
      [METRICS_SCRIPT, '--since', '2026-09-10', '--until', '2026-09-10', '--json'],
      {
        cwd: path.resolve(__dirname, '../..'),
        env: { ...process.env, CLAUDE_PROJECTS_DIR: root },
        encoding: 'utf8',
      },
    );

    assert.equal(result.status, 0, result.stderr);
    const summary = JSON.parse(result.stdout);
    assert.deepEqual(summary.委譲の内訳['impl-standard'].outcomes, outcomesOf({ gptRan: 1 }));
    assert.deepEqual(summary.委譲の内訳['impl-light'].outcomes, outcomesOf({ denied: 1 }));
  });
});

test('stripHeredocs は対応する終端記号まで本文を落とす', () => {
  // 依頼文は実行内容ではないため、本文中の git commit や sleep を指標に混ぜない。
  const forms = [
    ["<<'EOF'", 'EOF'],
    ['<<"EOF"', 'EOF'],
    ['<<EOF', 'EOF'],
    ['<<-EOF', '\tEOF'],
  ];

  for (const [start, end] of forms) {
    const command = `bash ~/.claude/tools/codex-agent.sh impl-standard ${start}\ngit commit -am fake\nsleep 30\n${end}`;
    const stripped = stripHeredocs(command);
    assert.equal(stripped, 'bash ~/.claude/tools/codex-agent.sh impl-standard <<HEREDOC');
    assert.doesNotMatch(stripped, /git commit|sleep/);
  }
});

test('splitCommands はシェル区切りを分け、引用符とコメントの区切りを保つ', () => {
  // 実行単位を正しく切り出さないと、依頼文や表示用文字列にある区切りを実行として数えてしまう。
  const separated = splitCommands('echo one; echo two & echo three | echo four\necho five');
  assert.deepEqual(separated.map((segment) => segment.trim()), [
    'echo one',
    'echo two',
    'echo three',
    'echo four',
    'echo five',
  ]);

  const quotedAndCommented = splitCommands(
    `printf "quoted; & |\n bash ~/.claude/tools/codex-agent.sh impl-standard"\necho after # comment; & |`,
  );
  assert.deepEqual(quotedAndCommented.map((segment) => segment.trim()).filter(Boolean), [
    'printf "quoted; & |\n bash ~/.claude/tools/codex-agent.sh impl-standard"',
    'echo after',
  ]);
});

test('isCodexInvocation は起動形だけを判定する', () => {
  // bash/sh による実行だけを数え、cat の閲覧や引用符内の説明文は起動に含めない。
  assert.equal(
    isCodexInvocation(
      "bash ~/.claude/tools/codex-agent.sh impl-standard <<'EOF'\ngit commit と sleep は実行しない\nEOF",
    ),
    true,
  );
  assert.equal(
    isCodexInvocation(
      'CODEX_HOME="$HOME/.codex-subagent" TRACE=1 bash ~/.claude/tools/codex-agent.sh impl-standard',
    ),
    true,
  );
  assert.equal(isCodexInvocation('cat tools/codex-agent.sh'), false);
  assert.equal(
    isCodexInvocation('echo first\nbash ~/.claude/tools/codex-agent.sh impl-standard'),
    true,
  );
  assert.equal(
    isCodexInvocation(
      'printf "説明だけ\nbash ~/.claude/tools/codex-agent.sh impl-standard"',
    ),
    false,
  );
});

test('parseDay は UTC の日付を返し、不正な日付を拒否する', () => {
  // 期間境界をローカル時刻に左右されない UTC の日の始点で固定する。
  assert.equal(parseDay('2026-09-16', '--since'), Date.UTC(2026, 8, 16));
  assert.throws(() => parseDay('2026-02-30', '--since'), /実在しない日付/);
  assert.throws(() => parseDay('2026/09/16', '--since'), /YYYY-MM-DD/);
});

test('collect は同一分の起動、結果、待機、親子の紐付けを規則どおり数える', () => {
  // 起動と委譲は時刻や依頼文ではまとめず、各 tool_use と meta.json の識別子を単位に数える。
  withTempDir((root) => {
    const sessionDir = path.join(root, 'session-main');
    const parentFile = logPath(sessionDir, 'session.jsonl');
    const subagentsDir = path.join(sessionDir, 'subagents');
    const crossDayFile = logPath(subagentsDir, 'agent-one.jsonl');
    const noCodexFile = logPath(subagentsDir, 'agent-two.jsonl');
    const measuredFile = logPath(subagentsDir, 'agent-three.jsonl');
    const unlinkedFile = logPath(subagentsDir, 'agent-four.jsonl');
    const promptPrefix = '同じ接頭辞を持つ依頼文: ';
    const prompt1 = `${promptPrefix}コミットする仕事`;
    const prompt2 = `${promptPrefix}Codexを呼ばない仕事`;

    // 紐付けの取り違えを集計の差として検出できるよう、agent-use-1 と agent-use-2 の定義を分ける。
    // 同じ定義にすると、2 件の紐付けが入れ替わっても行の合計が変わらない。
    writeJsonl(parentFile, [
      agentEvent('2026-09-10T23:59:59.999Z', 'agent-use-1', 'impl-standard', prompt1),
      agentEvent('2026-09-10T12:00:00.000Z', 'agent-use-2', 'impl-light', prompt2),
      agentEvent('2026-09-10T12:00:01.000Z', 'agent-use-3', 'impl-light', prompt2),
      event('not-a-date', agentUse('agent-use-invalid', 'impl-standard', prompt1)),
      '{"timestamp":"2026-09-10T12:00:02.000Z",',
    ]);

    writeJsonl(crossDayFile, [
      bashEvent({
        timestamp: '2026-09-11T00:00:00.000Z',
        id: 'cross-day-codex',
        command: "bash ~/.claude/tools/codex-agent.sh impl-standard <<'EOF'\ngit commit -am '依頼文にあるだけ'\nsleep 30\nEOF",
        result: 'codex-agent: result=ok',
      }),
      bashEvent({
        timestamp: '2026-09-11T00:00:01.000Z',
        id: 'cross-day-commit',
        command: 'git commit -am "実際の実行"',
      }),
    ]);
    writeMeta(crossDayFile, 'agent-use-1');

    writeJsonl(measuredFile, [
      bashEvent({
        timestamp: '2026-09-10T12:34:56.000Z',
        id: 'same-minute-ok',
        command: "bash ~/.claude/tools/codex-agent.sh impl-standard <<'EOF'\ngit commit -am '依頼文にあるだけ'\nsleep 30\nuntil test -f done; do :; done\nEOF",
        result: [
          { text: 'codex-agent: result=ok' },
          { text: 'Output too large (950KB)' },
        ],
      }),
      bashEvent({
        timestamp: '2026-09-10T12:34:56.000Z',
        id: 'same-minute-rate-limited',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'codex-agent: result=rate-limited',
      }),
      bashEvent({
        timestamp: '2026-09-10T12:34:57.000Z',
        id: 'unavailable',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'codex-agent: result=unavailable',
      }),
      bashEvent({
        timestamp: '2026-09-10T12:34:58.000Z',
        id: 'failed',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'codex-agent: result=failed exit=3',
      }),
      bashEvent({
        timestamp: '2026-09-10T12:34:59.000Z',
        id: 'no-result-line',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: '通常の出力だけ',
      }),
      bashEvent({
        timestamp: '2026-09-10T12:35:00.000Z',
        id: 'background',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'The command was moved to the background',
      }),
      bashEvent({
        timestamp: '2026-09-10T12:35:01.000Z',
        id: 'wait-output',
        command: 'test -f task.output',
      }),
      bashEvent({
        timestamp: '2026-09-10T12:35:02.000Z',
        id: 'wait-sleep',
        command: 'sleep 1',
      }),
      bashEvent({
        timestamp: '2026-09-10T12:35:03.000Z',
        id: 'wait-until',
        command: 'until test -f done; do :; done',
      }),
    ]);
    writeJsonl(noCodexFile, [
      bashEvent({
        timestamp: '2026-09-10T12:33:00.000Z',
        id: 'no-codex-bash',
        command: 'echo delegated work completed',
      }),
    ]);
    writeMeta(noCodexFile, 'agent-use-2');

    writeJsonl(unlinkedFile, [
      bashEvent({
        timestamp: '2026-09-10T12:36:00.000Z',
        id: 'unlinked-codex',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'codex-agent: result=ok',
      }),
    ]);
    assert.equal(fs.existsSync(unlinkedFile.replace(/\.jsonl$/, '.meta.json')), false);

    const metrics = collect(
      [parentFile, crossDayFile, noCodexFile, measuredFile, unlinkedFile],
      parseDay('2026-09-10', '--since'),
      parseDay('2026-09-11', '--until'),
    );

    assert.equal(metrics.runs, 6);
    assert.deepEqual(metrics.results, {
      ok: 2,
      'rate-limited': 1,
      unavailable: 1,
      'failed exit=3': 1,
      '(結果行なし)': 1,
    });
    assert.equal(metrics.background, 1);
    assert.deepEqual(metrics.offloaded, [950]);
    assert.equal(metrics.waitCalls, 3);
    assert.deepEqual(metrics.byAgent, {
      'impl-standard': {
        calls: 1,
        noCodex: 0,
        committed: 1,
        unlinked: 0,
        outcomes: outcomesOf({ gptRan: 1 }),
      },
      'impl-light': {
        calls: 2,
        noCodex: 1,
        committed: 0,
        unlinked: 1,
        outcomes: outcomesOf({ notInvoked: 1 }),
      },
    });
    assert.equal(metrics.badTimestamps, 1);
    assert.equal(metrics.unparseableLines.get(parentFile), 1);
    assert.equal(metrics.unreadable.length, 0);
  });
});

test('collect は開始日の先頭と終了日の最後を含み、期間外を除く', () => {
  // 期間は start 以上 endExclusive 未満であり、開始日の先頭と終了日の最後のミリ秒を含める。
  withTempDir((root) => {
    const file = logPath(root, 'session', 'subagents', 'boundary.jsonl');
    writeJsonl(file, [
      bashEvent({
        timestamp: '2026-09-09T23:59:59.999Z',
        id: 'before-start',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'codex-agent: result=ok',
      }),
      bashEvent({
        timestamp: '2026-09-10T00:00:00.000Z',
        id: 'at-start',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'codex-agent: result=ok',
      }),
      bashEvent({
        timestamp: '2026-09-10T23:59:59.999Z',
        id: 'at-end',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'codex-agent: result=ok',
      }),
      bashEvent({
        timestamp: '2026-09-11T00:00:00.000Z',
        id: 'after-end',
        command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
        result: 'codex-agent: result=ok',
      }),
    ]);

    const metrics = collect(
      [file],
      parseDay('2026-09-10', '--since'),
      parseDay('2026-09-11', '--until'),
    );

    assert.equal(metrics.runs, 2);
    assert.deepEqual(metrics.results, { ok: 2 });
  });
});

test('--since と --until は開始日の先頭から終了日の最後のミリ秒までを含む', () => {
  // collect は終端を「未満」で受け取る。終了日を含める変換はメイン処理が行うので、CLI を通して確かめる。
  withTempDir((root) => {
    const file = logPath(root, 'session', 'subagents', 'boundary.jsonl');
    writeJsonl(file, [
      ['2026-09-09T23:59:59.999Z', 'before-start'],
      ['2026-09-10T00:00:00.000Z', 'at-start'],
      ['2026-09-10T23:59:59.999Z', 'at-end'],
      ['2026-09-11T00:00:00.000Z', 'after-end'],
    ].map(([timestamp, id]) => bashEvent({
      timestamp,
      id,
      command: 'bash ~/.claude/tools/codex-agent.sh impl-standard',
      result: 'codex-agent: result=ok',
    })));

    const result = spawnSync(
      process.execPath,
      [METRICS_SCRIPT, '--since', '2026-09-10', '--until', '2026-09-10', '--json'],
      {
        cwd: path.resolve(__dirname, '../..'),
        env: { ...process.env, CLAUDE_PROJECTS_DIR: root },
        encoding: 'utf8',
      },
    );

    assert.equal(result.status, 0, result.stderr);
    const summary = JSON.parse(result.stdout);
    assert.equal(summary.実起動, 2);
    assert.deepEqual(summary.結果の内訳, { ok: 2 });
  });
});

test('collect は読み込めないファイルを実績 0 と区別して報告する', () => {
  // 読めなかった場所を報告すれば、データ不足を実績が無い状態と取り違えない。
  withTempDir((root) => {
    const missingFile = logPath(root, 'missing.jsonl');
    const invalidMetaFile = logPath(root, 'session', 'subagents', 'invalid-meta.jsonl');
    writeJsonl(invalidMetaFile, []);
    writeText(invalidMetaFile.replace(/\.jsonl$/, '.meta.json'), '{invalid json');
    const metrics = collect(
      [missingFile, invalidMetaFile],
      parseDay('2026-09-10', '--since'),
      parseDay('2026-09-11', '--until'),
    );

    assert.equal(metrics.runs, 0);
    assert.equal(metrics.unreadable.length, 2);
    assert.match(metrics.unreadable[0], /missing\.jsonl: ENOENT$/);
    assert.match(metrics.unreadable[1], /invalid-meta\.meta\.json:/);
  });
});

test('メイン処理は存在しない記録ディレクトリをエラーとして報告する', () => {
  // 入力場所が無い場合も無言で空集計にせず、指定した一時ディレクトリを含めて報告する。
  withTempDir((root) => {
    const missingDir = path.join(root, 'missing-projects');
    const result = spawnSync(
      process.execPath,
      [METRICS_SCRIPT, '--since', '2026-09-10', '--until', '2026-09-10', '--json'],
      {
        cwd: path.resolve(__dirname, '../..'),
        env: { ...process.env, CLAUDE_PROJECTS_DIR: missingDir },
        encoding: 'utf8',
      },
    );

    assert.equal(result.status, 2);
    assert.match(result.stderr, /agent-log-metrics: 記録の置き場が無い:/);
    assert.match(result.stderr, new RegExp(missingDir.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  });
});

test('--help は用法コメントだけを出力する', () => {
  // ヘルプに実装本体が混ざると、利用者が読む説明とソースコードを区別できない。
  const result = spawnSync(process.execPath, [METRICS_SCRIPT, '--help'], {
    cwd: path.resolve(__dirname, '../..'),
    encoding: 'utf8',
  });

  assert.equal(result.status, 0);
  assert.equal(result.stderr, '');
  assert.match(result.stdout, /Claude Code のセッション記録から/);
  assert.match(result.stdout, /--since/);
  assert.doesNotMatch(result.stdout, /const fs = require/);
  assert.doesNotMatch(result.stdout, /function collect/);
  assert.doesNotMatch(result.stdout, /module\.exports/);
});
