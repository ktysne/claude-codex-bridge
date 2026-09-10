# claude-codex-bridge

Claude Code から Codex CLI を、用途別のサブエージェントとして呼び出すための定義と手順をまとめたリポジトリである。
1 アカウント運用と、用途別に ChatGPT アカウントを分ける 2 アカウント運用の両方に対応する。
アカウントを分ける場合は、`CODEX_HOME` を分離して認証を切り替える。
分けるときは、通常利用とレビューに使うアカウントを既定ホーム `~/.codex` に置き、サブエージェント専用のアカウントに `~/.codex-subagent` を与える。
ここでいう**通常利用**は Codex CLI の対話、VS Code や Chrome の Codex 拡張、Claude Code の Codex プラグインを指し、**サブエージェント**は GPT 側へ実装を委譲する `impl-hard`、`impl-light`、`impl-standard`、`codex-subagent` の 4 定義を指す。
`codex-review` も Claude Code からはサブエージェントとして起動されるが、役割はレビューなので既定ホーム側のアカウントを使う。
通常利用の入口は既定ホームしか見ないため、そこを空けると未ログイン扱いになるためである。

用途に応じて、次の 3 パターンから選ぶ。

- **パターン 1**：1 アカウントで `impl-hard`、`impl-light`、`impl-standard` を使う。
- **パターン 2**：1 アカウントで実装用とレビュー用の 5 定義を使う。
- **パターン 3**：通常利用とレビューに使うアカウントと、サブエージェント専用のアカウントの 2 つを使い、役割ごとに `CODEX_HOME` を固定する。前者が既定ホーム `~/.codex` を使う。

## 構成

```text
Claude Code(メインセッション)
├─ パターン 1(1 アカウント、実装用だけ)
│   ├─ impl-hard        .claude/agents/impl-hard.md(既定は codex_model 未設定で Claude だけが実装)
│   ├─ impl-light       .claude/agents/impl-light.md
│   └─ impl-standard    .claude/agents/impl-standard.md
│       └─ CODEX_HOME=~/.codex
├─ パターン 2(1 アカウント、実装用とレビュー用)
│   ├─ impl-hard / impl-light / impl-standard
│   └─ codex-review / codex-subagent
│       └─ CODEX_HOME=~/.codex
└─ パターン 3(2 アカウント、役割別。通常利用とレビューを既定ホームに置く)
    ├─ codex-review
    │   └─ CODEX_HOME=~/.codex
    └─ impl-hard / impl-light / impl-standard / codex-subagent
        └─ CODEX_HOME=~/.codex-subagent
```

各定義は Claude 側の `.claude/agents/<name>.md` から `~/.claude/tools/codex-agent.sh` を呼び出し、スクリプトが `.claude/gpt-agents/<name>.md` を読んで `codex exec` を組み立てる。
`impl-light` と `impl-standard` は既定で GPT 側に実装を委ね、GPT 側がレートリミットで使えないときだけ自身の Claude モデルで実装する。
`impl-hard` も同じ手順を持つが、出荷時の GPT 側定義には `codex_model` を書いていないため、既定では Claude 側のモデルが実装する。GPT 側に委ねたい場合は `.claude/gpt-agents/impl-hard.md` に `codex_model` を設定する。
`codex-review` と `codex-subagent` は非 0 終了時にフォールバックせず、終了コードと出力末尾を返して停止する。
Codex 側のモデル、effort、認証ホームは `.claude/gpt-agents/` の定義に集約する([docs/gpt-agents.md](docs/gpt-agents.md))。

Codex CLI は認証情報を `$CODEX_HOME/auth.json` に保存し、他の場所を参照しない。
そのため `CODEX_HOME` を分けるだけで、アカウントごとの認証、設定、セッションログが完全に分離される。

## ファイル

| パス | 役割 |
|---|---|
| `.claude/agents/codex-review.md` | レビュー用サブエージェントの定義。`~/.claude/tools/codex-agent.sh` への転送を持つ |
| `.claude/agents/codex-subagent.md` | 実装補助用サブエージェントの定義。`~/.claude/tools/codex-agent.sh` への転送を持つ |
| `.claude/agents/impl-hard.md` | 高難度実装用サブエージェントの Claude 側定義。GPT 側への委譲とフォールバックの手順を持つ。既定は `codex_model` 未設定で、Claude 側のモデルが実装する |
| `.claude/agents/impl-light.md` | 小規模実装用サブエージェントの Claude 側定義。GPT 側への委譲とフォールバックの手順を持つ |
| `.claude/agents/impl-standard.md` | 一般実装用サブエージェントの Claude 側定義。同じくフォールバックの手順を持つ |
| `.claude/gpt-agents/codex-review.md` | レビュー用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/codex-subagent.md` | 実装補助用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/impl-hard.md` | 高難度実装用 GPT 側定義。出荷時は `codex_model` を書かず GPT 側へ委譲しない。設定すれば他の定義と同じく Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/impl-light.md` | 小規模実装用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/impl-standard.md` | 一般実装用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `tools/codex-agent.sh` | GPT 側の定義を読んで `codex exec` を組み立てるスクリプト |
| `gui/` | 定義ファイルを GUI から書き換える設定コンソール(Windows、.NET Framework 4.8)の一式 |
| `gui/build.bat` | 設定コンソールをビルドし、`gui/dist/CodexBridgeConsole.exe` を作る。ダブルクリックで実行できる |
| `gui/start.bat` | 設定コンソールを起動する。exe が無ければ先にビルドする |
| `docs/gpt-agents.md` | GPT 系サブエージェントの構成と、フォールバックの条件 |
| `docs/setup.md` | アカウントのログインからサブエージェント有効化までの手順 |
| `docs/gui.md` | 設定コンソールの使い方 |
| `docs/gui-console-design.md` | 定義ファイルを GUI から書き換える設定コンソール(Windows)の設計。実装済み |
| `docs/cross-review.md`、`.cross-review.md` | ai-cross-review の手順と、このプロジェクト固有のレビュー観点 |

## 使い始めるまで

[docs/setup.md](docs/setup.md) の手順に従う。
選んだパターンに必要な Claude 側定義、GPT 側定義、`tools/codex-agent.sh` を配置する。
利用先の `CLAUDE.md` に、難易度で `impl-hard`、`impl-standard`、`impl-light` を選ぶ役割分担の節を追加する。
Claude Code を再起動し、配置した定義だけを `Agent` ツールの `subagent_type` に指定して呼ぶ。
定義のモデル、effort、GPT 経路の有効状態、サブエージェントの認証ホームを GUI から変えるなら、`gui\build.bat` で設定コンソールを作る([docs/gui.md](docs/gui.md))。

## 守るべき前提

複数アカウントは役割固定で使う。
利用上限の回避を目的にアカウントを切り替える構成は、OpenAI の利用規約が禁じる rate limit の回避と解釈される余地があるため導入しない。
詳細は [CLAUDE.md](CLAUDE.md) の設計原則を参照。
