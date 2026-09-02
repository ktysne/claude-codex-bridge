# プロジェクトガイドライン

このファイルは AI コーディングエージェント(Claude Code など)向けの共通指示を記載する。

## このプロジェクトが扱うもの

Claude Code から Codex CLI を、用途別のサブエージェントとして呼び出す仕組みを整備する。
レビュー用と実装補助用で ChatGPT アカウントを分け、`CODEX_HOME` を分離して認証を切り替える。
全体像は [README.md](README.md)、設計上の制約と検証結果は [docs/verification-2026-09-02.md](docs/verification-2026-09-02.md) を参照。

## 言語

このプロジェクトでは日本語を共通言語とする。具体的には以下をすべて日本語で書くこと。

- セッション中の応答、説明、報告などの出力
- コミットメッセージ
- Pull Request のタイトルと本文
- Issue やレビューコメントなど、リポジトリ上のやり取り

コード中の識別子(変数名、関数名など)は慣例どおり英語でよい。コメントやドキュメントは日本語で書く。

例外: `.bat` など cmd.exe が解釈するスクリプトのコメントと表示メッセージは ASCII(英語)で書く。cmd のバッチパーサは UTF-8 の日本語をコマンドとして誤解釈し、実行自体が失敗するためである。

ユーザが目にする日本語の文書(docs 配下の文書、README など)を書く、または推敲するときは、`/japanese-tech-writing` スキルを使い、その文章規範に従うこと。

## 守るべき設計原則

- 用途固定の原則: アカウント A はレビュー専用、アカウント B は実装補助専用とする。利用上限の回避を目的にアカウントを切り替える構成(枠が尽きたら別アカウントへ回す、など)は導入しない。OpenAI の利用規約が禁じる rate limit の回避と解釈される余地があるためである。
- 認証の分離: `codex` を呼ぶときは必ず `CODEX_HOME` を明示する。既定の `~/.codex` に暗黙に依存する呼び出しを書かない。
- 権限の固定: レビュー用は `--sandbox read-only` を外さない。実装補助用でも `--dangerously-bypass-approvals-and-sandbox` は使わない。
- 認証情報の非コミット: `auth.json`、トークン、アカウント ID をリポジトリに入れない。ドキュメントの例には実値を書かない。
- 作業ツリーの分離: 書き込み可能な Codex 呼び出しは、Claude Code と別の worktree で行う。

## CLAUDE.md と AGENTS.md の同期

`CLAUDE.md` と `AGENTS.md` は同一内容を保つ。どちらか一方を変更した場合は、必ずもう一方にも同じ変更を反映すること。片方だけを更新した状態でコミットしてはならない。

## 検証コマンド

```bash
codex --version
CODEX_HOME="$USERPROFILE/.codex-review" codex login status
CODEX_HOME="$USERPROFILE/.codex-subagent" codex login status
bash tools/codex-agent.sh impl-light <<< "Reply with exactly: PONG-LUNA"
```

エージェント定義(`.claude/agents/*.md`)を変更した場合、Claude Code のセッションを再起動しないと反映されない。

## 相互レビュー

実装後は ai-cross-review で Claude と Codex の相互レビューを回す。手順は [docs/cross-review.md](docs/cross-review.md)、観点は `.cross-review.md` を参照。

```bash
npm run review:codex -- --uncommitted
npm run review:claude -- --uncommitted
```
