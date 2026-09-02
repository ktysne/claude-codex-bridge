# claude-codex-bridge

Claude Code から Codex CLI を、用途別のサブエージェントとして呼び出すための定義と手順をまとめたリポジトリである。
レビュー用と実装補助用で ChatGPT Plus アカウントを分け、`CODEX_HOME` を分離して認証を切り替える。

現在は 1 アカウント(既定ホーム `~/.codex`)を使い、4 定義を `tools/codex-agent.sh` 経由で動かす段階にある。
2 アカウント運用は次の段階である。

## 構成

```text
Claude Code(メインセッション)
├─ codex-review     .claude/agents/codex-review.md
│   └─ tools/codex-agent.sh          .claude/gpt-agents/codex-review.md を読む
│       └─ CODEX_HOME=~/.codex       既定ホーム(暫定、gpt-5.6-sol、medium、read-only)
├─ codex-subagent   .claude/agents/codex-subagent.md
│   └─ tools/codex-agent.sh          .claude/gpt-agents/codex-subagent.md を読む
│       └─ CODEX_HOME=~/.codex       既定ホーム(暫定、gpt-5.6-sol、medium、workspace-write)
├─ impl-light       .claude/agents/impl-light.md(Claude、Sonnet 5)
│   └─ tools/codex-agent.sh          .claude/gpt-agents/impl-light.md を読む
│       └─ CODEX_HOME=~/.codex       既定ホーム(暫定、gpt-5.6-luna、effort=xhigh)
└─ impl-standard    .claude/agents/impl-standard.md(Claude、Opus 5)
    └─ tools/codex-agent.sh          .claude/gpt-agents/impl-standard.md を読む
        └─ CODEX_HOME=~/.codex       既定ホーム(暫定、gpt-5.6-luna、effort=max)
```

4 定義とも `tools/codex-agent.sh` が `.claude/gpt-agents/` の定義を読み、`codex exec` を組み立てる。
`impl-light` と `impl-standard` は既定で GPT 側に実装を委ね、GPT 側がレートリミットで使えないときだけ自身の Claude モデルで実装する。
`codex-review` と `codex-subagent` は非 0 終了時にフォールバックせず、終了コードと出力末尾を返して停止する。
Codex 側のモデル、effort、認証ホームは `.claude/gpt-agents/` の定義に集約し、`tools/codex-agent.sh` がそれを読んで `codex exec` を組み立てる([docs/gpt-agents.md](docs/gpt-agents.md))。

Codex CLI は認証情報を `$CODEX_HOME/auth.json` に保存し、他の場所を参照しない。
そのため `CODEX_HOME` を分けるだけで、アカウントごとの認証、設定、セッションログが完全に分離される。
この点は 2026-09-02 に実環境とソースコードで確認した([docs/verification-2026-09-02.md](docs/verification-2026-09-02.md))。

## ファイル

| パス | 役割 |
|---|---|
| `.claude/agents/codex-review.md` | レビュー用サブエージェントの定義。`tools/codex-agent.sh` への転送を持つ |
| `.claude/agents/codex-subagent.md` | 実装補助用サブエージェントの定義。`tools/codex-agent.sh` への転送を持つ |
| `.claude/agents/impl-light.md` | 小規模実装用サブエージェントの Claude 側定義。GPT 側への委譲とフォールバックの手順を持つ |
| `.claude/agents/impl-standard.md` | 一般実装用サブエージェントの Claude 側定義。同じくフォールバックの手順を持つ |
| `.claude/gpt-agents/codex-review.md` | レビュー用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/codex-subagent.md` | 実装補助用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/impl-light.md` | 小規模実装用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/impl-standard.md` | 一般実装用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `tools/codex-agent.sh` | GPT 側の定義を読んで `codex exec` を組み立てるスクリプト |
| `docs/gpt-agents.md` | GPT 系サブエージェントの構成と、フォールバックの条件 |
| `docs/setup.md` | アカウントのログインからサブエージェント有効化までの手順 |
| `docs/verification-2026-09-02.md` | ChatGPT 回答の妥当性検証と、定義、呼び出しの動作確認の記録 |
| `docs/backlog.md` | 残作業と、開発者の判断を要する事項 |
| `docs/chatgpt-answer-2026-09-02.md` | 検討の出発点になった ChatGPT の回答(原文) |
| `docs/cross-review.md`、`.cross-review.md` | ai-cross-review の手順と、このプロジェクト固有のレビュー観点 |

## 使い始めるまで

[docs/setup.md](docs/setup.md) の手順に従う。
現段階では既定ホーム `~/.codex` で `codex login` を済ませればよい。
2 アカウント段階に進むときは、[docs/setup.md](docs/setup.md) の手順 1〜2 で用途別ホームを準備する。
その後、定義を配置して Claude Code を再起動する。

1. このリポジトリの 4 つの Claude 側定義と GPT 側定義を、使いたいプロジェクトへ配置する。
2. `tools/codex-agent.sh` を配置する。
3. Claude Code を再起動し、`Agent` ツールの `subagent_type` に 4 つの定義のいずれかを指定して呼ぶ。

## 守るべき前提

複数アカウントは役割固定で使う。
利用上限の回避を目的にアカウントを切り替える構成は、OpenAI の利用規約が禁じる rate limit の回避と解釈される余地があるため導入しない。
詳細は [CLAUDE.md](CLAUDE.md) の設計原則を参照。
