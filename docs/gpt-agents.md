# 実装サブエージェントを GPT 側で動かす

Claude Code のサブエージェント `impl-light` と `impl-standard` は、既定で Codex CLI 上の GPT-5.6 Luna に実装を委ねる。
GPT 側がレートリミットで使えないときだけ、サブエージェント自身が Claude として実装する。

## 目的

この段階では既定の認証ホーム(`~/.codex`)の 1 アカウントだけを使う。
既定ホームは対話と実装補助の両方を兼ねており、用途別アカウントへの分離は次の段階で行う。

委譲の対象は `impl-light` と `impl-standard` の 2 つに限る。
`impl-hard` は GPT 側へ委譲せず、Claude(Opus 5 / high)が担う。
設計判断を伴う変更や、正しさの検証が難しい変更は、メインセッションと同じ Claude 系に留めたほうが、監査で挙動の食い違いを追いやすいためである。

## 構成

```text
Claude Code(メインセッション)
└─ impl-light(Claude、Sonnet 5)      .claude/agents/impl-light.md
    └─ tools/codex-agent.sh           定義ファイルを読んでコマンドを組み立てる
        └─ codex exec                 CODEX_HOME=~/.codex、gpt-5.6-luna
```

`impl-standard` も同じ形で、Claude 側が Opus 5、GPT 側の effort が `max` である点だけが異なる。

サブエージェントは依頼を受けると、まず `tools/codex-agent.sh` を 1 回呼び、依頼文をヒアドキュメントで標準入力に流す。
コマンドライン引数に埋め込むと、依頼文に含まれる引用符やバックスラッシュで壊れるためである。
スクリプトが成功すれば、その出力をそのまま返す。
レートリミットで失敗したときは、サブエージェント自身が Claude として実装する。

## 定義ファイルの二層

定義ファイルは 2 つのディレクトリに分かれる。

- **`.claude/agents/<name>.md`**：Claude Code が読むサブエージェント定義。Claude 側のモデル、effort、フォールバック時の役割文を持つ。
- **`.claude/gpt-agents/<name>.md`**：`tools/codex-agent.sh` だけが読む GPT 側の定義。Codex のモデル、effort、認証ホーム、サンドボックス、および Codex へ渡す役割文を持つ。

GPT 側のフロントマターに書くキーは、このスクリプトのために定めた独自のものである。
Claude Code はこれらを解釈しないし、`.claude/gpt-agents/` をサブエージェント定義として読むこともない。
そのため、両者の役割文を別々に書き分けられる。

GPT 側の定義では、フロントマター直後から末尾までが Codex へ渡る役割文になる。
スクリプトはその役割文を先頭に置き、区切り線を挟んで `## 依頼` として依頼文を続けたプロンプトを組み立てる。

定義の探索は、スクリプトを起動したカレントディレクトリを基準にする。
`-C` で別のディレクトリを作業ディレクトリに指定しても、読む定義は切り替わらない。
別のプロジェクトの定義を使いたい場合は、そのディレクトリへ移ってからスクリプトを起動する。

## フロントマターのキー

GPT 側の定義(`.claude/gpt-agents/<name>.md`)で使うキーは次の 4 つである。

- **codex_home**：`CODEX_HOME` に渡すディレクトリ。必須。`~`、`$USERPROFILE`、`%USERPROFILE%` を実パスへ展開する。存在しなければ実行せずに終了コード 2 で止まる。
- **codex_model**：`codex exec -m` に渡すモデル名。必須。
- **codex_reasoning_effort**：`-c model_reasoning_effort=` に渡す値。省略時は `medium`。`low`、`medium`、`high`、`xhigh`、`max` のみを受け付ける。
- **codex_sandbox**：`--sandbox` に渡す値。省略時は `workspace-write`。`read-only` と `workspace-write` のみを受け付ける。

`--dangerously-bypass-approvals-and-sandbox` はスクリプトに存在せず、フロントマターからも指定できない。

Claude 側の定義(`.claude/agents/<name>.md`)のフロントマターは、Claude Code の通常のサブエージェント定義と同じ `name`、`description`、`model`、`effort` である。

## フォールバックの条件と終了コード

サブエージェントはスクリプトの終了コードで動きを決める。

- **0**：Codex が完了した。出力の末尾にある Codex の報告をそのまま返し、Claude 側では実装しない。
- **2**：引数、定義ファイルの内容、環境の不備でスクリプトが起動しなかった。フロントマターのキー不足、effort やサンドボックスの不正値、`codex_home` の不在、作業ディレクトリの不在、端末からの起動、空の依頼文がこれにあたる。実装せず、終了コードと出力の末尾を報告して終わる。
- **3**：GPT 側が未導入である。`codex` コマンドが PATH に無いか、`.claude/gpt-agents/<name>.md` が見つからない。サブエージェント自身が Claude として実装し、その旨を報告の冒頭に書く。
- **75**：Codex がレートリミットで実行できなかった。サブエージェント自身が Claude として実装し、フォールバックした旨を報告の冒頭に書く。
- **その他**：Codex の終了コードをそのまま返している。実装せず、同じく終了コードと出力の末尾を報告して終わる。

Codex 自身が 75 で終了した場合だけは、レートリミットの 75 と区別できないため 1 に写像する。
元の値は `codex-agent: result=failed exit=75` の行に残る。

スクリプトは末尾に結果の 1 行を出す。
成功なら `codex-agent: result=ok`、レートリミットなら `codex-agent: result=rate-limited`、それ以外の失敗なら `codex-agent: result=failed exit=<code>` である。

レートリミットの判定は、`codex` が 0 以外で終了し、かつ `usage limit`、`rate limit`、`too many requests`、`429` のいずれかが大文字小文字を問わず含まれる場合に限る。
判定の対象は標準出力と標準エラーの両方である。
Codex の版によって通知の出力先が変わるためである。
標準エラー側は、`WARNING` と `hook:` で始まる行を除いた後の内容だけを見る。
`429` は単語境界で照合し、ID や桁数の一致で誤検出しないようにしている。

スクリプト自体が見つからない場合も、GPT 側が未導入とみなしてフォールバックする。
サブエージェントはカレントディレクトリの `tools/codex-agent.sh` を先に探し、無ければ `%USERPROFILE%\.claude\tools\codex-agent.sh` を使い、どちらも無ければ自分で実装する。

## 役割を実装補助用アカウントへ移す

GPT 側の定義の `codex_home` を 1 行書き換える。

```yaml
codex_home: ~/.codex-subagent
```

これは実装補助という役割を、その用途に固定したアカウントへ移す変更である。
利用上限に達したアカウントから別のアカウントへ処理を回す切り替えではない。
その運用は [CLAUDE.md](../CLAUDE.md) の用途固定の原則で禁じている。

切り替え先の `CODEX_HOME` でのログインは [setup.md](setup.md) の手順に従う。
ログインを済ませないままだと、スクリプトが「codex_home が存在しない」として終了コード 2 で止まる。

## effort を変える

常用の値を変えるなら、GPT 側の定義の `codex_reasoning_effort` を書き換える。
1 回の依頼だけ変えるなら、`--effort` で上書きする。

```bash
bash tools/codex-agent.sh impl-light --effort low <<'EOF'
Reply with exactly: PONG-LUNA
EOF
```

`--effort` は定義ファイルの値より優先される。

## 既知の制約

**同じ作業ツリーに書き込む。**
Codex は Claude Code と同じ worktree で動く。
実行中はメインセッションが同じファイルを編集しない運用にしている。
書き込み範囲の大きい依頼は `codex-subagent` に回し、worktree を分ける。

**既定ホームのフック出力が混じる。**
既定ホーム `~/.codex` には Codex プラグインと ai-cross-review の設定が入っており、実行のたびにフックの出力が標準エラーへ流れる。
スクリプトは標準出力と標準エラーを別々に受け取り、標準エラー側からだけ `WARNING` と `hook:` で始まる行を除く。
Codex の回答本文が流れる標準出力にはフィルタを掛けない。
`codex_home` を専用ホームに移せば、この除去は不要になる。

**全プロジェクトに適用するには。**
スクリプトもサブエージェントも、カレントディレクトリの定義を先に探し、無ければユーザ定義を使う。
そのため、このリポジトリの定義は、このリポジトリでのみ効く。
全プロジェクトに適用するなら、次の 3 つを置く。

1. `.claude/agents/impl-light.md` と `.claude/agents/impl-standard.md` を `%USERPROFILE%\.claude\agents\` に置き換える。
2. `.claude/gpt-agents/` を `%USERPROFILE%\.claude\gpt-agents\` にコピーする。
3. `tools/codex-agent.sh` を `%USERPROFILE%\.claude\tools\` にコピーする。

## 動作確認

GPT 側が動くことを確認する。
この確認は実モデルを起動するため、利用枠を消費する。
消費を抑えるため `--effort low` を付ける。

```bash
bash tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-LUNA"
```

先頭に監査用の 1 行が出る。

```text
codex-agent: agent=impl-light model=gpt-5.6-luna effort=low sandbox=workspace-write codex_home=... workdir=...
```

続く Codex のヘッダで `model: gpt-5.6-luna` と `reasoning effort` を確認し、`PONG-LUNA` に続いて `codex-agent: result=ok` が出れば期待どおりである。

フォールバック経路は、試験用の環境変数で確認する。

```bash
CODEX_AGENT_SIMULATE_RATE_LIMIT=1 bash tools/codex-agent.sh impl-light <<< x; echo exit=$?
```

`codex-agent: result=rate-limited (simulated)` と `exit=75` が出る。
この環境変数は `codex` を起動せずに終了コード 75 を返すだけのもので、フォールバック経路の確認以外には使わない。
