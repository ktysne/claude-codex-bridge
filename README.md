# claude-codex-bridge

Claude Code から Codex CLI を、用途別のサブエージェントとして呼び出すための定義と手順をまとめたリポジトリである。
レビュー用と実装補助用で ChatGPT Plus アカウントを分け、`CODEX_HOME` を分離して認証を切り替える。

現在は 1 アカウント(既定ホーム `~/.codex`)で `impl-light` を GPT-5.6 Luna 化する段階にあり、2 アカウント運用は次の段階である。

## 構成

```text
Claude Code(メインセッション)
├─ codex-review     .claude/agents/codex-review.md
│   └─ CODEX_HOME=~/.codex-review    ChatGPT アカウント A(レビュー専用、read-only)
├─ codex-subagent   .claude/agents/codex-subagent.md
│   └─ CODEX_HOME=~/.codex-subagent  ChatGPT アカウント B(実装補助専用、workspace-write)
└─ impl-light       .claude/agents/impl-light.md
    └─ tools/codex-agent.sh          定義ファイルを読んで codex exec を組み立てる
        └─ CODEX_HOME=~/.codex       既定ホーム(暫定、gpt-5.6-luna、workspace-write)
```

`codex-review` と `codex-subagent` は定義本文にコマンドを直書きする。
`impl-light` はコマンドの組み立てを `tools/codex-agent.sh` に寄せ、モデル、effort、認証ホームを定義ファイルのフロントマターに集約している([docs/impl-light-luna.md](docs/impl-light-luna.md))。

Codex CLI は認証情報を `$CODEX_HOME/auth.json` に保存し、他の場所を参照しない。
そのため `CODEX_HOME` を分けるだけで、アカウントごとの認証、設定、セッションログが完全に分離される。
この点は 2026-09-02 に実環境とソースコードで確認した([docs/verification-2026-09-02.md](docs/verification-2026-09-02.md))。

## ファイル

| パス | 役割 |
|---|---|
| `.claude/agents/codex-review.md` | レビュー用サブエージェントの定義 |
| `.claude/agents/codex-subagent.md` | 実装補助用サブエージェントの定義 |
| `.claude/agents/impl-light.md` | 小規模実装用サブエージェントの定義。Codex 側の設定もここに集約する |
| `tools/codex-agent.sh` | エージェント定義を読んで `codex exec` を組み立てるスクリプト |
| `docs/impl-light-luna.md` | `impl-light` を GPT-5.6 Luna で動かす構成と、切り替え手順 |
| `docs/setup.md` | アカウントのログインからサブエージェント有効化までの手順 |
| `docs/verification-2026-09-02.md` | ChatGPT 回答の妥当性検証と、定義、呼び出しの動作確認の記録 |
| `docs/backlog.md` | 残作業と、開発者の判断を要する事項 |
| `docs/chatgpt-answer-2026-09-02.md` | 検討の出発点になった ChatGPT の回答(原文) |
| `docs/cross-review.md`、`.cross-review.md` | ai-cross-review の手順と、このプロジェクト固有のレビュー観点 |

## 使い始めるまで

[docs/setup.md](docs/setup.md) の手順に従う。
要点は次の三つである。

1. 用途ごとの `CODEX_HOME` でブラウザログインする(アカウント A、B それぞれ)。
2. このリポジトリのエージェント定義を、使いたいプロジェクトの `.claude/agents/` にコピーする。
3. Claude Code を再起動し、`Agent` ツールの `subagent_type` に `codex-review` または `codex-subagent` を指定して呼ぶ。

## 守るべき前提

複数アカウントは役割固定で使う。
利用上限の回避を目的にアカウントを切り替える構成は、OpenAI の利用規約が禁じる rate limit の回避と解釈される余地があるため導入しない。
詳細は [CLAUDE.md](CLAUDE.md) の設計原則を参照。
