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
      'impl-standard': { calls: 1, noCodex: 0, committed: 1, unlinked: 0 },
      'impl-light': { calls: 2, noCodex: 1, committed: 0, unlinked: 1 },
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
