#!/usr/bin/env node
//
// ラッパー役のサブエージェント定義(WRAPPER_AGENTS)の PreToolUse フック。
// Bash と Write を、Codex への転送に要る形だけに絞る。仕様の正本は docs/gpt-agents.md の「ラッパー役の定義の道具を絞る」。
//
// 用法: node tools/codex-agent-hook.js <定義名>   (標準入力にフックの JSON を受け取る)
// 許すときは何も出さずに終了コード 0、拒否するときは標準エラーに理由を書いて終了コード 2 で終わる。

'use strict';

const fs = require('node:fs');

const WRAPPER_AGENTS = ['codex-review', 'codex-subagent', 'impl-hard', 'impl-standard', 'impl-light'];
const WRAPPER_PREFIX = 'bash ~/\\.claude/tools/codex-agent\\.sh';
// 二重引用符の中で bash が展開や終端に使う文字と、行を分ける文字を含めない。
const QUOTED_PATH = '"[^"$`\\r\\n\\0]+"';
// 引用符なしのパスは、シェルが特別に扱わない文字だけにする。
const BARE_PATH = "[\\p{L}\\p{N}._~/:+@,=-]+";
const SEP = '[ \\t]+';
const RUN_ID = '[A-Za-z0-9._-]+';
const PROMPTS_DIR_SUFFIX = '/.claude/codex-agent/prompts';

function allowedFormsText(agent) {
  const name = agent || '<定義名>';
  return [
    `Bash の bash ~/.claude/tools/codex-agent.sh ${name} [-C <パス>] < "<依頼文のファイル>"`,
    `Bash の bash ~/.claude/tools/codex-agent.sh --new-prompt ${name} ["<親ディレクトリ>"]`,
    'bash ~/.claude/tools/codex-agent.sh --wait <実行 ID>',
    'bash ~/.claude/tools/codex-agent.sh --header-of "<出力ファイル>"',
    `Write の <スクラッチパッド>/codex-agent-${name}-<YYYYMMDD>-<HHMMSS>-<英数字 6 文字>/prompt.md`
      + '(スクラッチパッドが無いときは %USERPROFILE%/.claude/codex-agent/prompts/ の下)',
  ].join('、');
}

function rejection(agent, reason) {
  return `codex-agent-hook: ${reason}ため拒否した。`
    + 'この定義は Codex への転送だけを行う。'
    + `許す形は ${allowedFormsText(agent)} だけである。`
    + '引数や連結を変えた形も拒否する。この拒否は権限判定による拒否と同じく扱い、この文言をそのまま報告して停止する。';
}

function bashPatterns(agent) {
  const start = `^${WRAPPER_PREFIX}${SEP}`;
  return {
    forward: new RegExp(`${start}${agent}(?:${SEP}-C${SEP}(?:${QUOTED_PATH}|${BARE_PATH}))?${SEP}<[ \\t]*"([^"]+)"$`, 'u'),
    others: [
      new RegExp(`${start}--new-prompt${SEP}${agent}(?:${SEP}${QUOTED_PATH})?$`, 'u'),
      new RegExp(`${start}--wait${SEP}${RUN_ID}$`, 'u'),
      new RegExp(`${start}--header-of${SEP}${QUOTED_PATH}$`, 'u'),
    ],
  };
}

// 転送の入力も依頼文のファイルに限る。任意のファイル(認証情報など)を依頼文として Codex へ送らせないためである。
function isAllowedBash(agent, command) {
  if (typeof command !== 'string') return false;
  const trimmed = command.replace(/^\s+|\s+$/g, '');
  const patterns = bashPatterns(agent);
  const forward = patterns.forward.exec(trimmed);
  if (forward) return new RegExp(`^${QUOTED_PATH}$`, 'u').test(`"${forward[1]}"`) && isPromptFilePath(agent, forward[1]);
  return patterns.others.some((re) => re.test(trimmed));
}

function isAllowedWrite(agent, filePath) {
  return isPromptFilePath(agent, filePath);
}

function isPromptFilePath(agent, filePath) {
  if (typeof filePath !== 'string' || filePath.includes('\0')) return false;
  const segments = filePath.replace(/\\/g, '/').split('/');
  const fileName = segments[segments.length - 1];
  const parentName = segments.length >= 2 ? segments[segments.length - 2] : '';
  const grandparentPath = segments.slice(0, -2).join('/');
  const grandparentName = segments.length >= 3 ? segments[segments.length - 3] : '';
  const directoryPattern = new RegExp(`^codex-agent-${agent}-\\d{8}-\\d{6}-[A-Za-z0-9]{6}$`);
  if (fileName !== 'prompt.md' || !directoryPattern.test(parentName)) return false;
  return grandparentName === 'scratchpad' || grandparentPath.endsWith(PROMPTS_DIR_SUFFIX);
}

// 許すなら null、拒否するなら理由の文を返す。
function evaluate(agent, rawInput) {
  if (!WRAPPER_AGENTS.includes(agent)) {
    return rejection(null, `フックの定義名の引数(${agent === undefined ? 'なし' : agent})がラッパー役の定義名ではない`);
  }
  let input;
  try {
    input = JSON.parse(rawInput);
  } catch {
    return rejection(agent, 'フックの入力が JSON として読めない');
  }
  if (!input || typeof input !== 'object' || typeof input.tool_name !== 'string'
      || !input.tool_input || typeof input.tool_input !== 'object') {
    return rejection(agent, 'フックの入力に tool_name か tool_input が無い');
  }
  if (input.tool_name === 'Bash') {
    return isAllowedBash(agent, input.tool_input.command) ? null : rejection(agent, 'Bash のコマンドが許す形に一致しない');
  }
  if (input.tool_name === 'Write') {
    return isAllowedWrite(agent, input.tool_input.file_path) ? null : rejection(agent, 'Write のパスが依頼文の置き場と名前に一致しない');
  }
  return rejection(agent, `${input.tool_name} ツールは使えない`);
}

function main() {
  let raw;
  try {
    raw = fs.readFileSync(0, 'utf8');
  } catch {
    raw = '';
  }
  const reason = evaluate(process.argv[2], raw);
  if (reason === null) process.exit(0);
  // Windows のパイプでは process.stderr.write が非同期で、直後の exit で理由が途切れるため同期で書く。
  fs.writeSync(2, `${reason}\n`);
  process.exit(2);
}

main();
