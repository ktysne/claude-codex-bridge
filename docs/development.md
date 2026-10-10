# claude-codex-bridge の開発

claude-codex-bridge 自体を開発するときの入口である。利用者向けの導入は [setup.md](setup.md)、委譲の流れと使い分けは [usage.md](usage.md) にある。

## 最初に読むもの

| 文書 | 内容 |
|---|---|
| [CLAUDE.md](../CLAUDE.md) / [AGENTS.md](../AGENTS.md) | 守るべき設計原則(用途固定、認証ホームの配置、認証の分離、権限の固定、認証情報の非コミット、委譲の検証、実装担当の操作範囲、作業ツリーの分離)。2 つは同じ内容に保つ |
| [docs/gpt-agents.md](gpt-agents.md) | GPT 系サブエージェントの構成、フック、終了コード、再委譲の流れ、既知の制約 |
| [docs/gpt-agent-log-review-2026-09-16.md](gpt-agent-log-review-2026-09-16.md) | セッション記録から測った運用の状態と、そこから直した内容。次に測るときの基準値と測り直しの規則 |
| [docs/gui-console-design.md](gui-console-design.md) | 設定コンソールの設計 |
| [docs/plans/](plans/) | 改修の計画書 |
| [docs/cross-review.md](cross-review.md) / [.cross-review.md](../.cross-review.md) | AI クロスレビューの手順(ai-cross-review から取り込んだもの)と、このリポジトリのレビュー観点 |

## ファイル

| パス | 役割 |
|---|---|
| `.claude/agents/codex-review.md` | レビュー用サブエージェントの定義。`~/.claude/tools/codex-agent.sh` への転送を持つ |
| `.claude/agents/codex-subagent.md` | 実装補助用サブエージェントの定義。`~/.claude/tools/codex-agent.sh` への転送を持つ |
| `.claude/agents/impl-hard.md` | 高難度実装の窓口。`~/.claude/tools/codex-agent.sh` への転送と、Claude 側へ倒すときの再委譲の報告を持つ |
| `.claude/agents/impl-light.md` | 小規模実装の窓口。転送と再委譲の報告を持つ |
| `.claude/agents/impl-standard.md` | 一般実装の窓口。転送と再委譲の報告を持つ |
| `.claude/agents/impl-hard-claude.md` | 高難度実装を Claude 側で行う定義。委譲を止める指定のある依頼だけを実装する |
| `.claude/agents/impl-light-claude.md` | 小規模実装を Claude 側で行う定義。委譲を止める指定のある依頼だけを実装する |
| `.claude/agents/impl-standard-claude.md` | 一般実装を Claude 側で行う定義。委譲を止める指定のある依頼だけを実装する |
| `.claude/agents/review-claude.md` | Claude 側でレビューを行う任意定義 |
| `.claude/gpt-agents/codex-review.md` | レビュー用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/codex-subagent.md` | 実装補助用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/impl-hard.md` | 高難度実装用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/impl-light.md` | 小規模実装用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `.claude/gpt-agents/impl-standard.md` | 一般実装用 GPT 側定義。Codex のモデル、effort、認証ホーム、サンドボックス、役割文を持つ |
| `tools/codex-agent.sh` | GPT 側の定義を読んで `codex exec` を組み立てるスクリプト |
| `tools/codex-agent-hook.js` | ラッパー役の定義(窓口と `codex-review`、`codex-subagent`)の Bash と Write を、転送の形だけに絞るフック |
| `tools/agent-log-metrics.js` | Claude Code のセッション記録から、GPT 系サブエージェントの運用の指標を数えるスクリプト |
| `tools/test/` | ラッパー、フック、集計スクリプトのテスト |
| `tools/cross-review*.js`、`tools/cross-review.sync.example.json`、`.claude/skills/cross-review/`、`docs/cross-review.md`、`.cross-review.example.md` | ai-cross-review から同期で取り込んだもの。直接編集しない |
| `gui/` | 定義ファイルを GUI から書き換え、`codex-review` と `codex-subagent` の GPT 側モデルと effort も変更できる設定コンソール(Windows、.NET Framework 4.8)の一式 |
| `gui/build.bat` | 設定コンソールをビルドし、`gui/dist/CodexBridgeConsole.exe` を作る。ダブルクリックで実行できる |
| `gui/start.bat` | 設定コンソールを起動する。exe が無ければ先にビルドする |

## 検証コマンド

```bash
bash -n tools/codex-agent.sh
npm run test:codex-agent       # ラッパー(tools/codex-agent.sh)のテスト。偽の codex を使うので利用枠を消費しない。Git Bash から実行する
npm run test:codex-agent-hook  # ラッパー役の定義のフック(tools/codex-agent-hook.js)のテスト
npm run test:metrics           # 集計スクリプト(tools/agent-log-metrics.js)のテスト。Git Bash から実行する
npm test                       # 上の 3 つをまとめて流す
dotnet build gui/CodexBridgeConsole.sln -c Release  # 設定コンソール
dotnet test gui/CodexBridgeConsole.sln -c Release
```

実モデルで確かめるときは、リポジトリ側のスクリプトで起動する。利用枠を消費する。

```bash
bash tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-LUNA"
bash tools/codex-agent.sh codex-review --effort low <<< "Reply with exactly: PONG-REVIEW"
```

## 開発中の反映

サブエージェントが呼ぶスクリプトはユーザ側の固定パス(`~/.claude/tools/`)にある。リポジトリ側の `tools/codex-agent.sh` や `.claude/agents/` を直しても、ユーザ側へ配置し直すまでサブエージェントの動きは変わらない。
配置の順序(スクリプトとフックを先に、定義を後に)と、再起動が要る条件は [gpt-agents.md](gpt-agents.md) の「ラッパー役の定義の道具を絞る」と [setup.md](setup.md) の共通手順 6 にある。

## ai-cross-review の取り込み

相互レビューの基盤は ai-cross-review から同期で取り込む。手順は「同期 → 表示された移行ノートの作業 → 検証コマンド」の順である。

```bash
npm run sync:cross-review        # 上流から取り込む
npm run sync:cross-review:check  # ドリフトの検査
```

## 連携先との約束

連携先のツールが読む値や置き場を変えるときは、連携先の文書とコードも合わせて確かめる。

| 連携先 | bridge 側の約束 |
|---|---|
| [ai-cross-review](https://github.com/ktysne/ai-cross-review) | `~/.claude/tools/codex-agent.sh` を `bash <スクリプト> <定義名> -C <作業ディレクトリ>` の形で、依頼文を標準入力で受けて起動できる。`codex exec` に `-c approval_policy=never` を渡す。定義 `codex-review` は `read-only`、`codex-subagent` は `workspace-write`。終了コード 3 は未導入、75 は GPT 側の事情で使えないことを表す |
| [agent-cockpit](https://github.com/ktysne/agent-cockpit) | `~/.claude/tools/codex-agent.sh` の有無で導入を判断される。`codex-agent: agent=` の監査行、`run=` の行、`result=` の行と、`--wait` の状態の行の形が、ダッシュボードの表示に使われる。経路が Claude のときは、サブエージェントの `codex-agent.sh` の実行が agent-cockpit のフックに拒否される |

## 文書を直すとき

- 導入の手順(配置するファイル、権限規則、役割分担の節、パターンごとの設定と確認、連携ツールごとの確認)を変えたら [setup.md](setup.md) を直す。
- 委譲の流れやほかの入口との使い分けを変えたら [usage.md](usage.md) を、定義、フック、終了コードの仕様を変えたら [gpt-agents.md](gpt-agents.md) を直す。
- 設定コンソールを変えたら [gui.md](gui.md) と [gui-console-design.md](gui-console-design.md) を直す。
- 連携ツールや概要が変わったら [README.md](../README.md) を直す。
- 設計原則を変えたら [CLAUDE.md](../CLAUDE.md) と [AGENTS.md](../AGENTS.md) を同じ内容で直す。
