# claude-codex-bridge の使い方

導入の手順は [setup.md](setup.md) にある。この文書は、導入した後の委譲の流れと、ほかの入口との使い分けを説明する。
定義ファイルの仕組み、終了コード、既知の制約の正本は [gpt-agents.md](gpt-agents.md) にある。

## 構成

```text
Claude Code(メインセッション)
├─ パターン 1(1 アカウント、実装用だけ)
│   ├─ impl-hard        .claude/agents/impl-hard.md
│   ├─ impl-light       .claude/agents/impl-light.md
│   ├─ impl-standard    .claude/agents/impl-standard.md
│   │   └─ CODEX_HOME=~/.codex
│   └─ impl-hard-claude / impl-light-claude / impl-standard-claude(Codex を呼ばない)
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
Codex CLI は認証情報を `$CODEX_HOME/auth.json` に保存し、他の場所を参照しない。
そのため `CODEX_HOME` を分けるだけで、アカウントごとの認証、設定、セッションログが完全に分離される。

## 実装を委譲する

メインセッションは、[setup.md](setup.md) の共通手順 5 で `CLAUDE.md` に書いた役割分担に従い、難易度で `impl-hard`、`impl-standard`、`impl-light` を選んで `Agent` ツールで委譲する。

1. 窓口は依頼文を Codex へ転送する。自分では調べず、実装せず、検証しない。
2. Codex が終わると、窓口は 1 行目に「GPT 側(Codex)で実行した」と書き、`codex-agent:` で始まる行(監査行、`run=`、`result=` など)だけを返す。
3. メインセッションは、`run=` の行の実行 ID で `bash ~/.claude/tools/codex-agent.sh --wait <実行 ID>` を前面で実行し、その標準出力を Codex の最終報告として読む。
4. メインセッションは差分を読み、検証コマンドを実行し直して監査してから、コミットや push を行う。実装担当はコミット、push、PR への投稿を行わない。

Codex が使えないとき(未導入、無効化、利用上限など)は、窓口が `再委譲: impl-*-claude` を報告する。
メインセッションは作業ツリーの変更の有無を確かめ、同じ依頼文の最初の行に `委譲: Claude 側で実装`、次の行に `理由: 再委譲:` と窓口が報告した行を置いて、報告に書かれた `impl-*-claude` へ委譲し直す。
報告の受け取り方、再委譲、「進行中」と書いた報告の扱いは、[CLAUDE.md](../CLAUDE.md) の「委譲の検証」と、[gpt-agents.md](gpt-agents.md) の「再委譲の流れ」「既知の制約」にある。

サブエージェントは背景で起動する(`run_in_background: false` を指定しない)。前面で起動すると、ターンを終えた時点で Codex の実行の追跡が切れるためである。

## レビューと調査を依頼する

パターン 2 と 3 では、`codex-review` に単発のレビューを、`codex-subagent` に調査や実装補助を依頼できる。
2 定義は難易度で選ぶ定義ではないので、役割分担の表には載せない。メインセッションが明示的に指定する。
どちらも Codex が非 0 で終わったときはフォールバックせず、終了コードと出力の末尾を返して止まる。

## Codex を呼ぶ 3 つの系統

Claude Code から Codex を呼ぶ入口は、このリポジトリのほかに 2 つある。
どれもレビューと呼べる機能を持つため、用途を決めておかないと毎回選び直すことになる。

| 系統 | 役割 | 入口 | 使う認証ホーム |
|---|---|---|---|
| claude-codex-bridge(このリポジトリ) | `CODEX_HOME` とサンドボックスを定義ファイルで固定して `codex exec` を起動する層。実装の委譲と、レビュー依頼の転送を担う | `Agent` ツールの `impl-hard`、`impl-light`、`impl-standard`、`codex-review`、`codex-subagent`。または `bash ~/.claude/tools/codex-agent.sh <定義名>` | `.claude/gpt-agents/<定義名>.md` の `codex_home` |
| ai-cross-review | 差分を取り出してレビュアーへ渡し、指摘と対応の往復を PR に記録する CLI | `npm run review:codex`、`npm run review:claude`、`node tools/cross-review.js subagent` | bridge 経由で起動できたときは bridge と同じ。直接起動へ戻ったときは、環境の `CODEX_HOME` (未設定なら既定ホーム) |
| 公式プラグイン `codex@openai-codex` | Codex を救援役として呼ぶ。セッション共有の broker 経由で `codex app-server` を起動する | `/codex:rescue`、`Agent` ツールの `codex:codex-rescue`、`/codex:review`、Stop フックのレビューゲート | 既定ホーム `~/.codex`。broker の起動時に固定される |

用途は次のように割り当てる。

- **差分のレビュー**:ai-cross-review を使う。指摘、対応、妥当性確認の往復が PR に残る。Codex 側は bridge 経由で起動できたときに限り、認証ホームとサンドボックスが定義ファイルで固定される。経由できる条件は [cross-review.md](cross-review.md) の「codex の起動は bridge（codex-agent.sh）を経由する」にあり、実際にどちらで動いたかは実行時の通知と `.cross-review/` に残るメタ情報の `via` でわかる。
- **実装の委譲**:bridge の `impl-hard`、`impl-light`、`impl-standard` を使う。難易度で選ぶ規則は [setup.md](setup.md) の共通手順 5 にある。`impl-*-claude` は、窓口が再委譲を報告したときだけ使う。
- **単発のレビュー依頼と調査**:bridge の `codex-review` と `codex-subagent` を使う。ai-cross-review が Codex を起動するときも同じ 2 定義を使い、`--fix` 無しなら `codex-review`、`--fix` 付きなら `codex-subagent` を選ぶ。
- **救援**:公式プラグインを使う。行き詰まった実装の引き取りや、別実装での診断は bridge に無い。

公式プラグインを救援に限るのは、製品の制限ではなくこのリポジトリの運用方針である。
プラグインは broker プロセスの環境変数を起動時に固定するため、呼び出しごとに `CODEX_HOME` を切り替えられない。
用途別にアカウントを分ける運用は bridge の定義ファイルで行い、プラグインは既定ホームのアカウントで使う。

### Stop レビューゲートの扱い

公式プラグインは、セッションの停止時に Codex のレビューを挟む Stop フックを持つ。
このフックはワークスペースごとの設定 `stopReviewGate` が真のときだけレビューを起動し、偽なら何もせずに戻る。
既定は偽であり、プラグインを入れただけでは停止のたびにレビューが走ることはない。

現在の状態は `/codex:setup` の出力にある `review gate:` の行でわかる。
ゲートを使わない運用にするなら、そのワークスペースで次を実行して `disabled` を確認する。

```text
/codex:setup --disable-review-gate
```

ゲートの既定値と設定の持ち方は、プラグイン 1.0.6 で確認した。

## 設定コンソール

定義のモデル、effort、GPT 経路の有効状態、サブエージェントの認証ホームを GUI から変えるときは、設定コンソール(Windows)を使う。`codex-review` と `codex-subagent` の GPT 側モデルと effort も変更できる。
導入は [setup.md](setup.md) の共通手順 7、使い方は [gui.md](gui.md) にある。

## 連携したときの動き

### ai-cross-review

ai-cross-review は `~/.claude/tools/codex-agent.sh` を見つけると、レビューを `codex-review`、`--fix` を `codex-subagent` の定義で起動する。
メインセッションが `npm run review:codex` を実行するので、サブエージェントを経由しない。設定と確認は [setup.md](setup.md) の「連携: + ai-cross-review」にある。

### agent-cockpit

agent-cockpit のダッシュボードは、サブエージェントの行に Codex のモデルと経路を表示する。
「経路の設定」で Claude を選ぶと、次に起動する窓口は Codex を呼べずに `impl-*-claude` への再委譲を報告する。Codex を選ぶと、窓口に Codex で行う指定が伝えられる。
経路ごとの動きと確認は [setup.md](setup.md) の「連携: + agent-cockpit」にある。
