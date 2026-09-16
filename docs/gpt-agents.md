# GPT 系サブエージェントを Codex CLI で動かす

Claude Code のサブエージェント `impl-hard`、`impl-light`、`impl-standard`、`codex-review`、`codex-subagent` は、ユーザ側の `~/.claude/tools/codex-agent.sh` を通じて Codex CLI を呼び出す。
`impl-hard`、`impl-light`、`impl-standard` は、GPT 側が未設定、未導入、無効化、または GPT 側の事情で使えないときだけ、サブエージェント自身が Claude として実装する。

## 目的

1 アカウントで運用する場合は、5 定義すべてが既定の認証ホーム(`~/.codex`)を使う。
2 アカウントで運用する場合は、通常利用とレビューに使うアカウントを既定ホーム(`~/.codex`)に置き、サブエージェント専用のアカウントに `~/.codex-subagent` を与える。
ここでいう**通常利用**は Codex CLI の対話、VS Code や Chrome の Codex 拡張、Claude Code の Codex プラグインを指し、**サブエージェント**は GPT 側へ実装を委譲する `impl-hard`、`impl-light`、`impl-standard`、`codex-subagent` の 4 定義を指す。
`codex-review` も Claude Code からはサブエージェントとして起動されるが、役割はレビューなので既定ホーム側に置く。
どの定義を配置するかは、実装用の委譲だけを使うパターンと、レビュー用も使うパターンで選べる。

委譲の対象は `impl-hard`、`impl-light`、`impl-standard`、`codex-review`、`codex-subagent` の 5 つである。
このうち `impl-hard`(`.claude/agents/impl-hard.md`)だけは、出荷時の GPT 側定義(`.claude/gpt-agents/impl-hard.md`)に `codex_model` を書いておらず、既定では GPT 側へ委譲せず、定義に書いた Claude 側のモデルが担う。
設計判断を伴う変更や、正しさの検証が難しい変更は、メインセッションと同じ Claude 系に留めたほうが、監査で挙動の食い違いを追いやすいためである。
GPT 側に委ねたい場合は、`.claude/gpt-agents/impl-hard.md` に `codex_model` と `codex_reasoning_effort` を書く(設定コンソールからも設定できる)。

## 構成

```text
Claude Code(メインセッション)
├─ codex-review                      .claude/agents/codex-review.md
│   └─ ~/.claude/tools/codex-agent.sh  .claude/gpt-agents/codex-review.md を読む
│       └─ codex exec                 CODEX_HOME=~/.codex、read-only
├─ codex-subagent                    .claude/agents/codex-subagent.md
│   └─ ~/.claude/tools/codex-agent.sh  .claude/gpt-agents/codex-subagent.md を読む
│       └─ codex exec                 CODEX_HOME=~/.codex、workspace-write
├─ impl-hard                         .claude/agents/impl-hard.md
│   └─ ~/.claude/tools/codex-agent.sh  .claude/gpt-agents/impl-hard.md を読む(既定は codex_model 未設定)
│       └─ codex exec                 codex_model を設定した場合のみ実行。CODEX_HOME=~/.codex、workspace-write
├─ impl-light                        .claude/agents/impl-light.md
│   └─ ~/.claude/tools/codex-agent.sh  .claude/gpt-agents/impl-light.md を読む
│       └─ codex exec                 CODEX_HOME=~/.codex、workspace-write
└─ impl-standard                     .claude/agents/impl-standard.md
    └─ ~/.claude/tools/codex-agent.sh  .claude/gpt-agents/impl-standard.md を読む
        └─ codex exec                 CODEX_HOME=~/.codex、workspace-write
```

この図の `CODEX_HOME` は 1 アカウント運用の値である。
2 アカウント運用の値は「認証ホームの割り当て」の表に従う。

`codex-review` と `codex-subagent` は同じ形で、サンドボックスだけが異なる。
`impl-hard`、`impl-light`、`impl-standard` も同じ形で、定義に書くモデルと effort だけが異なる。
`impl-hard` は出荷時の GPT 側定義に `codex_model` を書いていないため、既定ではこの経路を使わず Claude 側にフォールバックする。
Claude 側と GPT 側のモデルと effort は各定義のフロントマターが正であり、設定コンソール([gui.md](gui.md))や手編集で変えられる。
そのため、この文書には具体値を書かない。

5 つのサブエージェントは依頼を受けると、まずユーザ側の `~/.claude/tools/codex-agent.sh` を 1 回呼び、依頼文をヒアドキュメントで標準入力に流す。
コマンドライン引数に埋め込むと、依頼文に含まれる引用符やバックスラッシュで壊れるためである。
スクリプトが成功すれば、その出力をそのまま返す。
`impl-hard`、`impl-light`、`impl-standard` は、GPT 側が未導入、無効化、未設定、または GPT 側の事情で実行できないときだけ、サブエージェント自身が Claude として実装する。
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

GPT 側の定義の探索は、スクリプトを起動したカレントディレクトリを基準にする。
`-C` で別のディレクトリを作業ディレクトリに指定しても、読む定義は切り替わらない。
別のプロジェクトの定義を使いたい場合は、そのディレクトリへ移ってからスクリプトを起動する。

## フロントマターのキー

GPT 側の定義(`.claude/gpt-agents/<name>.md`)で使うキーは次の 5 つである。

- **codex_home**：`CODEX_HOME` に渡すディレクトリ。必須。`~`、`$USERPROFILE`、`%USERPROFILE%` を実パスへ展開する。存在しなければ実行せずに終了コード 2 で止まる。
- **codex_model**：`codex exec -m` に渡すモデル名。無いか空の場合は、その定義で GPT 側を使わない設定として終了コード 3 で止まる。区分ごとに GPT 経路の有無を切り替える手段であり、`codex_enabled`(定義をまとめて止める切替)とは役割が異なる。
- **codex_reasoning_effort**：`-c model_reasoning_effort=` に渡す値。省略時は `medium`。`low`、`medium`、`high`、`xhigh`、`max`、`ultra` のみを受け付ける。どの値が使えるかはモデルによって異なる。`codex debug models` の `supported_reasoning_levels` が正である。
- **codex_sandbox**：`--sandbox` に渡す値。省略時は `read-only`。`read-only` と `workspace-write` のみを受け付ける。
- **codex_enabled**：`true` または `false` のみを受け付ける。省略時は `true`。`false` のときは Codex を起動せず終了コード 3 で止まる。それ以外の値は終了コード 2 で止まる。キーを書いて値を空にした場合は省略とみなさず、終了コード 2 で止まる。省略時を `true` とするのは、既存の定義ファイルを書き換えずに動かし続けるためである。

`--dangerously-bypass-approvals-and-sandbox` はスクリプトに存在せず、フロントマターからも指定できない。
承認方針はスクリプトが `-c approval_policy=never` で固定し、フロントマターや呼び出し側の `config.toml` では変えられない。
このスクリプトは非対話の委譲専用で承認を返す相手がいないため、承認待ちで止まる余地を残さない。

Claude 側の定義(`.claude/agents/<name>.md`)のフロントマターは、Claude Code の通常のサブエージェント定義と同じ `name`、`description`、`model`、`effort` である。

## フォールバックの条件と終了コード

`impl-hard`、`impl-light`、`impl-standard` はスクリプトの終了コードでフォールバックの要否を決める。

- **0**：Codex が完了した。出力にある Codex の最終報告をそのまま返し、Claude 側では実装しない。
- **2**：引数、定義ファイルの内容、環境の不備でスクリプトが起動しなかった。フロントマターのキー不足、effort やサンドボックスの不正値、`codex_home` の不在、作業ディレクトリの不在、端末からの起動、空の依頼文がこれにあたる。実装せず、終了コードと出力の末尾を報告して終わる。
- **3**：GPT 側が未導入、無効化、または未設定である。`codex` コマンドが PATH に無い、`.claude/gpt-agents/<name>.md` が見つからない、`codex_enabled: false` が書かれている、`codex_model` が無いか空である、のいずれかに当たる場合である。サブエージェント自身が Claude として実装し、その旨を報告の冒頭に書く。キー名の誤記も `codex_model` の未設定と同じ経路で Claude 側へ倒れるため、報告の冒頭には `codex-agent:` の理由行をそのまま添える。
- **75**：呼び出し側では直せない GPT 側の事情で実行できなかった。利用上限の場合は `codex-agent: result=rate-limited`、それ以外の場合は `codex-agent: result=unavailable` として理由を区別する。`unavailable` は、いまのところモデルの混雑を示す `Selected model is at capacity` を対象にする。利用上限なら `codex-agent: rate-limit evidence: ...`、それ以外なら `codex-agent: unavailable evidence: ...` の行に判定の根拠を残す。サブエージェント自身が Claude として実装し、フォールバックした旨を報告の冒頭に書く。
- **権限判定で拒否された場合**：Bash の実行自体が拒否され、終了コードを得られない。
  `impl-hard`、`impl-light`、`impl-standard` は Claude 側で実装し、報告の冒頭に「Codex の呼び出しが権限判定で拒否されたため Claude 側で実装した」と書く。
  `codex-review` と `codex-subagent` は拒否の文言をそのまま報告して停止する。
- **その他**：Codex の終了コードをそのまま返している。実装せず、同じく終了コードと出力の末尾を報告して終わる。

`codex-review` と `codex-subagent` は、終了コードが 0 以外ならフォールバックしない。
終了コードと出力の末尾を報告して停止する。
これらは Codex への明示的なレビューまたは実装補助の依頼を扱うため、Claude 側が代行すると依頼の意味が変わるからである。

引数や定義の不備は呼び出し側で直せるため終了コード 2 で止め、GPT 側の事情は呼び出し側で直せないため終了コード 75 として呼び出し側が Claude 側へフォールバックできるようにする。

Codex 自身が 75 で終了した場合だけは、レートリミットの 75 と区別できないため 1 に写像する。
元の値は `codex-agent: result=failed exit=75` の行に残る。

**標準出力の形。**
Codex を起動して正常終了した場合、標準出力には監査用の行、ログファイルのパス、最終報告、結果の行だけが出る。
Codex の経過は標準エラーへ数百 KB 流れることがある。
これをそのまま返すと呼び出し側のツール結果が肥大してファイルへ退避され、報告を取り出せなくなるため、経過はログファイルへ残す。

```text
codex-agent: agent=<name> model=<定義の codex_model> effort=<実行時の値> sandbox=<定義の codex_sandbox> codex_home=... workdir=...
codex-agent: log=<ログファイルのパス>
<codex exec の最終報告>
codex-agent: result=ok
```

最終報告は `codex exec` の `--output-last-message` から取る。
この選択肢がない版では標準出力の末尾で代用する。
失敗時は最終報告の位置にログの末尾 40 行が出る。
ログは `$TMPDIR/codex-agent` に置き、`TMPDIR` が未設定なら `/tmp/codex-agent` を使う。
スクリプトの起動時に 7 日より古い `.log` を削除する。

スクリプトは末尾に結果の 1 行を出す。
成功なら `codex-agent: result=ok`、利用上限なら `codex-agent: result=rate-limited`、モデルの混雑など GPT 側の事情なら `codex-agent: result=unavailable`、それ以外の失敗なら `codex-agent: result=failed exit=<code>` である。
ただし、Codex を起動する前に終了コード 2 で止まる経路では出さない。
引数や定義の不備で止まる経路であり、`codex-agent: <理由>` の 1 行だけを標準エラーに出す。
Codex 自身が 2 を返した場合は、他の非 0 終了と同じく `codex-agent: result=failed exit=2` を出す。
レートリミットと判定したときは、その直前に `codex-agent: rate-limit evidence: <一致した行>` を出し、フォールバックの根拠を報告から追えるようにしている。
GPT 側の事情と判定したときは、その直前に `codex-agent: unavailable evidence: <一致した行>` を出す。

レートリミットの判定は、`codex` が 0 以外で終了し、判定対象に `usage limit`、`rate limit`、`too many requests`、`429` のいずれかが大文字小文字を問わず含まれる場合に限る。
失敗の判定対象は、標準出力と標準エラーをログへまとめた内容のうち、失敗を告げる行(`ERROR` または `stream error` で始まる行)と出力の末尾 10 行だけである。
従来のように出力の全文を対象にしないのは、Codex の出力には読み込んだファイルの中身も流れ、テストデータに含まれる文字列で誤検出するからである。
末尾 10 行を残すのは、失敗の通知が `ERROR` で始まらない版があり得るためである。
`unavailable` の判定は、上記の対象に `at capacity` が大文字小文字を問わず含まれる場合に限る。
標準出力と標準エラーのどちらに通知されても判定できるようにするためである。
標準エラー側は、`WARNING` と `hook:` で始まる行を除いた後の内容だけを見る。
`429` は単語境界で照合し、ID や桁数の一致で誤検出しないようにしている。

スクリプト自体が見つからない場合も、GPT 側が未導入とみなす。
5 つのサブエージェントは `bash ~/.claude/tools/codex-agent.sh` を固定で呼び、カレントディレクトリのスクリプトを探さない。
変数への代入や `[ -f ... ] ||` の分岐を前に付けないのは、権限の許可規則がコマンドの先頭一致で判定されるためである。
カレントディレクトリを先に探すのは GPT 側の定義ファイル(`.claude/gpt-agents/<name>.md`)だけであり、無ければ `%USERPROFILE%\.claude\gpt-agents\` の定義を使う。
ユーザ側の `~/.claude/tools/codex-agent.sh` が無ければ GPT 側が未導入として扱い、`impl-hard`、`impl-light`、`impl-standard` は自分で実装し、`codex-review` と `codex-subagent` は終了コードと出力の末尾を報告して停止する。

## 認証ホームの割り当て

1 アカウントで使う場合は、5 定義の `codex_home` を `~/.codex` にする。
2 アカウントで使う場合は、通常利用とレビューに使うアカウントが既定ホーム(`~/.codex`)を使い、サブエージェント専用のアカウントが `~/.codex-subagent` を使う。
通常利用のアカウントを既定ホームに置くのは、Codex CLI の対話、VS Code や Chrome の Codex 拡張、Claude Code の Codex プラグインが既定ホームしか見ないためである。
既定ホームを空けるとこれらが未ログイン扱いになり、`~/.codex` が自動で再生成される。
GPT 側の定義の `codex_home` は次の表のとおりに書く。

| GPT 側の定義 | `codex_home` |
|---|---|
| `codex-review` | `~/.codex` |
| `codex-subagent` | `~/.codex-subagent` |
| `impl-hard` | `~/.codex-subagent` |
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
`impl-hard`、`impl-light`、`impl-standard` は同じ worktree で動くが、実行中はメインセッションが同じファイルを編集しない。
書き込み範囲の大きい依頼は `codex-subagent` に回し、worktree を分ける。

**Codex が書き込めるのは `-C` で指定した作業ディレクトリの配下だけである。**
`workspace-write` の書き込み範囲は作業ディレクトリに限られる。
複数のプロジェクトにまたがる変更を委譲するときは、依頼文の作業ディレクトリに共通の親ディレクトリを指定する。

**1 回の委譲は Bash ツールの上限である 10 分に収める。**
Claude Code の版によって、Bash ツールが長時間のコマンドをバックグラウンドへ移して完了を通知する場合と、上限で打ち切る場合がある。
バックグラウンドへ移った場合は、完了の通知を待ち、通知の本文または通知が示す出力ファイルの末尾を 1 回だけ読んで `codex-agent: result=` の行で結果を判定する。
自分で `sleep` や `until` のループを回したり、プロセスの一覧を調べたり、出力ファイルを探し回ったりしない。
上限で打ち切られた場合は、その時点までの書き込みは残るが、報告は返らない。
`.claude/agents/` の 5 定義には「実行が長引いたとき」の節があり、バックグラウンドへ移った場合の待ち方と出力の読み方を定めている。
対象が多い依頼は、数プロジェクトずつに分けて委譲する。

**スクリプト自身の書き換えを Codex に任せると、実行中の bash が壊れ得る。**
bash はスクリプトを読みながら実行する。
`tools/codex-agent.sh` を書き換える依頼を Codex に渡すと、Codex がファイルを書き換えた時点で、待機中だった bash が続きを書き換え後の内容から読み、構文エラーで終了コード 2 になる。
対策として本体を `main` 関数に包み、末尾の呼び出し行までを読み終えてから実行するようにした。
それでも、このスクリプト自身の変更は Claude 側で行い、Codex には任せないほうが安全である。

**認証ホームのフック出力が混じる。**
`CODEX_HOME` に Codex プラグインや ai-cross-review の設定が入っている場合、実行のたびにフックの出力が標準エラーへ流れる。
スクリプトは標準出力と標準エラーを別々に受け取り、標準エラー側からだけ `WARNING` と `hook:` で始まる行を除く。
Codex の回答本文が流れる標準出力にはフィルタを掛けず、ログへ保存する。
フックを使わない専用ホームを `codex_home` に指定すれば、この除去は不要になる。

**全プロジェクトに適用するには。**
GPT 側の定義は、スクリプトを起動したカレントディレクトリの定義を先に探し、無ければユーザ定義を使う。
サブエージェントが呼ぶスクリプトはユーザ側の固定パスにあるため、リポジトリ側の `tools/codex-agent.sh` を直しても、ユーザ側へ配布するまでサブエージェントの動きは変わらない。
`.claude/agents/` の定義も、ユーザ側へ配布したうえで Claude Code のセッションを再起動するまで効かない。
このリポジトリで作業しているあいだは、スクリプトがユーザ側の複製、GPT 側の定義がリポジトリ側という混在で動く。
全プロジェクトに適用するなら、次の 3 つを置く。

1. `.claude/agents/` の 5 定義を `%USERPROFILE%\.claude\agents\` に置き換える。
2. `.claude/gpt-agents/` の 5 定義を `%USERPROFILE%\.claude\gpt-agents\` にコピーする。
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

先頭の監査用行(`agent=` の行)で定義どおりの `model` と実行時の `effort` を確認し、`PONG-LUNA` に続いて `codex-agent: result=ok` が出れば期待どおりである。

フォールバック経路は、試験用の環境変数で確認する。

```bash
CODEX_AGENT_SIMULATE_RATE_LIMIT=1 bash tools/codex-agent.sh impl-light <<< x; echo exit=$?
```

`codex-agent: result=rate-limited (simulated)` と `exit=75` が出る。

利用上限以外の事情で使えない経路も、同じ形で確認できる。

```bash
CODEX_AGENT_SIMULATE_UNAVAILABLE=1 bash tools/codex-agent.sh impl-light <<< x; echo exit=$?
```

`codex-agent: result=unavailable (simulated)` と `exit=75` が出る。
どちらの環境変数も `codex` を起動せずに終了コード 75 を返すだけのもので、フォールバック経路の確認以外には使わない。
