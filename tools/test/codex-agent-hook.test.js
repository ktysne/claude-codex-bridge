'use strict';

const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const path = require('node:path');
const test = require('node:test');

const HOOK_SCRIPT = path.resolve(__dirname, '..', 'codex-agent-hook.js');
const WRAPPER = 'bash ~/.claude/tools/codex-agent.sh';
const SCRATCHPAD = 'E:\\Temp\\claude\\proj\\f997603b\\scratchpad';
const PROMPTS_DIR = 'C:/Users/someone/.claude/codex-agent/prompts';
const WORKTREE = 'D:/Desktop/Develop/claude-codex-bridge';

function runHook(args, stdin) {
  const result = spawnSync(process.execPath, [HOOK_SCRIPT, ...args], { input: stdin, encoding: 'utf8' });
  return { status: result.status, stdout: result.stdout, stderr: result.stderr };
}

function hookInput(toolName, toolInput) {
  return JSON.stringify({
    session_id: 's',
    hook_event_name: 'PreToolUse',
    tool_name: toolName,
    tool_input: toolInput,
  });
}

function bash(agent, command) {
  return runHook([agent], hookInput('Bash', { command, timeout: 600000 }));
}

function write(agent, filePath) {
  return runHook([agent], hookInput('Write', { file_path: filePath, content: '依頼文' }));
}

function assertAllowed(result) {
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout, '');
  assert.equal(result.stderr, '');
}

function assertRejected(result) {
  assert.equal(result.status, 2);
  assert.equal(result.stdout, '');
  assert.match(result.stderr, /この定義は Codex への転送だけを行う/);
  assert.match(result.stderr, /--wait <実行 ID>/);
  assert.match(result.stderr, /--header-of "<出力ファイル>"/);
  assert.equal(result.stderr.trim().split('\n').length, 1, '理由は 1 段落にする');
}

const PROMPT_DIRECTORY = `${SCRATCHPAD}/codex-agent-codex-review-20260928-120000-k7q2m9`;
const PROMPT_FILE = `${PROMPT_DIRECTORY}/prompt.md`;
const FORWARD = `${WRAPPER} codex-review < "${PROMPT_FILE}"`;
const NEW_PROMPT = `${WRAPPER} --new-prompt codex-review "${SCRATCHPAD}"`;
const WAIT = `${WRAPPER} --wait codex-review-20260928-120000-12345`;
const RUN_ID_OF = `${WRAPPER} --header-of "C:/Users/someone/AppData/Local/Temp/claude/tasks/b1x2.output"`;

test('Bash は依頼文のファイルを渡す転送の形を許す', () => {
  assertAllowed(bash('codex-review', FORWARD));
  assertAllowed(bash('codex-subagent', `${WRAPPER} codex-subagent < "E:/Temp/claude/p/scratchpad/codex-agent-codex-subagent-20260928-120000-abc123/prompt.md"`));
});

test('窓口の impl-hard、impl-standard、impl-light に転送の形と依頼文の置き場を許す', () => {
  for (const agent of ['impl-hard', 'impl-standard', 'impl-light']) {
    const promptFile = `${SCRATCHPAD}/codex-agent-${agent}-20261003-120000-abc123/prompt.md`;
    assertAllowed(bash(agent, `${WRAPPER} --new-prompt ${agent} "${SCRATCHPAD}"`));
    assertAllowed(write(agent, promptFile));
    assertAllowed(bash(agent, `${WRAPPER} ${agent} -C "${WORKTREE}" < "${promptFile}"`));
    assertAllowed(bash(agent, WAIT));
    assertAllowed(bash(agent, RUN_ID_OF));
  }
});

test('窓口は別の区分の定義名への転送と、転送以外の道具を拒否する', () => {
  assertRejected(bash('impl-light', `${WRAPPER} impl-hard < "${SCRATCHPAD}/codex-agent-impl-hard-20261003-120000-abc123/prompt.md"`));
  assertRejected(write('impl-light', `${SCRATCHPAD}/codex-agent-impl-standard-20261003-120000-abc123/prompt.md`));
  assertRejected(bash('impl-hard', 'git diff'));
  assertRejected(runHook(['impl-standard'], hookInput('Edit', { file_path: `${WORKTREE}/README.md` })));
});

test('Bash は --new-prompt を親ディレクトリの有無にかかわらず許す', () => {
  assertAllowed(bash('codex-review', NEW_PROMPT));
  assertAllowed(bash('codex-review', `${WRAPPER} --new-prompt codex-review`));
});

test('Bash は転送の入力が依頼文の名前と置き場に合わないものを拒否する', () => {
  assertRejected(bash('codex-review', `${WRAPPER} codex-review < "C:/Users/someone/.codex-subagent/auth.json"`));
  assertRejected(bash('codex-review', `${WRAPPER} codex-review < "D:/work/repo/codex-agent-codex-review-20260928-120000-k7q2m9.md"`));
  assertRejected(bash('codex-review', `${WRAPPER} codex-review < "E:/Temp/claude/proj/scratchpad/codex-agent-codex-review-20260928-120000-k7q2m9.md"`));
  assertRejected(bash('codex-review', `${WRAPPER} codex-review < "E:/Temp/claude/proj/scratchpad/codex-agent-codex-subagent-20260928-120000-k7q2m9/prompt.md"`));
});

test('Bash は --new-prompt の定義名違い、余分な語、連結記号を拒否する', () => {
  assertRejected(bash('codex-review', `${WRAPPER} --new-prompt codex-subagent "${SCRATCHPAD}"`));
  assertRejected(bash('codex-review', `${NEW_PROMPT} extra`));
  assertRejected(bash('codex-review', `${NEW_PROMPT}; cat x`));
  assertRejected(bash('codex-review', `${NEW_PROMPT} && cat x`));
  assertRejected(bash('codex-review', `${WRAPPER} --new-prompt codex-review ${SCRATCHPAD}`));
  assertRejected(bash('codex-review', `${WRAPPER} --new-prompt codex-review "a$(cat x)"`));
  assertRejected(bash('codex-review', `${WRAPPER} --new-prompt codex-review "a\`cat x\`"`));
});

test('Bash は -C のパスを引用符で囲んでも囲まなくても許す', () => {
  const file = '"E:/Temp/scratchpad/codex-agent-codex-subagent-20260928-120000-abc123/prompt.md"';
  assertAllowed(bash('codex-subagent', `${WRAPPER} codex-subagent -C "D:/My Projects/repo" < ${file}`));
  assertAllowed(bash('codex-subagent', `${WRAPPER} codex-subagent -C D:/Desktop/Develop/repo < ${file}`));
});

test('Bash は -C を定義名の前に置いた転送も許す', () => {
  for (const agent of ['impl-standard', 'codex-subagent']) {
    const file = `"${SCRATCHPAD}/codex-agent-${agent}-20261009-013311-3eARdk/prompt.md"`;
    assertAllowed(bash(agent, `${WRAPPER} -C "${WORKTREE}" ${agent} < ${file}`));
    assertAllowed(bash(agent, `${WRAPPER} -C D:/Desktop/Develop/repo ${agent} < ${file}`));
  }
});

test('Bash は -C を 2 回置いた転送、パスの無い -C、別の定義名への転送を拒否する', () => {
  const file = `"${SCRATCHPAD}/codex-agent-impl-standard-20261009-013311-3eARdk/prompt.md"`;
  assertRejected(bash('impl-standard', `${WRAPPER} -C "${WORKTREE}" impl-standard -C "${WORKTREE}" < ${file}`));
  assertRejected(bash('impl-standard', `${WRAPPER} -C impl-standard < ${file}`));
  assertRejected(bash('impl-standard', `${WRAPPER} -C "${WORKTREE}" impl-hard < ${file}`));
});

test('Bash は --wait と --header-of の形を許す', () => {
  assertAllowed(bash('codex-review', WAIT));
  assertAllowed(bash('codex-review', RUN_ID_OF));
});

test('Bash は前後の空白と CR を除いて照合する', () => {
  assertAllowed(bash('codex-review', `  ${WAIT} \r\n`));
});

test('Bash は転送の形にコマンドを連結したものを拒否する', () => {
  for (const suffix of ['; cat x', ' && cat x', ' || cat x', ' | cat', ' $(cat x)', ' `cat x`', '\ncat x']) {
    assertRejected(bash('codex-review', FORWARD + suffix));
    assertRejected(bash('codex-review', WAIT + suffix));
    assertRejected(bash('codex-review', RUN_ID_OF + suffix));
  }
});

test('Bash は引用符の中に連結や展開の文字を含むパスを拒否する', () => {
  assertRejected(bash('codex-review', `${WRAPPER} codex-review < "a$(cat x).md"`));
  assertRejected(bash('codex-review', `${WRAPPER} codex-review < "a\`cat x\`.md"`));
  assertRejected(bash('codex-review', `${WRAPPER} codex-review < "a"; cat "b.md"`));
  assertRejected(bash('codex-review', `${WRAPPER} --header-of "a\nb"`));
  assertRejected(bash('codex-subagent', `${WRAPPER} codex-subagent -C D:/x;cat < "a.md"`));
  assertRejected(bash('codex-subagent', `${WRAPPER} codex-subagent -C D:/My Projects < "a.md"`));
});

test('Bash は出力のリダイレクトを拒否する', () => {
  assertRejected(bash('codex-review', `${FORWARD} > out.txt`));
  assertRejected(bash('codex-review', `${WAIT} > out.txt`));
  assertRejected(bash('codex-review', `${WRAPPER} codex-review > "a.md"`));
});

test('Bash は別の定義名への転送を拒否する', () => {
  assertRejected(bash('codex-review', `${WRAPPER} codex-subagent < "a.md"`));
  assertRejected(bash('codex-subagent', `${WRAPPER} codex-review < "a.md"`));
  assertRejected(bash('codex-review', `${WRAPPER} impl-hard < "a.md"`));
});

test('Bash はラッパー以外のコマンドを拒否する', () => {
  for (const command of ['cat README.md', 'ls -la', 'git diff', 'bash tools/codex-agent.sh codex-review < "a.md"']) {
    assertRejected(bash('codex-review', command));
  }
});

test('Bash は先頭に空白以外の語を足したものを拒否する', () => {
  assertRejected(bash('codex-review', `x=1 ${FORWARD}`));
  assertRejected(bash('codex-review', `cd /tmp && ${FORWARD}`));
  assertRejected(bash('codex-review', `time ${WAIT}`));
});

test('Bash は --wait の実行 ID に許さない文字があれば拒否する', () => {
  for (const id of ['a/b', 'a b', 'a$b', 'a;b', '"a"', '']) {
    assertRejected(bash('codex-review', `${WRAPPER} --wait ${id}`));
  }
});

test('Bash は --header-of の引用符の無いパスと、ほかの引数を足したものを拒否する', () => {
  assertRejected(bash('codex-review', `${WRAPPER} --header-of C:/x/b1x2.output`));
  assertRejected(bash('codex-review', `${WRAPPER} --header-of "C:/x/b1x2.output" codex-review`));
  assertRejected(bash('codex-review', `${WRAPPER} codex-review --effort low < "a.md"`));
});

test('Write はスクラッチパッドと prompts の置き場の、定義名と形の合う名前を許す', () => {
  assertAllowed(write('codex-review', PROMPT_FILE));
  assertAllowed(write('codex-review', `${PROMPTS_DIR}/codex-agent-codex-review-20260928-120000-K7Q2M9/prompt.md`));
  assertAllowed(write('codex-subagent', `${SCRATCHPAD}/codex-agent-codex-subagent-20260928-120000-abc123/prompt.md`));
});

test('Write は作業ツリーのソースのパスを拒否する', () => {
  assertRejected(write('codex-review', `${WORKTREE}/tools/codex-agent.sh`));
  assertRejected(write('codex-review', `${WORKTREE}/codex-agent-codex-review-20260928-120000-k7q2m9/prompt.md`));
  assertRejected(write('codex-review', `${WORKTREE}/scratchpad/../tools/codex-agent-codex-review-20260928-120000-k7q2m9/prompt.md`));
});

test('Write は別の定義名の名前を拒否する', () => {
  assertRejected(write('codex-review', `${SCRATCHPAD}/codex-agent-codex-subagent-20260928-120000-abc123/prompt.md`));
  assertRejected(write('codex-subagent', `${PROMPTS_DIR}/codex-agent-impl-hard-20260928-120000-abc123/prompt.md`));
});

test('Write は形の違う名前を拒否する', () => {
  for (const name of [
    'codex-agent-codex-review-2026092-120000-k7q2m9/prompt.md',
    'codex-agent-codex-review-20260928-12000-k7q2m9/prompt.md',
    'codex-agent-codex-review-20260928-120000-k7q2m/prompt.md',
    'codex-agent-codex-review-20260928-120000-k7q2m9.md/prompt.md',
    'codex-agent-codex-review-20260928-120000-k7_2m9/prompt.md',
    'codex-agent-codex-review-20260928-120000-k7q2m9.md',
    'codex-agent-codex-review-20260928-120000-k7q2m9/notes.md',
    'notes.md',
  ]) {
    assertRejected(write('codex-review', `${SCRATCHPAD}/${name}`));
  }
});

test('Bash と Write 以外のツールを拒否する', () => {
  assertRejected(runHook(['codex-review'], hookInput('Read', { file_path: `${WORKTREE}/README.md` })));
  assertRejected(runHook(['codex-review'], hookInput('Edit', { file_path: `${WORKTREE}/README.md` })));
});

test('JSON でない入力と、tool_name か tool_input の無い入力を拒否する', () => {
  assertRejected(runHook(['codex-review'], 'not json'));
  assertRejected(runHook(['codex-review'], ''));
  assertRejected(runHook(['codex-review'], JSON.stringify({ tool_input: { command: WAIT } })));
  assertRejected(runHook(['codex-review'], JSON.stringify({ tool_name: 'Bash' })));
});

test('定義名の引数が無いか不正なら、許す形の入力でも拒否する', () => {
  assertRejected(runHook([], hookInput('Bash', { command: WAIT })));
  assertRejected(runHook(['impl-hard-claude'], hookInput('Bash', { command: WAIT })));
  assertRejected(runHook(['codex-review;'], hookInput('Bash', { command: WAIT })));
});

test('定義名の引数が不正なときの理由は、定義名を列挙せずに書く', () => {
  const result = runHook(['impl-hard-claude'], hookInput('Bash', { command: WAIT }));
  assertRejected(result);
  assert.match(result.stderr, /フックの定義名の引数\(impl-hard-claude\)がラッパー役の定義名ではない/);
});
