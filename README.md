# claude-codex-bridge

Claude Code から Codex CLI を、用途別のサブエージェントとして呼び出すための定義と手順をまとめたリポジトリである。
定義ファイルを `~/.claude/` に配置して使う。常駐するプロセスは無い。

## 概要

Claude Code のメインセッションが設計と監査を受け持ち、実装やレビューを Codex に任せる構成を作る。

- **実装の委譲**:`impl-hard`、`impl-standard`、`impl-light` の 3 定義を、難易度で選んで委譲する。3 定義は依頼を Codex へ転送する窓口で、Codex が使えないときは Claude 側の実装用の定義(`impl-*-claude`)への再委譲を報告する。
- **レビューと調査**:`codex-review`(read-only)と `codex-subagent`(workspace-write)に、単発のレビューや調査を依頼する。
- **Claude のレビュー**:`review-claude` は Claude だけで動く読み取り専用の担当であり、Codex の実装に対する別ベンダーのレビューや、Claude の実装に対する客観レビューに使う。任意で配置でき、設定コンソールからモデルと effort を変更できる。
- **設定の一元化**:Codex のモデル、effort、認証ホーム(`CODEX_HOME`)、サンドボックスを `.claude/gpt-agents/` の定義ファイルで固定する。起動スクリプト `codex-agent.sh` がこの定義を読んで `codex exec` を組み立てる。
- **アカウントの分離**:1 アカウントでも使える。2 アカウントで使うときは、通常利用とレビューに使うアカウントを既定ホーム `~/.codex` に、サブエージェント専用のアカウントを `~/.codex-subagent` に置き、役割ごとに認証を固定する。
- **設定コンソール**:定義のモデル、effort、GPT 経路の有効状態、認証ホームを GUI から変える Windows のアプリ(任意)。

ここでいう**通常利用**は Codex CLI の対話、VS Code や Chrome の Codex 拡張、Claude Code の Codex プラグインを指し、**サブエージェント**は GPT 側へ実装を委譲する `impl-hard`、`impl-light`、`impl-standard`、`codex-subagent` の 4 定義を指す。
複数アカウントは役割固定で使い、利用上限の回避を目的にアカウントを切り替える構成は導入しない。OpenAI の利用規約が禁じる rate limit の回避と解釈される余地があるためである。

委譲の流れ、公式プラグインとの使い分け、設定コンソールは [docs/usage.md](docs/usage.md) にある。

## 連携できるツール

claude-codex-bridge は単独で使える。次のツールを導入済みなら、使える機能が増える。導入していなくても、委譲は止まらない。

| ツール | 概要 | 連携するとできること |
|---|---|---|
| [ai-cross-review](https://github.com/ktysne/ai-cross-review) | Claude と Codex に、git の差分で交互にコードレビューさせる CLI | `npm run review:codex` が、bridge の `codex-review`(`--fix` 付きなら `codex-subagent`)の定義で Codex を起動する。差分のレビューでも、モデル、effort、認証ホームが bridge の定義にそろう。 |
| [agent-cockpit](https://github.com/ktysne/agent-cockpit) | Claude Code のセッションの状態を表示するローカルのダッシュボード | サブエージェントの行に、経路と Codex のモデルが表示される。ダッシュボードの「経路の設定」で、次に起動するサブエージェントを Codex と Claude のどちらで動かすかを切り替えられる。 |

## 導入

導入は AI(Claude Code など)に任せる前提で、手順を [docs/setup.md](docs/setup.md) にまとめている。
AI には、次のように依頼する。

> claude-codex-bridge を導入して。手順は https://raw.githubusercontent.com/ktysne/claude-codex-bridge/main/docs/setup.md にある。

手順書は、アカウントの使い方で選ぶパターン 1〜3 と、連携ツールごとの設定の手順に分かれている。
連携ツールそのものの導入は、それぞれのドキュメントに従う。
`~/.claude/settings.json`、`~/.claude/CLAUDE.md`、`~/.claude/agents/` など、リポジトリの外のファイルを変えるときは、AI が変更内容を示して確認を求める。

## 導入できたかの確認

パターンごとに、次の点を確かめる。具体的なコマンドと期待する結果は、[docs/setup.md](docs/setup.md) の各パターンの「確認」にある。

| パターン | 確かめること |
|---|---|
| 共通 | `codex --version` が通る。`codex login status` がログイン済みを示す。`~/.claude/tools/` に `codex-agent.sh` と `codex-agent-hook.js` がある。 |
| パターン 1 | `bash ~/.claude/tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-LUNA"` が `codex-agent: result=ok` で終わる。Claude Code の `Agent` ツールで `impl-light` を指定すると、1 行目に「GPT 側(Codex)で実行した」と書いた報告が返る。 |
| パターン 2 | パターン 1 に加えて、`codex-review` の監査行に `sandbox=read-only`、`codex-subagent` の監査行に `sandbox=workspace-write` が出る。 |
| パターン 3 | パターン 2 に加えて、`codex-review` の監査行に `codex_home=.../.codex`、ほかの 4 定義の監査行に `codex_home=.../.codex-subagent` が出る。 |
| + ai-cross-review | ai-cross-review を入れたリポジトリで `npm run review:codex` を実行すると、出力に「codex-agent.sh 経由 (定義: codex-review)」と出る。 |
| + agent-cockpit | ダッシュボードの「経路の設定」にサブエージェントの段が出る。経路を選んでから起動したサブエージェントの行に、動いている間は経路の札と Codex のモデルが出る。 |

## 開発者向けドキュメント

claude-codex-bridge 自体を開発するときは、[docs/development.md](docs/development.md) から読み始める。ファイルの構成、検証コマンド、設計の文書の場所をまとめている。
