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

## 実装担当に許す操作

`impl-hard`、`impl-light`、`impl-standard`、`codex-subagent` と、それらが委譲する GPT 側の実行が行うのは、作業ツリーの変更と検証までである。
コミット、push、git の履歴やブランチを変える操作、PR や Issue への投稿は、依頼文が求めても行わない。
これらは、メインセッションが差分を読み、検証コマンドを再実行して監査したうえで行う。
依頼文による例外を設けないのは、メインセッションが書いた依頼文だけで実装担当の権限が増える構成を避けるためである。
PR コメントの投稿やブランチの操作のような実装でない作業は、そもそも実装担当へ回さない。

GPT への委譲を止めて Claude 側で実装させたいときは、依頼文の最初の行に次の指定を置き、直後の行に理由を書く。

```text
委譲: Claude 側で実装
理由: <委譲を止める理由>
```

最初の空でない行がこの指定である依頼では、`impl-hard`、`impl-light`、`impl-standard` は `codex-agent.sh` を呼ばずに自分で実装し、報告の冒頭に「委譲の指定により Claude 側で実装した」と書いて、指定の行と理由の行を添える。
指定として認めるのは、最初の空でない行にあるこの書き方だけである。
依頼文の途中やコードブロックの中にある同じ行は指定として扱わない。
このリポジトリでは定義や文書そのものにこの行が書かれているため、それらを引用した依頼で委譲が止まらないようにするためである。
「Codex を使わずに」のような言い回しも指定として扱わない。
書き方を 1 つに決めておくのは、表現が揺れると、実装担当が定義の「必ず GPT 側へ委譲を試みる」と依頼文のどちらに従うかを毎回裁定することになるためである。

依頼の種類ごとの担当と、実装担当が行う操作は次のとおりである。
同じ表を 3 つの Claude 側定義にも載せている。

| 依頼 | 実装の担当 | 行う操作 |
|---|---|---|
| 通常の依頼 | GPT 側。使えなければ Claude 側 | 作業ツリーの変更と検証 |
| 最初の空でない行が `委譲: Claude 側で実装` の依頼 | Claude 側 | 作業ツリーの変更と検証 |
| コミットを求める依頼 | 上の 2 行と同じ | 作業ツリーの変更と検証。コミットは行わず、行っていないことを報告に書く |
| push、PR の作成、PR や Issue への投稿を求める依頼 | 上の 2 行と同じ | 作業ツリーの変更と検証。求められた操作は行わず、行っていないことを報告に書く |
| 禁止事項に当たる操作(他のサブエージェントへの委譲、`CLAUDE.md` と `AGENTS.md` の変更など)を求める依頼 | 上の 2 行と同じ | 禁止事項に当たる部分は行わず、理由を報告に書く。残りの実装は進める |

GPT 側の役割文(`.claude/gpt-agents/` の `impl-hard`、`impl-light`、`impl-standard`、`codex-subagent`)にも同じ禁止事項を書いている。
ユーザ側の GPT 側定義は丸ごと配らない運用なので、役割文を変えたときは、フロントマターを残して本文だけをユーザ側へ反映する。

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
Codex を起動して正常終了した場合、標準出力には監査用の行、実行の識別の行、ログファイルのパス、最終報告、結果の行だけが出る。
Codex の経過は標準エラーへ数百 KB 流れることがある。
これをそのまま返すと呼び出し側のツール結果が肥大してファイルへ退避され、報告を取り出せなくなるため、経過はログファイルへ残す。

```text
codex-agent: agent=<name> model=<定義の codex_model> effort=<実行時の値> sandbox=<定義の codex_sandbox> codex_home=... workdir=...
codex-agent: run=<実行 ID> pid=<ラッパーの PID> started=<開始時刻>
codex-agent: log=<ログファイルのパス>
<codex exec の最終報告>
codex-agent: result=ok
```

`run=` の行と `log=` の行は、Codex を起動する前に出す。
Bash ツールがコマンドをバックグラウンドへ移したあとも、実行中に出力ファイルからログの場所と実行の識別を読めるようにするためである。
実行 ID はログファイル名から拡張子を除いたもの(`<agent>-<YYYYmmdd-HHMMSS>-<番号>`)である。
PID は、Git Bash では `/proc/$$/winpid` から読んだ Windows の PID、読めない環境では bash の `$$` である。
開始時刻は UTC の ISO 8601 形式である。
ログは 1 行目に `run=` の行と同じ内容を書き、Codex の標準エラーを実行中から行ごとに追記する。
Codex が終わると標準出力をログへ追記し、最後の行に標準出力と同じ `result=` の行を書く。
ログの最後の行が `result=` の行でなければ、その実行は完了していないか、途中で止められている。
書き込み可能な定義では、同じ worktree に書き込み可能な別の実行が残っていると、`log=` の行の後に `codex-agent: warning=concurrent-writer run=<相手の実行 ID> log=<相手のログのパス>` の行が出うる。
意味と仕組みは「既知の制約」の「書き込み担当の目印と警告の行」にある。

最終報告は `codex exec` の `--output-last-message` から取る。
この選択肢がない版では標準出力の末尾で代用する。
失敗時は最終報告の位置にログの末尾 40 行が出る。
ログは `%USERPROFILE%\.claude\codex-agent\logs` に置く。
ログには Codex が読んだファイルの中身が入りうるため、共有される一時ディレクトリを避けてホーム配下に取る。
共有の場所では、他の利用者から読まれる余地と、先回りして置かれたシンボリックリンク越しに別のファイルを切り詰める余地が残る。
Codex の標準出力と最終報告の受け皿も、同じ置き場に `<実行 ID>.out` と `<実行 ID>.last` として置き、終了時に消す。
強制終了で終了時の後始末が動かなくても、実行 ID から特定して消せるようにするためである。
スクリプトの起動時に 7 日より古い `.log`、`.last`、`.out` を削除する。

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

**1 つの worktree に同時に書き込む担当は 1 つである。**
担当には、メインセッション、`impl-hard`、`impl-light`、`impl-standard`(Claude 側で実装する場合の実装担当自身と、委譲する GPT 側の実行)、`codex-subagent`、`npm run review:codex:fix`(ai-cross-review の `--fix`)を含む。
担当するファイルを分けても足りないのは、ビルドの生成物、テストの実行、git の索引が worktree の中で共有されるためである。
`codex-review` は `read-only` で動き、ファイルを書き換えないため、担当に数えない。
GPT 側へ委譲した実行や `codex-subagent` が同じ worktree に書き込むあいだ、メインセッションはその worktree を編集しない。
`codex-subagent` は、カレントディレクトリが対象の worktree であれば `-C` を省いてよい。

並行させたいときは、担当ごとに別 worktree を使う。
Agent ツールの `isolation: "worktree"` でも別 worktree を用意できる。
ただし、隔離された実装担当では、依頼文に「git」という語を含むヒアドキュメントを渡す `codex-agent.sh` の起動が隔離の検査で拒否され、Claude 側で実装されることがある(2026-09-16 に観測)。
また、メインセッションが作業の途中で別の worktree へ移ると、元の worktree で隔離せずに動かしていたバックグラウンドのサブエージェントのコマンドが拒否されることがある(2026-09-16 に観測)。
実装担当を並行して動かすあいだは、メインセッションが worktree を移らないか、実装担当を `isolation: "worktree"` で起動する。

**書き込み担当の目印と警告の行。**
ラッパーは、`codex_sandbox` が `workspace-write` の起動に限り、作業ディレクトリの worktree 固有の git ディレクトリ(`git rev-parse --absolute-git-dir` の値)に目印を置く。
置き場は `<git ディレクトリ>/codex-agent/runs/` で、目印は `<実行 ID>.run` というファイルである。
中身は、`run=` の行と同じ内容の 1 行、`codex-agent: log=<ログのパス>` の 1 行、`codex-agent: agent=<名前> sandbox=<値>` の 1 行である。
共通の git ディレクトリでなく worktree 固有の git ディレクトリに置くのは、同じリポジトリの別 worktree で並行する起動を重なりと見なさないためである。
目印はラッパーの終了時に消す。
作業ディレクトリが git の管理下に無い場合、git が無い場合、目印の読み書きに失敗した場合は、目印を扱わずに従来どおり起動する。
`read-only` の起動は目印を置かず、他の目印も調べない。

ラッパーは自分の目印を置く前に、置き場にある他の目印を 1 つずつ調べる。

- 目印が示すログが存在しない場合(7 日を過ぎたログの削除で消えた場合を含む)は、古い目印として消す。
- ログの最後の行が `codex-agent: result=` の行であれば、その実行は終わっているので、目印を消す。
- それ以外は、実行中か、強制終了で後始末が動かずに残った実行である。目印を消さず、標準出力とログに `codex-agent: warning=concurrent-writer run=<相手の実行 ID> log=<相手のログのパス>` の行を出す。

警告の行は、同じ worktree に書き込み可能な別の実行が残っている可能性を示す。
実行は止めない。
警告を受けたメインセッションは、行が示すログの最後の行を読み、相手が実行中であれば、同じ worktree への書き込みが重なっていないかを確かめる。
警告の行は失敗の判定(利用上限などの語の照合)の対象に含めない。実行 ID やパスの数字が `429` に一致しうるためである。

警告は観測の補助であり、次の点に限界がある。

- メインセッションの編集はラッパーを通らないため、目印に現れない。GPT 側への委譲とメインセッションの編集の重なりは検出できない。
- 強制終了で残った目印は、ログの最後の行が `result=` の行でないため、実行中の目印と区別できない。止める手順の最後で目印を消さないと、警告が出続ける。
- 2 つの起動がほぼ同時に始まると、互いの目印を置く前に調べ終え、警告が出ないことがある。

警告から排他(重なりを見つけたら起動しない形)へ上げるのは、再測定で同じ worktree の並行が残っており、かつ強制終了で残った目印を Windows で正しく見分けられると確かめた場合に限る。
見分けられないまま排他にすると、残った目印 1 つで、その worktree の委譲がすべて止まるためである。

**Codex が書き込めるのは `-C` で指定した作業ディレクトリの配下だけである。**
`workspace-write` の書き込み範囲は作業ディレクトリに限られる。
複数のプロジェクトにまたがる変更を委譲するときは、依頼文の作業ディレクトリに共通の親ディレクトリを指定する。

**1 回の委譲は Bash ツールの上限である 10 分に収める。**
Claude Code の版によって、Bash ツールが長時間のコマンドをバックグラウンドへ移して完了を通知する場合と、上限で打ち切る場合がある。
バックグラウンドへ移った場合は、完了の通知を待ち、通知の本文または通知が示す出力ファイルの末尾を読んで `codex-agent: result=` の行で結果を判定する。
途中経過が要るときは、出力ファイルの `codex-agent: log=` の行が示すログの末尾を数十行まで読んでよい。
自分で `sleep` や `until` のループを回したり、プロセスの一覧を調べたり、ファイルを探し回ったりしない。
`result=` の行を確認できないままターンを終えるサブエージェントは、報告の冒頭に「進行中」と書き、出力ファイルにある `codex-agent: agent=` の監査行をそのまま添える。
`run=` の行と `log=` の行があれば、それもそのまま添える。
古いラッパーは実行中にどちらも出さないため、無い場合は Bash ツールが示した出力ファイルのパスを添える。
監査行は、古いラッパーでも Codex の起動前に出ている。
Claude Code のセッション記録で確かめたところ、バックグラウンドで動くサブエージェントが `result=` の行を確認できないままターンを終えると、親には待機中の一言を本文にした完了の通知が先に届く。
その後、Bash ツールがバックグラウンドへ移したコマンドが終わると、サブエージェントは通常は自動で再開し、同じエージェントの通知をもう一度送って最終報告を返す。
そのため、メインセッションは「進行中」の報告を完了の報告として扱わず、同じエージェントの次の通知を待つ。
待つあいだ、作業ツリーの差分やプロセスを定期的に監視しない。
報告には、呼び出し側への案内として、定義に書いた決まった文面も添える。
呼び出し側のセッションは別のプロジェクトで動いていることが多く、このリポジトリの CLAUDE.md や文書を読まないため、報告の本文だけで次の行動が決まるようにする。
ただし、記録には再開を確かめられない例も少数あり、その原因は分かっていない。
そのため、案内の文面には、次の通知の前に状態を確かめる方法も含める。
状態を確かめたいときは、メインセッションはファイルを読まず、サブエージェントへ「codex-agent の状態確認」とだけ書いたメッセージを送る。
1 回の確認で送るのは 1 通だけにし、続けて送ったり定期的に送ったりしない。
返答が「進行中」だったあとも通知が届かず、改めて確かめる必要が出たときは、同じメッセージをもう一度送ってよい。1 回で打ち切ると、そのあとで再開が起きなかった実行の結果を回収できないためである。
ファイルの内容を結果として受け取らないのは、終了コードごとの扱い(`impl-*` の Claude 側へのフォールバックなど)がサブエージェントの定義にしか無く、ログには経過が混ざっていて最終報告だけを取り出せないためである。
サブエージェントは、このメッセージか Bash ツールの完了通知で再開したら、自分の出力ファイルの末尾を読む。このメッセージは新しい依頼ではないので、`codex-agent.sh` は再実行しない。
`result=` の行があれば、その値を終了コードに読み替えて通常どおり扱う。`ok` は 0、`rate-limited` と `unavailable` は 75、`failed exit=<code>` はその値(75 のときは 1)である。
`result=` の行が無ければ、同じ添付と案内で「進行中」をもう一度返す。
メッセージでの再開と完了通知での再開が重なると、「進行中」でない通知が 2 回届くことがある。メインセッションは最初の通知を最終報告とし、サブエージェントは扱い済みの結果を扱い直さない。
サブエージェントへメッセージを送れないときに限り、メインセッションは確かめるたびに報告に添えた出力ファイルまたはログの最後の行を読み、Codex の実行が残っているかを確かめる。
ファイルの内容は結果として扱わない。最後の行が `result=` の行でないうちは、委譲をやり直さない。
ログが空でないことは、実行が終わった根拠にならない。ログの 1 行目は Codex の起動前に書かれるためである。
監査行が無い「進行中」の報告と、`run=` の行も出力ファイルのパスも無い「進行中」の報告は、Codex を経由した根拠も残った実行を探す手がかりも無いので、無効な報告として扱う([CLAUDE.md](../CLAUDE.md) の「委譲の検証」)。
委譲をやり直す前に残った実行を確かめるとき、`run=` の行が無い報告では、下の「実行 ID で探せない場合」の手順を使う。
上限で打ち切られた場合は、その時点までの書き込みは残るが、報告は返らない。
`.claude/agents/` の 5 定義には「実行が長引いたとき」と出力の読み方の節があり、バックグラウンドへ移った場合の待ち方と出力の読み方を定めている。
古いラッパーでは `run=` の行が無く、`log=` の行は Codex の終了後に出る。定義はこの形でも `log=` の行と `result=` の行で同じように読む。
対象が多い依頼は、数プロジェクトずつに分けて委譲する。

**Bash ツールでバックグラウンドのコマンドを止めても、ラッパーと Codex は残る。**
Windows 10 と Git Bash 5.3 で確かめたところ、Bash ツールで止まるのは、ツールが起動した最上位のシェルだけだった。
その下のラッパー、ラッパーが起動した Codex(npm のシムと node、その子の実行ファイル)は動き続け、bash の trap も動かない。
そのため、ラッパーは停止のシグナルを受けて Codex を止める処理を持たず、止める対象を特定するための `run=` の行を出す。
残ったまま同じ作業を委譲し直すと、同じ作業ツリーに 2 つの Codex が書き込むおそれがある。

中断したあとで同じ作業を委譲し直す前に、メインセッションは次の順で確かめる。

1. 出力の `log=` の行が示すログの最後の行を読む。
   `codex-agent: result=` の行であれば、その実行は終わっている。何も止めない。
2. 出力の `run=` の行から PID、実行 ID、開始時刻(`started=`)を控え、その PID のプロセスがラッパー本人であるかを確かめる。
   PID は別のプロセスに再利用されうるため、PID が残っているだけでは止めない。
   コマンドラインに `codex-agent.sh` を含み、作成時刻が `started=` の前後 60 秒以内にあるときだけ、ラッパーと見なして `taskkill /T /F /PID <pid>` で止める。
   一致しなければ止めず、手で確かめる。
   ラッパーを先に止めるのは、Codex 側を先に止めると、ラッパーが続きを実行して `result=` の行を書くためである。
   止めたシムの終了コードが 0 として返り、`result=ok` と書かれることがあり、完了と取り違えやすい。
3. ラッパーが残っていなかった場合も、コマンドラインに `<実行 ID>.last` を含むプロセスを探し、残っていれば `taskkill /T /F /PID <pid>` で止める。
   `.last` は、ラッパーが `codex exec` の `-o` に渡す最終報告の受け皿のファイル名である。
   実行 ID は時刻と番号を含み、別の実行と重ならないため、PID と違って作成時刻を確かめなくてよい。
   2 だけでは Codex まで止まらないため、この手順が要る。
   Git Bash が Git Bash 系のプログラム(npm のシムの `sh` など)を起動すると、中継のプロセスが先に終わり、Windows 上の親子関係がラッパーから途切れるためである。
4. 止めたあと、ログ置き場(`%USERPROFILE%\.claude\codex-agent\logs`)の `<実行 ID>.last` と `<実行 ID>.out` を消す。ログ本体の `<実行 ID>.log` は残す。
   強制終了ではラッパーの終了時の後始末が動かないため、この 2 つが残る。
   消さなくても、次にラッパーを起動したときに 7 日より古いものは消える。
   書き込み可能な定義の実行であれば、作業ディレクトリの目印 `<git ディレクトリ>/codex-agent/runs/<実行 ID>.run` も消す。
   消さなくても、ログの最後の行が `result=` の行でない限り、同じ worktree で書き込み可能な起動をするたびに警告の行が出続ける。ログが 7 日を過ぎて消えれば、次の起動が目印を消す。

2 の PowerShell の例を示す。
`started=` は UTC なので、両辺を UTC に揃えて比べる。

```powershell
$wrapperPid = <run= の pid>
$started = '<run= の started>'
$p = Get-CimInstance Win32_Process -Filter "ProcessId=$wrapperPid"
if (-not $p) {
  'ラッパーは残っていない'
} elseif ($p.CommandLine -like '*codex-agent.sh*' -and
    [Math]::Abs(($p.CreationDate.ToUniversalTime() - ([datetimeoffset]$started).UtcDateTime).TotalSeconds) -le 60) {
  taskkill /T /F /PID $wrapperPid
} else {
  "PID $wrapperPid は別のプロセスに再利用されている可能性がある。止めずに確かめる: $($p.Name) $($p.CommandLine)"
}
```

3 の PowerShell の例を示す。
実行 ID を検索の文字列と分けて変数に入れるのは、問い合わせたシェル自身のコマンドラインが一致しないようにするためである。
一致するのはシム、node、Codex の実行ファイルの 3 つになることが多い。最初の `taskkill /T` で残りも止まるため、続く呼び出しが「見つからない」と失敗することがあるが、害はない。

```powershell
$id = '<実行 ID>'
Get-CimInstance Win32_Process |
  Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like "*$id.last*" } |
  ForEach-Object { taskkill /T /F /PID $_.ProcessId }
```

Git Bash から `taskkill` を呼ぶときは、`/T` などが Git Bash のパス変換で書き換えられないよう、`taskkill //T //F //PID <pid>` と書く。

**実行 ID で探せない場合。**
`--output-last-message` を持たない版の Codex では、ラッパーが `-o` を渡さないため、3 の方法では探せない。
`run=` の行を出さない古いラッパーでも、実行 ID が分からないため同じである。
この場合は、コマンドラインに `exec` と、監査行の `workdir=` の値(`codex exec` の `-C` に渡る作業ディレクトリ)と、監査行の `sandbox=` の値(`--sandbox` に渡る値)を含むプロセスを候補にする。
`sandbox=` の値でも絞るのは、同じ作業ディレクトリで読み取り専用のレビュー(`codex-review`)と書き込み可能な委譲が並ぶことがあり、中断した側だけを候補にするためである。
1 つの作業ツリーに書き込む Codex は同時に 1 つという前提なので、書き込み可能な委譲の候補は通常 1 つになる。
候補の作成時刻が中断した委譲と合うことを確かめてから、`taskkill /T /F /PID <pid>` で止める。
複数あって見分けられなければ、止めずに手で確かめる。
ラッパーが残っていれば、先に 2 の確かめ方で止める。

PowerShell の例を示す。
`workdir=` の値は `/` 区切りだが、コマンドライン側が `\` 区切りでも一致するよう、区切りをどちらにも一致させて照合する。
シム、node、Codex の実行ファイルがそろって一致するため、親が候補に含まれない最上位のプロセスだけを残す。
`-C` と `exec` の照合に前の空白を求めるのは、問い合わせたシェル自身のコマンドラインが一致しないようにするためである。

```powershell
$workdir = '<監査行の workdir= の値>'
$sandbox = '<監査行の sandbox= の値>'
$dir = [regex]::Escape($workdir) -replace '/', '[\\/]'
$all = @(Get-CimInstance Win32_Process | Where-Object {
  $_.ProcessId -ne $PID -and
  $_.CommandLine -match '\sexec\s' -and
  $_.CommandLine -match ('\s-C\s+"?' + $dir + '"?(\s|$)') -and
  $_.CommandLine -match ('\s--sandbox\s+"?' + [regex]::Escape($sandbox) + '"?(\s|$)')
})
$ids = @($all | ForEach-Object { $_.ProcessId })
$all | Where-Object { $ids -notcontains $_.ParentProcessId } |
  Select-Object ProcessId, Name, CreationDate, CommandLine | Format-List
```

候補が 1 つで、作成時刻とコマンドラインを確かめたら、その `ProcessId` を `taskkill /T /F /PID <pid>` に渡す。
止めたあとは、4 と同じくログ置き場の一時ファイルを消す。
古いラッパーでは `.out` を作らないため、ログ置き場で消す対象は `.last` だけである。
古いラッパーは起動時の削除で `.last` を消さないため、この場合は手で消す。

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
`.claude/agents/` の定義も、ユーザ側へ配布するまではサブエージェントの動きが変わらない。
配布先のディレクトリがセッション開始時から在れば、書き換えは数秒で次の委譲に反映される。そのディレクトリを新しく作った場合など、再起動が要る条件は [setup.md](setup.md) の共通手順 6 にある。
このリポジトリで作業しているあいだは、スクリプトがユーザ側の複製、GPT 側の定義がリポジトリ側という混在で動く。
全プロジェクトに適用するなら、次の 3 つを置く。
配布は、ラッパー(3)を定義(1)より先に行う。
定義を先に配ると、新しい定義が `run=` の行を出さない古いラッパーの出力を読む時間ができるためである。
定義は古いラッパーの出力も読めるように書いてあるが、「進行中」の報告から実行 ID が欠け、止める手順が「実行 ID で探せない場合」に落ちる。

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
