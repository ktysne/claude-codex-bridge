# GPT 系サブエージェントを Codex CLI で動かす

Claude Code のサブエージェント `impl-light`、`impl-standard`、`codex-review`、`codex-subagent` は、`tools/codex-agent.sh` を通じて Codex CLI を呼び出す。
`impl-light` と `impl-standard` は、GPT 側がレートリミットで使えないときだけ、サブエージェント自身が Claude として実装する。

## 目的

1 アカウントで運用する場合は、4 定義すべてが既定の認証ホーム(`~/.codex`)を使う。
2 アカウントで運用する場合は、実装用の 3 定義が `~/.codex-subagent` を使い、レビュー用の 1 定義が `~/.codex-review` を使う。
どの定義を配置するかは、実装用の委譲だけを使うパターンと、レビュー用も使うパターンで選べる。

委譲の対象は `impl-light`、`impl-standard`、`codex-review`、`codex-subagent` の 4 つである。
`impl-hard`(`.claude/agents/impl-hard.md`)は GPT 側へ委譲せず、定義に書いた Claude 側のモデルが担う。
設計判断を伴う変更や、正しさの検証が難しい変更は、メインセッションと同じ Claude 系に留めたほうが、監査で挙動の食い違いを追いやすいためである。

## 構成

```text
Claude Code(メインセッション)
├─ codex-review                      .claude/agents/codex-review.md
│   └─ tools/codex-agent.sh           .claude/gpt-agents/codex-review.md を読む
│       └─ codex exec                 CODEX_HOME=~/.codex、read-only
├─ codex-subagent                    .claude/agents/codex-subagent.md
│   └─ tools/codex-agent.sh           .claude/gpt-agents/codex-subagent.md を読む
│       └─ codex exec                 CODEX_HOME=~/.codex、workspace-write
├─ impl-light                        .claude/agents/impl-light.md
│   └─ tools/codex-agent.sh           .claude/gpt-agents/impl-light.md を読む
│       └─ codex exec                 CODEX_HOME=~/.codex、workspace-write
└─ impl-standard                     .claude/agents/impl-standard.md
    └─ tools/codex-agent.sh           .claude/gpt-agents/impl-standard.md を読む
        └─ codex exec                 CODEX_HOME=~/.codex、workspace-write
```

`codex-review` と `codex-subagent` は同じ形で、サンドボックスだけが異なる。
`impl-light` と `impl-standard` も同じ形で、定義に書くモデルと effort だけが異なる。
Claude 側と GPT 側のモデルと effort は各定義のフロントマターが正であり、設定コンソール([gui.md](gui.md))や手編集で変えられる。
そのため、この文書には具体値を書かない。

4 つのサブエージェントは依頼を受けると、まず `tools/codex-agent.sh` を 1 回呼び、依頼文をヒアドキュメントで標準入力に流す。
コマンドライン引数に埋め込むと、依頼文に含まれる引用符やバックスラッシュで壊れるためである。
スクリプトが成功すれば、その出力をそのまま返す。
`impl-light` と `impl-standard` はレートリミットで失敗したときだけ、サブエージェント自身が Claude として実装する。
`codex-review` と `codex-subagent` は、非 0 終了時にフォールバックせず、終了コードと出力末尾を報告して停止する。

## 定義ファイルの二層

定義ファイルは 2 つのディレクトリに分かれる。

- **`.claude/agents/<name>.md`**：Claude Code が読むサブエージェント定義。Claude 側のモデル、Bash による呼び出し方、実行失敗時の扱いを持つ。
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

GPT 側の定義(`.claude/gpt-agents/<name>.md`)で使うキーは次の 5 つである。

- **codex_home**：`CODEX_HOME` に渡すディレクトリ。必須。`~`、`$USERPROFILE`、`%USERPROFILE%` を実パスへ展開する。存在しなければ実行せずに終了コード 2 で止まる。
- **codex_model**：`codex exec -m` に渡すモデル名。必須。
- **codex_reasoning_effort**：`-c model_reasoning_effort=` に渡す値。省略時は `medium`。`low`、`medium`、`high`、`xhigh`、`max`、`ultra` のみを受け付ける。どの値が使えるかはモデルによって異なる。`codex debug models` の `supported_reasoning_levels` が正である。
- **codex_sandbox**：`--sandbox` に渡す値。省略時は `read-only`。`read-only` と `workspace-write` のみを受け付ける。
- **codex_enabled**：`true` または `false` のみを受け付ける。省略時は `true`。`false` のときは Codex を起動せず終了コード 3 で止まる。それ以外の値は終了コード 2 で止まる。キーを書いて値を空にした場合は省略とみなさず、終了コード 2 で止まる。省略時を `true` とするのは、既存の定義ファイルを書き換えずに動かし続けるためである。

`--dangerously-bypass-approvals-and-sandbox` はスクリプトに存在せず、フロントマターからも指定できない。
承認方針はスクリプトが `-c approval_policy=never` で固定し、フロントマターや呼び出し側の `config.toml` では変えられない。
このスクリプトは非対話の委譲専用で承認を返す相手がいないため、承認待ちで止まる余地を残さない。

Claude 側の定義(`.claude/agents/<name>.md`)のフロントマターは、Claude Code の通常のサブエージェント定義と同じ `name`、`description`、`model`、`effort` である。

## フォールバックの条件と終了コード

`impl-light` と `impl-standard` はスクリプトの終了コードでフォールバックの要否を決める。

- **0**：Codex が完了した。出力の末尾にある Codex の報告をそのまま返し、Claude 側では実装しない。
- **2**：引数、定義ファイルの内容、環境の不備でスクリプトが起動しなかった。フロントマターのキー不足、effort やサンドボックスの不正値、`codex_home` の不在、作業ディレクトリの不在、端末からの起動、空の依頼文がこれにあたる。実装せず、終了コードと出力の末尾を報告して終わる。
- **3**：GPT 側が未導入、または無効化されている。`codex` コマンドが PATH に無いか、`.claude/gpt-agents/<name>.md` が見つからないか、`codex_enabled: false` が書かれている場合である。サブエージェント自身が Claude として実装し、その旨を報告の冒頭に書く。
- **75**：Codex がレートリミットで実行できなかった。サブエージェント自身が Claude として実装し、フォールバックした旨を報告の冒頭に書く。
- **権限判定で拒否された場合**：Bash の実行自体が拒否され、終了コードを得られない。
  `impl-light` と `impl-standard` は Claude 側で実装し、報告の冒頭に「Codex の呼び出しが権限判定で拒否されたため Claude 側で実装した」と書く。
  `codex-review` と `codex-subagent` は拒否の文言をそのまま報告して停止する。
- **その他**：Codex の終了コードをそのまま返している。実装せず、同じく終了コードと出力の末尾を報告して終わる。

`codex-review` と `codex-subagent` は、終了コードが 0 以外ならフォールバックしない。
終了コードと出力の末尾を報告して停止する。
これらは Codex への明示的なレビューまたは実装補助の依頼を扱うため、Claude 側が代行すると依頼の意味が変わるからである。

Codex 自身が 75 で終了した場合だけは、レートリミットの 75 と区別できないため 1 に写像する。
元の値は `codex-agent: result=failed exit=75` の行に残る。

スクリプトは末尾に結果の 1 行を出す。
成功なら `codex-agent: result=ok`、レートリミットなら `codex-agent: result=rate-limited`、それ以外の失敗なら `codex-agent: result=failed exit=<code>` である。
ただし、Codex を起動する前に終了コード 2 で止まる経路では出さない。
引数や定義の不備で止まる経路であり、`codex-agent: <理由>` の 1 行だけを標準エラーに出す。
Codex 自身が 2 を返した場合は、他の非 0 終了と同じく `codex-agent: result=failed exit=2` を出す。
レートリミットと判定したときは、その直前に `codex-agent: rate-limit evidence: <一致した行>` を出し、フォールバックの根拠を報告から追えるようにしている。

レートリミットの判定は、`codex` が 0 以外で終了し、かつ `usage limit`、`rate limit`、`too many requests`、`429` のいずれかが大文字小文字を問わず含まれる場合に限る。
判定の対象は標準出力と標準エラーの両方である。
Codex の版によって通知の出力先が変わるためである。
標準エラー側は、`WARNING` と `hook:` で始まる行を除いた後の内容だけを見る。
`429` は単語境界で照合し、ID や桁数の一致で誤検出しないようにしている。

スクリプト自体が見つからない場合も、GPT 側が未導入とみなす。
4 つのサブエージェントはカレントディレクトリの `tools/codex-agent.sh` を先に探し、無ければ `%USERPROFILE%\.claude\tools\codex-agent.sh` を使う。
`impl-light` と `impl-standard` はどちらも無ければ自分で実装し、`codex-review` と `codex-subagent` は終了コードと出力の末尾を報告して停止する。

## 認証ホームの割り当て

1 アカウントで使う場合は、4 定義の `codex_home` を `~/.codex` にする。
2 アカウントで使う場合は、GPT 側の定義の `codex_home` を次の表のとおりにする。

| GPT 側の定義 | 2 アカウント運用の `codex_home` |
|---|---|
| `codex-review` | `~/.codex-review` |
| `codex-subagent` | `~/.codex-subagent` |
| `impl-light` | `~/.codex-subagent` |
| `impl-standard` | `~/.codex-subagent` |

これは各定義の役割を対応するアカウントへ固定する設定である。
利用上限に達したアカウントから別のアカウントへ処理を回す切り替えではない。
その運用は [CLAUDE.md](../CLAUDE.md) の用途固定の原則で禁じている。

各 `CODEX_HOME` でのログインは [setup.md](setup.md) の手順に従う。
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

**書き込み可能な呼び出しは worktree を分ける。**
`codex-review` は `read-only` で動くため、ファイルを書き換えない。
`codex-subagent` は `workspace-write` で動くため、呼び出し側が Claude Code と別の worktree を用意し、`-C` で別 worktree を必ず指定する。
`impl-light` と `impl-standard` は同じ worktree で動くが、実行中はメインセッションが同じファイルを編集しない。
書き込み範囲の大きい依頼は `codex-subagent` に回し、worktree を分ける。

**Codex が書き込めるのは `-C` で指定した作業ディレクトリの配下だけである。**
`workspace-write` の書き込み範囲は作業ディレクトリに限られる。
複数のプロジェクトにまたがる変更を委譲するときは、依頼文の作業ディレクトリに共通の親ディレクトリを指定する。

**1 回の委譲は Bash ツールの上限である 10 分に収める。**
Claude 側のサブエージェントは Bash ツールで `tools/codex-agent.sh` を呼ぶため、Codex の実行が 10 分を超えるとタイムアウトで打ち切られる。
その時点までの書き込みは残るが、報告は返らない。
対象が多い依頼は、数プロジェクトずつに分けて委譲する。

**スクリプト自身の書き換えを Codex に任せると、実行中の bash が壊れ得る。**
bash はスクリプトを読みながら実行する。
`tools/codex-agent.sh` を書き換える依頼を Codex に渡すと、Codex がファイルを書き換えた時点で、待機中だった bash が続きを書き換え後の内容から読み、構文エラーで終了コード 2 になる。
対策として本体を `main` 関数に包み、末尾の呼び出し行までを読み終えてから実行するようにした。
それでも、このスクリプト自身の変更は Claude 側で行い、Codex には任せないほうが安全である。

**認証ホームのフック出力が混じる。**
`CODEX_HOME` に Codex プラグインや ai-cross-review の設定が入っている場合、実行のたびにフックの出力が標準エラーへ流れる。
スクリプトは標準出力と標準エラーを別々に受け取り、標準エラー側からだけ `WARNING` と `hook:` で始まる行を除く。
Codex の回答本文が流れる標準出力にはフィルタを掛けない。
フックを使わない専用ホームを `codex_home` に指定すれば、この除去は不要になる。

**全プロジェクトに適用するには。**
スクリプトもサブエージェントも、カレントディレクトリの定義を先に探し、無ければユーザ定義を使う。
そのため、このリポジトリの定義は、このリポジトリでのみ効く。
全プロジェクトに適用するなら、次の 3 つを置く。

1. `.claude/agents/` の 5 定義(`impl-hard` を含む)を `%USERPROFILE%\.claude\agents\` に置き換える。
2. `.claude/gpt-agents/` の 4 定義を `%USERPROFILE%\.claude\gpt-agents\` にコピーする。
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
codex-agent: agent=impl-light model=<定義の codex_model> effort=low sandbox=workspace-write codex_home=... workdir=...
```

続く Codex のヘッダで定義どおりの `model` と `reasoning effort` を確認し、`PONG-LUNA` に続いて `codex-agent: result=ok` が出れば期待どおりである。

フォールバック経路は、試験用の環境変数で確認する。

```bash
CODEX_AGENT_SIMULATE_RATE_LIMIT=1 bash tools/codex-agent.sh impl-light <<< x; echo exit=$?
```

`codex-agent: result=rate-limited (simulated)` と `exit=75` が出る。
この環境変数は `codex` を起動せずに終了コード 75 を返すだけのもので、フォールバック経路の確認以外には使わない。
