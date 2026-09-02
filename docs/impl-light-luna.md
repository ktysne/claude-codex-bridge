# impl-light を GPT-5.6 Luna で動かす

Claude Code のサブエージェント `impl-light` の実体を、Claude の Sonnet 5 から Codex CLI 上の GPT-5.6 Luna に置き換える。
このフェーズでは既定の認証ホーム(`~/.codex`)の 1 アカウントだけを使う。
用途別アカウントへの分離は次の段階で行う。

## 構成

```text
Claude Code(メインセッション)
└─ impl-light(ラッパー、Claude haiku)    .claude/agents/impl-light.md
    └─ tools/codex-agent.sh              定義ファイルを読んでコマンドを組み立てる
        └─ codex exec                    CODEX_HOME=~/.codex、gpt-5.6-luna
```

設定源は `.claude/agents/impl-light.md` の 1 ファイルだけである。
モデル、effort、認証ホーム、サンドボックスはすべてこのフロントマターに書き、スクリプトはそれを読んで `codex exec` の引数に展開する。
ラッパー側の Claude は依頼文を標準入力へ流し、出力をそのまま返すだけで、判断を持たない。

依頼文はヒアドキュメントで標準入力に渡す。
コマンドライン引数に埋め込むと、依頼文に含まれる引用符やバックスラッシュで壊れるためである。

Codex に渡すプロンプトは、定義ファイルの `## Codex への指示` 節の本文を先頭に置き、区切り線を挟んで `## 依頼` として依頼文を続けた形になる。
役割文(進め方、禁止事項、報告の形式)は、この節を書き換えるだけで変えられる。

## フロントマターのキー

- **codex_home**：`CODEX_HOME` に渡すディレクトリ。必須。`~`、`$USERPROFILE`、`%USERPROFILE%` を実パスへ展開する。存在しなければ実行せずに終了コード 2 で止まる。
- **codex_model**：`codex exec -m` に渡すモデル名。必須。
- **codex_reasoning_effort**：`-c model_reasoning_effort=` に渡す値。省略時は `medium`。
- **codex_sandbox**：`--sandbox` に渡す値。省略時は `workspace-write`。`read-only` と `workspace-write` のみを受け付け、それ以外は終了コード 2 で止まる。
- **model**：ラッパー側の Claude が使うモデル。Bash を 1 回呼ぶだけなので `haiku` にしている。
- **tools**：ラッパーに許すツール。`Bash` だけにして、ラッパーが自分で調査を始めないようにしている。

`--dangerously-bypass-approvals-and-sandbox` はスクリプトに存在せず、フロントマターからも指定できない。

## アカウントを切り替える

`codex_home` の 1 行を書き換える。

```yaml
codex_home: ~/.codex-subagent
```

切り替え先の `CODEX_HOME` でのログインは [setup.md](setup.md) の手順に従う。
ログインを済ませないままだと、スクリプトが「codex_home が存在しない」として終了コード 2 で止まる。

エージェント定義はセッション開始時に読み込まれるため、書き換えたあとは Claude Code を再起動する。

## effort を変える

常用の値を変えるなら、定義ファイルの `codex_reasoning_effort` を書き換える。
1 回の依頼だけ変えるなら、`--effort` で上書きする。

```bash
bash tools/codex-agent.sh impl-light --effort low <<'EOF'
Reply with exactly: PONG-LUNA
EOF
```

`--effort` は定義ファイルの値より優先される。

## 既知の制約

**同じ作業ツリーに書き込む。**
`impl-light` は Claude Code と同じ worktree で動く。
小規模な変更しか任せない前提で、実行中はメインセッションが同じファイルを編集しない運用にしている。
書き込み範囲の大きい依頼は `codex-subagent` に回し、worktree を分ける。

**既定ホームのフック出力が混じる。**
既定ホーム `~/.codex` には Codex プラグインと ai-cross-review の設定が入っており、実行のたびにフックの出力が標準エラーへ流れる。
スクリプトは `WARNING` と `hook:` で始まる行を除いてから返す。
`codex_home` を専用ホームに移せば、この除去は不要になる。

**プロジェクト定義がユーザ定義を上書きする。**
`.claude/agents/impl-light.md` はこのリポジトリでのみ効く。
全プロジェクトに適用するなら、次の 2 つを置く。

1. この定義ファイルを `%USERPROFILE%\.claude\agents\impl-light.md` に置き換える。
2. `tools/codex-agent.sh` を `%USERPROFILE%\.claude\tools\` にコピーする。

ラッパーはカレントディレクトリの `tools/codex-agent.sh` を先に探し、無ければ `%USERPROFILE%\.claude\tools\codex-agent.sh` を使う。

## 動作確認

```bash
bash tools/codex-agent.sh impl-light <<< "Reply with exactly: PONG-LUNA"
```

先頭に監査用の 1 行が出る。

```text
codex-agent: agent=impl-light model=gpt-5.6-luna effort=medium sandbox=workspace-write codex_home=... workdir=...
```

続く Codex のヘッダで `model: gpt-5.6-luna` と `reasoning effort` を確認し、最後に `PONG-LUNA` が返れば期待どおりである。
