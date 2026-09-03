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

- 用途固定の原則: アカウント A はレビュー専用、アカウント B は実装補助専用とする。利用上限の回避を目的にアカウントを切り替える構成(枠が尽きたら別アカウントへ回す、など)は導入しない。OpenAI の利用規約が禁じる rate limit の回避と解釈される余地があるためである。現在は 1 アカウント段階であり、既定ホーム(`~/.codex`)を対話と 4 定義(`impl-light`、`impl-standard`、`codex-review`、`codex-subagent`)のすべてに使う。用途別アカウントに分けた時点で、`codex-review` は `~/.codex-review` へ、他の 3 定義は `~/.codex-subagent` へ移す。
- 認証の分離: `codex` を呼ぶときは必ず `CODEX_HOME` を明示する。既定の `~/.codex` に暗黙に依存する呼び出しを書かない。
- 権限の固定: レビュー用は `--sandbox read-only` を外さない。実装補助用でも `--dangerously-bypass-approvals-and-sandbox` は使わない。
- 認証情報の非コミット: `auth.json`、トークン、アカウント ID をリポジトリに入れない。ドキュメントの例には実値を書かない。
- 作業ツリーの分離: 書き込み可能な Codex 呼び出しは、Claude Code と別の worktree で行う。例外として、`impl-light` と `impl-standard` が委譲する GPT 側の実行は、メインセッションが同じファイルを同時に編集しない前提で同一 worktree に書く。

## CLAUDE.md と AGENTS.md の同期

`CLAUDE.md` と `AGENTS.md` は同一内容を保つ。どちらか一方を変更した場合は、必ずもう一方にも同じ変更を反映すること。片方だけを更新した状態でコミットしてはならない。

## 検証コマンド

```bash
codex --version
CODEX_HOME="$USERPROFILE/.codex" codex login status  # 現段階は 4 定義とも既定ホームを使う
bash tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-LUNA"  # 実モデルを起動し利用枠を消費する
bash tools/codex-agent.sh codex-review --effort low <<< "Reply with exactly: PONG-REVIEW"  # 同上
```

エージェント定義(`.claude/agents/*.md`)を変更した場合、Claude Code のセッションを再起動しないと反映されない。

## 相互レビュー

実装後は ai-cross-review で Claude と Codex の相互レビューを回す。手順は [docs/cross-review.md](docs/cross-review.md)、観点は `.cross-review.md` を参照。

```bash
npm run review:codex -- --uncommitted
npm run review:claude -- --uncommitted
```

## リモートセッション時の作業について

### モデル役割分担（メインセッションとサブエージェント）
メインセッションは設計・監査・レビューに専念し、実装は下位モデルのサブエージェント（Agent ツール）に切り出すことを基本とする。ただし、実装難易度が特に高い箇所はメインセッションが直接実装してよい。
- メインセッションが Fable の場合: 実装はOpus / Sonnetのサブエージェントに適切に切り出して実行する。
- メインセッションが Opus の場合: 実装は Sonnet のサブエージェントに切り出して実行する。

### AIクロスレビュー（ai-cross-review）
基本フローは **実装 → レビュー → 指摘対応 → 妥当性確認** を Claude / Codex を入れ替えて回し、受け渡しは **git 差分 / PR**（チャットログを手コピーしない）。**指摘・対応・妥当性確認は PR コメントに残す**。実行手順 (CLI / リモートコントロール時のサブエージェント経路・各フラグ) は **[cross-review スキル](.claude/skills/cross-review/SKILL.md)** に集約。

毎回守る必須ルール:
- **実装完了後の起点（Claude 主導・必須）**: Claude が改修を一区切りしたら、作業を完了扱いにする前に必ず **A. Codex にレビュー依頼 / B. レビュー + 修正依頼 / C. 何もしない** の 3 択を `AskUserQuestion` で提示する。「コミットして終わり」「PR を作って終わり」と勝手に締めない。反復改修でも論理的な区切りごとに確認する。省略してよい軽微な例外（誤字・ドキュメント文言調整・整形のみ等。省略時は一言添える）はスキル参照。**規模・影響で迷ったら省略せず確認する**。
- **サーキットブレーカー（無限ループ防止・必須）**: レビュー ↔ 指摘対応は **最大 3 往復**（1 往復 = 実装 or 指摘対応 → レビュー → Claude が結果確認。カウント対象は blocker / 要修正）。超過 or 同一指摘の揺り戻しを検知したら中断し、サマリを `AskUserQuestion` で提示する。
