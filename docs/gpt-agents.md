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

5 つのサブエージェントは依頼を受けると、最初に Bash で `bash ~/.claude/tools/codex-agent.sh --new-prompt <定義名> "<スクラッチパッド>"` を実行して置き場を払い出す。
スクラッチパッドが示されていないときは親ディレクトリの引数を付けない。
ラッパーは `mktemp -d` で `codex-agent-<定義名>-<YYYYMMDD>-<HHMMSS>-<英数字 6 文字>` のディレクトリを排他的に作り、`codex-agent: prompt-file=<ディレクトリ>/prompt.md` の 1 行を返す。
サブエージェントはそのパスに依頼文の全文を Write で書き、ファイルを標準入力へリダイレクトしてラッパーを呼ぶ。
排他的に作ったディレクトリを使うのは、同じセッションから並行して起動した担当同士が依頼文を上書きしないようにするためである。
名前の日時や乱数をモデルに作らせると、同じ文脈の担当が同じ値を出し、後の Write が先の依頼文を黙って上書きする。
スクラッチパッドはセッション単位で共有され、`isolation: "worktree"` でも分かれない。
指定した親ディレクトリが無い場合は作らず、終了コード 2 で止まる。
親ディレクトリを省略した場合は `~/.claude/codex-agent/prompts/` を作り、権限を 700 にする。
依頼文をファイルで渡すのは、コマンド文字列(引数やヒアドキュメント)に埋め込むと、Bash ツールの加工で 8,191 文字を超える依頼文が構文エラーになり、`\\` が `\` に変わるためである。
`prompts/` 直下の 7 日を過ぎた依頼文ファイルと、名前が `codex-agent-*` の 7 日を過ぎた依頼文ディレクトリは、ラッパーが中身ごと消す。
通常転送で標準入力が通常ファイルのときは、監査行の直後に `codex-agent: prompt-file=<パス> sha256=<64 桁の 16 進>` を出し、`--wait` の報告にも含める。
パイプやヒアストリングから読んだ場合、またはパス取得やハッシュ計算が失敗した場合は、この行を出さない。
スクリプトが成功すれば、その出力をそのまま返す。
`impl-hard`、`impl-light`、`impl-standard` は、GPT 側が未導入、無効化、未設定、または GPT 側の事情で実行できないときだけ、サブエージェント自身が Claude として実装する。
`codex-review` と `codex-subagent` は、非 0 終了時にフォールバックせず、終了コードと出力末尾を報告して停止する。

## ラッパー役の定義の道具を絞る

`codex-review` と `codex-subagent` は、依頼文を Codex へ転送するだけの定義である。
定義の文言で禁じても、ラッパーを実行せずに Bash で差分やファイルを読み、自分でレビューや調査をする例が測定で見つかった。
そこで 2 定義のフロントマターに PreToolUse のフックを置き、Bash と Write を転送に要る形だけに道具の側で絞る。

```yaml
hooks:
  PreToolUse:
    - matcher: "Bash|Write"
      hooks:
        - type: command
          command: "node \"$HOME/.claude/tools/codex-agent-hook.js\" codex-review || exit 2"
```

`codex-subagent` の定義では、引数を `codex-subagent` にする。
フックの本体は `tools/codex-agent-hook.js` で、標準入力にフックの JSON を受け取る。
許すときは何も出さずに終了コード 0 で終わり、拒否するときは標準エラーに 1 段落の理由を書いて終了コード 2 で終わる。
PreToolUse のフックが 2 で終わると、権限のモードに関係なくツールの呼び出しが止まり、理由がモデルへ渡る。

**Bash で許す形。**
`tool_input.command` の前後の空白を除いた全体が、次の 4 形のどれかに完全に一致するときだけ許す。
連結の記号を 1 つずつ拒否する書き方にしないのは、数え漏れた記号があると転送以外のコマンドが通るためである。

```text
bash ~/.claude/tools/codex-agent.sh <定義名> [-C <パス>] < "<依頼文のファイル>"
bash ~/.claude/tools/codex-agent.sh --new-prompt <定義名> ["<親ディレクトリ>"]
bash ~/.claude/tools/codex-agent.sh --wait <実行 ID>
bash ~/.claude/tools/codex-agent.sh --header-of "<出力ファイル>"
```

- `<定義名>` はフックの引数と同じでなければならない。
- `--new-prompt` の親ディレクトリは省略できる。指定する場合は二重引用符で囲む。
- `-C` のパスは二重引用符で囲んでもよい。囲まない場合は、文字、数字、`._~/:+@,=-` だけにする。空白やシェルの記号を含むパスは囲む。
- `<依頼文のファイル>` と `<出力ファイル>` は二重引用符で囲む。
- `<依頼文のファイル>` には、下の「Write で許す形」と同じ名前と置き場の条件をかける。任意のファイル(認証情報など)を、依頼文として Codex へ送らせないためである。
- 引用符の中のパス(`<依頼文のファイル>`、`<出力ファイル>`、`--new-prompt` の親ディレクトリ)には、`"`、`$`、バッククォート、改行、NUL を含めない。
- 実行 ID は `[A-Za-z0-9._-]+` とする。
- 語と語の間は、空白かタブが 1 個以上あればよい。

**Write で許す形。**
`tool_input.file_path` を `/` 区切りに揃えて、次の 3 つをどれも満たすときだけ許す。

- ファイル名が `prompt.md` である。
- 親ディレクトリの名前が `codex-agent-<定義名>-<8 桁の数字>-<6 桁の数字>-<英数字 6 文字>` に一致する。
- その親ディレクトリ(祖父母)の名前が `scratchpad` か、そのパスが `/.claude/codex-agent/prompts` で終わる。

Bash と Write 以外のツール、JSON として読めない入力、`tool_name` か `tool_input` の無い入力も拒否する。
フックの引数が `codex-review` と `codex-subagent` のどちらでもないときも拒否する。

**フックが起動できないときは拒否する。**
command の末尾の `|| exit 2` は、node が無い、`~/.claude/tools/codex-agent-hook.js` が無いといった理由でフック自体が失敗したとき、終了コードを 2 に揃える。
2 以外の終了コードでは、Claude Code はツールの呼び出しを止めないためである。
そのため、フックのスクリプトより先に定義だけを配ると、2 定義の Bash と Write はすべて拒否される。

**効く条件。**
フロントマターの `hooks` は、そのサブエージェントの実行中だけ効く。
ユーザ定義(`~/.claude/agents/`)ではそのまま効く。
プロジェクト定義(`.claude/agents/`)では、そのフォルダのワークスペース信頼が要る。
worktree で動くときは、worktree ではなく元のリポジトリのフォルダの信頼で判定される(Claude Code 2.1.282 で確認)。
信頼が無いと、フックだけが黙って飛ばされ、定義の本文と道具はそのまま使われる。
プロジェクト定義は同じ名前のユーザ定義より優先されるので、このリポジトリで動かすときは、元のフォルダで Claude Code を一度起動して信頼を受け入れておく。
プラグインが配る定義では無視される。
フックのコマンドは、Claude Code が Git Bash を見つけられるときに bash で実行される。
見つけられない環境では PowerShell で実行され、フックの拒否が止める拒否として扱われなかった(同じ版で確認)。
その環境では Bash ツール自体が使えないが、Write は絞られない。

**拒否されたときの定義の振る舞い。**
2 定義は、フックの拒否を権限判定の拒否と同じく扱う。
拒否の文言(`codex-agent-hook:` で始まる)をそのまま報告して停止し、別の形で試し直したり、自分で調べたりしない。
Bash ツールがラッパーを背景へ移したときは、出力ファイルを読む代わりに `--header-of` で実行 ID を取る(「フォールバックの条件と終了コード」の「完了を待つ」)。
「進行中」の報告には、`--header-of` が返した行(監査行、`prompt-file=`、`run=`、`log=`、警告の行)を添える。`not-found` のときは出力ファイルのパスを添える。

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
| 禁止事項に当たる操作(他のサブエージェントへの委譲、`CLAUDE.md` と `AGENTS.md` の変更、作業ツリーの外への書き込みや削除など)を求める依頼 | 上の 2 行と同じ | 禁止事項に当たる部分は行わず、理由を報告に書く。残りの実装は進める |

GPT 側の役割文(`.claude/gpt-agents/` の `impl-hard`、`impl-light`、`impl-standard`)にも同じ禁止事項を書いている。`codex-subagent` にはコミットと `CLAUDE.md` / `AGENTS.md` に関する禁止事項だけを書いている。
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

Claude 側の定義(`.claude/agents/<name>.md`)のフロントマターは、Claude Code の通常のサブエージェント定義と同じ `name`、`description`、`model`、`effort` に、`disallowedTools` を加えたものである。
`impl-hard`、`impl-light`、`impl-standard` は `disallowedTools: Agent` で Agent ツールを外している。
実装担当が別のサブエージェントを立ててそこへ委譲すると、報告が 2 段になり、メインセッションが起動していない担当の通知が届くためである。
禁止事項の文だけでは、「委譲」を Codex CLI への依頼でなくサブエージェントへの依頼と読んだモデルを止められないので、ツールの許可で塞ぐ。
許可リスト(`tools`)でなく `disallowedTools` を使うのは、Bash 以外に継承している MCP ツールを列挙せずに済ませるためである。

## フォールバックの条件と終了コード

`impl-hard`、`impl-light`、`impl-standard` はスクリプトの終了コードでフォールバックの要否を決める。

- **0**：Codex が完了した。出力にある Codex の最終報告をそのまま返し、Claude 側では実装しない。ただし、Codex が依頼文の検証を実行できなかった場合は、検証だけを Claude 側で実行する(「既知の制約」の「子プロセスを起こす検証は `spawn EPERM` で失敗する」)。
- **2**：引数、定義ファイルの内容、環境の不備でスクリプトが起動しなかった。フロントマターのキー不足、effort やサンドボックスの不正値、`codex_home` の不在、作業ディレクトリの不在、端末からの起動、空の依頼文がこれにあたる。実装せず、終了コードと出力の末尾を報告して終わる。
- **3**：GPT 側が未導入、無効化、または未設定である。`codex` コマンドが PATH に無い、`.claude/gpt-agents/<name>.md` が見つからない、`codex_enabled: false` が書かれている、`codex_model` が無いか空である、のいずれかに当たる場合である。サブエージェント自身が Claude として実装し、その旨を報告の冒頭に書く。キー名の誤記も `codex_model` の未設定と同じ経路で Claude 側へ倒れるため、報告の冒頭には `codex-agent:` の理由行をそのまま添える。
- **75**：呼び出し側では直せない GPT 側の事情で実行できなかった。利用上限の場合は `codex-agent: result=rate-limited`、それ以外の場合は `codex-agent: result=unavailable` として理由を区別する。`unavailable` は、いまのところモデルの混雑を示す `Selected model is at capacity` と、終了コード 0 でもツール接続が一度も成立しなかった実行(`code-mode host exited during handshake`)を対象にする。利用上限なら `codex-agent: rate-limit evidence: ...`、それ以外なら `codex-agent: unavailable evidence: ...` の行に判定の根拠を残す。サブエージェント自身が Claude として実装し、フォールバックした旨を報告の冒頭に書く。
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

Codex の経過に子プロセスの起動失敗の印があった実行では、最終報告の前(`log=` の行と `warning=concurrent-writer` の行の後)に次の 2 行が出る。
`result=` の値は変わらない。

```text
codex-agent: warning=child-spawn-failed count=<一致した行の数>
codex-agent: child-spawn-failed evidence: <最初に一致した行>
```

`ok` を 75 に倒さないのは、倒すと実装担当が GPT 側の実装を捨てて Claude 側で作り直すためである。
`result=` の行の直前に置かないのは、そこには終了コード 75 の根拠の行が来ると 5 定義が決めているためである。
ログにも警告の行を書き、報告の写し(`<実行 ID>.report`)にも標準出力と同じく入る。
印と照合の範囲は「既知の制約」の「子プロセスを起こす検証は `spawn EPERM` で失敗する」にある。

Codex の経過に、MSBuild がエラー文を出さずに `Checking File Globs` の直後で止まった印があった実行では、上の 2 行の後に次の 2 行が出る。
根拠の行は、失敗したコマンドを Codex が起動した行である。
扱いは子プロセスの起動失敗の警告と同じで、`result=` の値は変わらない。

```text
codex-agent: warning=sandbox-build-failed count=<一致した回数>
codex-agent: sandbox-build-failed evidence: <起動したコマンドの行>
```

印の形は「既知の制約」の「サンドボックスの中の MSBuild がエラー文を出さずに止まる」にある。

最終報告は `codex exec` の `--output-last-message` から取る。
この選択肢がない版では標準出力の末尾で代用する。
失敗時は最終報告の位置にログの末尾 40 行が出る。
ログは `%USERPROFILE%\.claude\codex-agent\logs` に置く。
ログには Codex が読んだファイルの中身が入りうるため、共有される一時ディレクトリを避けてホーム配下に取る。
共有の場所では、他の利用者から読まれる余地と、先回りして置かれたシンボリックリンク越しに別のファイルを切り詰める余地が残る。
Codex の標準出力と最終報告の受け皿も、同じ置き場に `<実行 ID>.out` と `<実行 ID>.last` として置き、終了時に消す。
強制終了で終了時の後始末が動かなくても、実行 ID から特定して消せるようにするためである。
報告の写し `<実行 ID>.report` は、下の「完了を待つ」で使うため、ログと同じく終了後も残す。
スクリプトの起動時に 7 日より古い `.log`、`.last`、`.out`、`.report`、`.report.tmp` を削除する。

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
ただし終了コードが 0 でも、ログの `ERROR` を含む行(先頭にタイムスタンプが付く)に `code-mode host exited during handshake` があり、ツール実行の成功行(` succeeded in ` を含む行)が 1 行も無いときは `unavailable` とする。
Codex は依頼を果たせなくても自分の応答を返せば 0 で終わるため、ツール接続が一度も成立しなかった実行を終了コードだけでは見分けられないからである。
成功行が 1 行でもあれば `ok` のままにする。
接続が一度失敗した後に復旧して作業を終えた実行を、失敗として扱わないためである。
標準出力と標準エラーのどちらに通知されても判定できるようにするためである。
標準エラー側は、`WARNING` と `hook:` で始まる行を除いた後の内容だけを見る。
`429` は単語境界で照合し、ID や桁数の一致で誤検出しないようにしている。

**完了を待つ。**
起動した実行の完了は、`run=` の行の実行 ID を渡して待てる。

```bash
bash ~/.claude/tools/codex-agent.sh --wait <実行 ID>
```

待つ処理をラッパーに置くのは、定義が禁じた `sleep` と `until` のループを使わずに前面で待つためと、権限の許可規則が `bash ~/.claude/tools/codex-agent.sh` の先頭一致で判定されるためである。
メインセッションも同じ入口で、サブエージェントが残した実行の結果を取り出せる。
`--wait` はエージェント名、`-C`、`--effort` と併用できず、依頼文も読まない。

背景へ移った起動の実行 ID と、Codex の起動前に出る案内の行は、出力ファイルから次のコマンドで取り出せる。

```bash
bash ~/.claude/tools/codex-agent.sh --header-of "<出力ファイル>"
```

出力ファイルの先頭から `codex-agent: ` で始まる行が続く間(監査行、`prompt-file=`、`run=`、`log=`、`warning=concurrent-writer` の行)だけを、行末の CR を除いて標準出力へ出し、終了コード 0 で終わる。
最初の別の行から後は出さない。出す行は 50 行までとする。
ファイルが無い、読めない、出す行の中に `run=` の行が無いときは、`codex-agent: not-found header-of=<出力ファイル>` を出して終了コード 2 で終わる。
ラッパー役の 2 定義はフックで出力ファイルを読めないため、この入口で実行 ID と「進行中」の報告に添える行を取る。
先頭の案内の行に限るのは、Codex の報告や任意のファイルの中身を、この入口で読めないようにするためである。
`--header-of` は `--wait`、エージェント名、`-C`、`--effort` と併用できず、依頼文も読まない。

ラッパーは、Codex を起動した実行のたびに、標準出力に出したものと同じ内容をログ置き場の `<実行 ID>.report` にも書く。
一時ファイル `<実行 ID>.report.tmp` に書き、`result=` の行まで書いてから改名する。
`--wait` が書きかけの報告を読むと、途切れた報告を返すためである。
改名はログへ `result=` の行を書く前に行う。ログの最後の行が `result=` の行なら、報告も揃っている。
Codex を起動する前に止まる経路(終了コード 2、3、試験用フック)は、ログも報告も残さない。

呼び出し側は、どの状態かを標準出力の最後の行で判定する。
終了コードは補助にとどめる。
完了した実行では Codex の終了コードをそのまま返すため、ほかの状態に割り当てた値と重ならないとは言い切れないためである。

| 最後の行 | 状態 | 終了コード |
|---|---|---|
| `codex-agent: result=...` | 完了している。元の実行の標準出力をそのまま出す | 元の実行と同じ |
| `codex-agent: waiting run=<実行 ID>` | 上限までに完了しなかった。もう一度 `--wait` で待つ | 124 |
| `codex-agent: vanished run=<実行 ID>` | ラッパーが `result=` の行を書かずに消え、Codex 側のプロセスも残っていない | 1 |
| `codex-agent: orphaned run=<実行 ID> pids=<PID,...>` | ラッパーは消えたが、コマンドラインに `<実行 ID>.last` を含むプロセスが残っている | 1 |
| `codex-agent: unverified run=<実行 ID>` | ラッパーは消えたが、`--output-last-message` の無い版の Codex で起動した実行なので、Codex 側が残っているかを確かめられない | 1 |
| `codex-agent: not-found run=<実行 ID>` | 実行 ID のログが無い | 2 |

完了したときの終了コードは `result=` の行から戻す。
`ok` は 0、`rate-limited` と `unavailable` は 75、`failed exit=<code>` はその値で、`failed exit=75` だけは 1 である。
Codex 自身が返した 75 を 75 のまま返すと、実装担当が GPT 側の使用不能と取り違えて Claude 側で作り直すためである。
ログの最後の行が `result=` の行なのに報告が無い実行(報告を書かない版のラッパーの実行)では、その `result=` の行だけを出す。

1 回の待ちの上限は、`--wait` 自身の起動時刻から数えて 570 秒で、Bash ツールの上限(600 秒)の内側に収めている。
上限で終えるときは、最後の行の前に `codex-agent: last-log-line: <ログの最後の行>` を進行の目印として 1 行出す。
出力の無い待ちが何度も続くと、担当が止まったと判断して途中の報告でターンを終えやすいためである。
`vanished`、`orphaned`、`unverified` の前にも同じ行を出す。
`orphaned` のときは、Codex が作業ツリーに書き続けている可能性がある。「既知の制約」の止める手順 3 と 4 で止める。
`unverified` のときも、Codex が残っている可能性がある。`vanished` と取り違えて委譲をやり直すと、同じ作業ツリーに 2 つの Codex が書き込むおそれがあるので、止める手順 3 で作業ディレクトリから探して確かめる。
ラッパーは、`--output-last-message` の無い版の Codex で起動したときに、ログへ `codex-agent: note=no-output-last-message` の行を書く。`--wait` はこの行で `vanished` と `unverified` を分ける。

ラッパーの生死は、待ちの初めに 1 回だけ PowerShell で照合する。
PowerShell の起動に数秒かかるためである。
照合の条件は「既知の制約」の止める手順 2 と同じで、`run=` の行の PID のプロセスがコマンドラインに `codex-agent.sh` を含み、作成時刻が `started=` の前後 60 秒以内にあるときだけ、ラッパーが生きているとみなす。
PID の生死だけで判定しないのは、Windows で PID が再利用されるためである。
Codex 側のプロセスの生死では判定しない。Codex が先に落ちても、ラッパーが生きていれば `result=failed` を書くためである。
PowerShell が無い環境と、ログの 1 行目に `run=` の行が無い実行では照合できないため、`vanished`、`orphaned`、`unverified` を出さず、上限まで待って `waiting` を出す。

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
隔離した担当の流れは下の「隔離した worktree で実装担当を動かす流れ」に書く。
メインセッションが作業の途中で別の worktree へ移ると、元の worktree で隔離せずに動かしていたバックグラウンドのサブエージェントのコマンドが拒否されることがある(2026-09-16 に観測)。
実装担当を並行して動かすあいだは、メインセッションが worktree を移らないか、実装担当を隔離して起動する。

**隔離した worktree で実装担当を動かす流れ。**
隔離された worktree のファイルは、メインセッションが Edit や Write で書けない(ハーネスが別 worktree への書き込みを拒む)。
Bash からの git の操作は行える。
担当は Claude 側も GPT 側も、作業ツリーの変更と検証までで止める(「実装担当に許す操作」のとおり)。
ブランチの作成、コミット、main の取り込み、push は、報告を受けた後にメインセッションが担当の worktree で Bash の git(`git -C <担当の worktree>`)により行う。
競合の解消のようにファイルの編集が要る作業は、メインセッション自身の worktree に未コミットの変更が無いことを確かめてから、担当のブランチを基点に一時ブランチを作って行い、`git push origin HEAD:<ブランチ>` で反映する。
反映したら元のブランチに戻す。
その後に担当が同じ worktree で作業を続けるときは、先にメインセッションが origin の内容をその worktree に取り込む。
依頼文に「git」の語を入れない。
隔離の検査はコマンド文字列を見るため、ヒアドキュメントで渡す依頼文の説明の文でも `codex-agent.sh` の起動が拒否され、Claude 側で実装される(2026-09-16 に観測)。
依頼文をファイルで渡す形では、依頼文の語はコマンド文字列に入らない。
ただし、リダイレクト元がスクラッチパッド(worktree の外)でも隔離の検査を通るかは確かめていない。
検査が部分一致かは確かめていないため、`GitHub` や `.gitignore` のように `git` を含む語も避ける。
担当はコミットや push を行わないので、それらを依頼文に書く必要も無い。
`codex-agent.sh` の呼び出しは定義にある 1 行の形のまま使う。
隔離の検査は、git の語に限らず、実行時に値を組み立てるような複雑なコマンドも拒む。
担当自身の Bash も同じ検査を受けるため、隔離は担当の作業そのものにも摩擦を足す。
依頼文の語とは関係なく、`bash <スクリプト>` の形の起動そのものが、次の文言で拒否されることもある。

```text
This agent is isolated in the worktree <パス>, but this command runs bash in a plain command; what it reads or is handed as shell text cannot be shown not to run git.
```

これは、依頼文の語が原因の拒否(文言に `feeds bash text naming git` を含む)とは別のもので、「依頼文に git の語を入れない」規則では防げない。
2026-09-14 から 09-17 のあいだに 6 件観測し、09-18 以降は記録に無い。Claude Code の版で検査の判定が変わった可能性があるが、確かめていない。
拒否を受けた担当の動きは、定義の「Bash の実行そのものが権限判定で拒否された場合」の規則で決まり、担当によって違う。
`impl-hard`、`impl-light`、`impl-standard` は Claude 側で実装し、報告の冒頭に「Codex の呼び出しが権限判定で拒否されたため Claude 側で実装した」と書いて拒否の文言を添える。
`codex-subagent` は文言をそのまま報告して止まり、自分では作業しない。
前者の報告を受けたメインセッションは、Claude 側で済んだ変更を通常どおり監査する。未着手と取り違えて委譲し直すと、済んだ変更に同じ作業を重ねるためである。
後者の報告を受けたときは、並行が本当に要るかを見直す。
要らなければ、隔離せずに自分の worktree で直列に委譲し直す。要るなら、隔離の代わりに担当ごとの別 worktree を用意して委譲する。
どちらの場合も、同じ作業を続けて隔離した担当に回すかは、この見直しで決める。
起動の形を変えて検査を避ける工夫は採らない。検査の判定がまた変われば、同じく拒否されうるためである。
Codex は、隔離の有無にかかわらず、メイン以外の worktree の git ディレクトリ(元リポジトリの `.git/worktrees/<名前>`)に書けない。
担当がコミットしない流れでは、この制約に当たらない。
隔離の制約を避けるために、依頼文で Codex への委譲を止めて Claude 側で完走させる形は採らない。
委譲を止める指定は「実装担当に許す操作」の形式に限る。
計画に沿って PR をスタック式に積む連続対応は、隔離せず、メインセッションの worktree で 1 件ずつ直列に委譲する形を推奨とする。
上の制約のどれにも当たらないためである。
並行が要るときだけ、担当ごとに別 worktree(隔離を含む)を使う。

**制約の回避策をメモリーに残すとき。**
ツールや運用の制約への回避策をプロジェクトのメモリーに書くときは、原因となる制約と、それを扱う Issue の番号を添える。
Issue が閉じたら、その記述を更新するか消す。
回避策だけが成功例として残ると、制約が解消した後もセッションの判断を縛り続けるためである。

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
- それ以外は、実行中か、強制終了で後始末が動かずに残った実行である。`--wait` と同じプロセスの照合(「完了を待つ」の `vanished` と `orphaned` の判定)で見分ける。照合に使うラッパーの PID と `started=` は、目印の 1 行目から読み、読めなければ相手のログの 1 行目から読む。
  - ラッパー本人も、コマンドラインに `<相手の実行 ID>.last` を含むプロセスも残っていなければ、強制終了で残った目印として消す。標準出力には何も出さず、自分のログにだけ `codex-agent: note=stale-run-marker-removed run=<相手の実行 ID> log=<相手のログのパス>` の行を書く。
  - どちらかが残っている場合と、照合できない場合は、目印を消さず、標準出力とログに `codex-agent: warning=concurrent-writer run=<相手の実行 ID> log=<相手のログのパス>` の行を出す。照合できないのは、PowerShell が無い場合、PID と `started=` が読めない場合、相手のログに `codex-agent: note=no-output-last-message` の行がある場合である。最後の場合は、Codex のコマンドラインに `.last` が載らず、Codex が残っているかを確かめられないためである(`--wait` の `unverified` と同じ扱い)。

警告の行は、同じ worktree に書き込み可能な別の実行が残っている可能性を示す。
実行は止めない。
警告を受けたメインセッションは、行が示すログの最後の行を読み、相手が実行中であれば、同じ worktree への書き込みが重なっていないかを確かめる。
警告の行と `note=` の行は失敗の判定(利用上限などの語の照合)の対象に含めない。実行 ID やパスの数字が `429` に一致しうるためである。
照合の PowerShell の起動には数秒かかるが、起動するのはログが `result=` の行で終わっていない目印ごとに 1 回だけである。

警告は観測の補助であり、次の点に限界がある。

- メインセッションの編集はラッパーを通らないため、目印に現れない。GPT 側への委譲とメインセッションの編集の重なりは検出できない。
- 照合できない場合(PowerShell が無い環境、`note=no-output-last-message` の行がある実行)は、強制終了で残った目印を実行中の目印と区別できない。止める手順の最後で目印を消さないと、警告が出続ける。
- 2 つの起動がほぼ同時に始まると、互いの目印を置く前に調べ終え、警告が出ないことがある。

警告から排他(重なりを見つけたら起動しない形)へ上げるのは、再測定で同じ worktree の並行が残っており、かつ強制終了で残った目印をプロセスの照合で正しく見分けられると実運用で確かめた場合に限る。
照合できない目印が残る環境で排他にすると、残った目印 1 つで、その worktree の委譲がすべて止まるためである。

**Codex が書き込めるのは `-C` で指定した作業ディレクトリの配下だけである。**
`workspace-write` の書き込み範囲は作業ディレクトリに限られる。
作業ディレクトリの外のファイルは読める。
複数のプロジェクトにまたがる変更を委譲するときは、依頼文の作業ディレクトリに共通の親ディレクトリを指定する。

**子プロセスを起こす検証は `spawn EPERM` で失敗する。**
Windows の `unelevated` サンドボックスでは、Codex が起動したコマンド自体は動くが、そのコマンドがさらに子プロセスを起こすと `EPERM` になる。
`npm test` や `npx vitest` のように、テストランナーやビルドが子プロセスを起こす検証はこの形で失敗する。
次のコマンドで、モデルを呼ばずに再現できる(`nested.js` は `child_process.spawnSync` で `node -e 0` と `cmd /c exit 0` を起動するスクリプト)。

```bash
CODEX_HOME="$USERPROFILE/.codex-subagent" codex sandbox -P :workspace -C <作業ディレクトリ> -- node nested.js
```

Codex の文書によると、`[windows] sandbox` の選択肢は `elevated` と `unelevated` だけで、子プロセスの起動だけを許す設定は無い。
両方の認証ホームの `config.toml` は `unelevated` である。
`elevated` で `EPERM` が消えるかは確かめていない。確かめるには管理者の承認を伴う導入が要る。
調べた内容の詳細は Issue #53 のコメントにある。

Codex はこの失敗を最終報告の文面に書くだけで、終了コード 0 で返す。
そこでラッパーは、Codex の経過(標準エラー)に子プロセスの起動失敗の印が出た行を数え、1 行以上あれば警告の行と最初の根拠の行を出す(「フォールバックの条件と終了コード」の標準出力の形)。
印は次の 3 つである。

- `spawn EPERM`：行頭と行末に空白が付いてもよい `Error: spawn EPERM` または `Execution failed: Error: spawn EPERM` の行を、直後の `at ChildProcess.spawn (` で始まる行と組にして数える。
  語だけで照合すると、Codex が読んだ文書の引用にも一致するためである。
- `exited -1073741502 in`：終了コード 0xC0000142。Codex がツールの完了を書く行(行頭の空白に続く `exited -1073741502 in <時間>`)に限って照合する。数値だけで照合すると、この値を書いた文書を Codex が読んだだけで一致するためである。
- MSYS2 のランタイムの異常終了：`*** fatal error - CreateFileMapping` と `Win32 error 5` を同じ行に含む行を数える。テストの出力に埋め込まれて行頭が変わるため、行の形ではなく 2 つの語の組で照合する。

3 つ目は、Git for Windows の `sh.exe`(`git push` などが内部で起動する)がサンドボックスの中で起動した直後に止まる形である。
入れ子でなくても、Codex が `sh.exe` を直接起動するだけで次の行を出して終わる。

```text
      0 [main] sh (<PID>) D:\Program Files\Git\usr\bin\sh.exe: *** fatal error - CreateFileMapping S-1-5-21-<SID>.1, Win32 error 5.  Terminating.
```

次のコマンドで、モデルを呼ばずに再現できる(`sh.exe` のパスは Git for Windows の導入先に合わせる)。

```bash
CODEX_HOME="$USERPROFILE/.codex-subagent" codex sandbox -P :workspace -C <作業ディレクトリ> -- "<Git の導入先>/usr/bin/sh.exe" -c "echo ok"
```

この行は Windows アカウントの SID を含むので、ラッパーは根拠の行に出すとき SID を `S-1-5-21-<SID>` に置き換える。
報告の行は PR や Issue へ写されうるためである。

最終報告(標準出力)の本文は数えない。
最終報告が検証の失敗を文章で引用しても、二重に数えないためである。

`impl-hard`、`impl-light`、`impl-standard` は、終了コード 0 で警告の行があるか、最終報告が依頼文の検証を実行できなかったと述べている場合に、依頼文の検証コマンドだけを Claude 側で実行し、結果を報告に添える。
コードは変えず、検証が失敗しても直さない。
GPT 側の変更に Claude 側の修正が混ざると、メインセッションがどちらの変更かを見分けられなくなるためである。
報告の冒頭には、検証を Claude 側で実行したことを書く。
`codex-review` と `codex-subagent` は標準出力の全体をそのまま返すので、警告の行がそのまま報告に含まれる。検証は実行し直さない。

**サンドボックスの中の MSBuild がエラー文を出さずに止まる。**
worktree の中の CMake のビルド(`cmake --build build --config Debug`)を Codex から実行すると、MSBuild が `Checking File Globs` を出した直後に、エラー文を出さずに終了コード 1 で終わることがある。
同じコマンドを Claude 側で実行すると通る。
同じ構成でも、別の worktree の実行ではこの段階を通過して各プロジェクトのビルドへ進んだ例がある。
原因は特定していない。
作業ディレクトリの外の読み取りは `codex sandbox` で通ることを確かめたので、外の取得物(`FETCHCONTENT_SOURCE_DIR` で指す親リポジトリの `build/_deps` など)を読めないことは原因ではない。
候補は、上の 2 つの制約(作業ディレクトリの外への書き込みと、入れ子の子プロセスの起動)である。

ラッパーは、Codex の経過で次の並びを見つけた回を数え、警告の行と根拠の行を出す(「フォールバックの条件と終了コード」の標準出力の形)。

- Codex がツールの完了を書く行が `exited <0 以外> in <時間>` である。
- その後、空行と `MSBuild` で始まる行を除いた最初の行が、字下げした `Checking File Globs` である。
- さらにその次の空でない行が無いか、Codex の経過の区切りの行(`exec`、`codex`、`user`、`apply patch`、`tokens used`、`diff --git ` で始まる行)である。ビルドの出力が `Checking File Globs` で終わったことを示す。字下げの無いエラー文(`CMake Error at ...` など)が続いた失敗は数えない。

根拠の行には、`exited` の直前にある、Codex がコマンドを起動した行を出す。
GPT 側の `impl-hard`、`impl-light`、`impl-standard` の定義は、サンドボックスの制約による検証の失敗を実装の失敗や止まって報告する条件として扱わず、実装を最後まで進めて「検証を実行できなかった」と報告するよう指示している。
Claude 側の定義は、この警告の行があれば、子プロセスの起動失敗と同じく検証だけを Claude 側で実行する。
そのため依頼文では、ビルドの失敗を止まって報告する条件に書かなくてよい。

**Claude Code 2.1.281 では、呼び出し側が担当を前面で起動すると、担当がターンを終えた時点で背景の Bash と Monitor の追跡が切れる。**
出力ファイルの末尾には `[killed]` が付くが、Codex とラッパーは動き続け、Codex の完了後にラッパーは `result=` の行を出力ファイルへ書く。
呼び出し側が担当を背景で起動した場合は、Codex の完了後に担当が再開する。
再現条件と確認結果は Issue #74 のコメントにある。
対策として、5 定義はターンを終えず、`--wait` を前面で実行して Codex の完了まで待つ。

**1 回の委譲は Bash ツールの上限である 10 分に収める。**
Claude Code の版によって、Bash ツールは長時間のコマンドをバックグラウンドへ移すか、上限で打ち切る。
バックグラウンドへ移った場合、担当は実行 ID を取り、次のコマンドを前面で実行する。
`impl-hard`、`impl-light`、`impl-standard` は、出力ファイルの `codex-agent: run=` の行から実行 ID を取る。
`codex-review` と `codex-subagent` は、フックで出力ファイルを読めないため、`--header-of` で取る(「ラッパー役の定義の道具を絞る」)。

```bash
bash ~/.claude/tools/codex-agent.sh --wait <実行 ID>
```

Bash ツールの `timeout` には 600000 を指定し、`run_in_background` は指定しない。
`--wait` の最後の行が `waiting` なら、同じコマンドを繰り返す。
最後の行が `result=` なら、その出力は元の実行の標準出力と同じなので、元の実行の結果として扱う。
`vanished`、`orphaned`、`unverified`、`not-found` のいずれかなら、最後の数行を添えて報告し、Claude 側で作り直したり委譲をやり直したりせず停止する。
`waiting` の間も含め、自分で `sleep` や `until` のループを回したり、プロセスの一覧を調べたり、ファイルを探し回ったりしない。
中断の指示を受けたときなど、完了前にターンを終えざるを得ない場合の報告形式は、5 定義の「実行が長引いたとき」に従う。
状態を確認するときは、出力ファイルを読む代わりに `--wait` を前面で実行し、最後の行で判定する。
`--wait` の状態と終了コードの契約は「完了を待つ」にある。

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
   `--output-last-message` の無い版の Codex で起動した実行では、`.last` がコマンドラインに載らない(ログに `codex-agent: note=no-output-last-message` の行がある)。その場合は、コマンドラインに `exec` と作業ディレクトリ(監査行の `workdir=` の値)を含むプロセスを候補として探し、別の実行でないことを人が確かめてから止める。
4. 止めたあと、ログ置き場(`%USERPROFILE%\.claude\codex-agent\logs`)の `<実行 ID>.last`、`<実行 ID>.out`、`<実行 ID>.report.tmp` を消す。ログ本体の `<実行 ID>.log` は残す。
   強制終了ではラッパーの終了時の後始末が動かないため、これらが残る。
   消さなくても、次にラッパーを起動したときに 7 日より古いものは消える。
   書き込み可能な定義の実行であれば、作業ディレクトリの目印 `<git ディレクトリ>/codex-agent/runs/<実行 ID>.run` も消す。
   照合できる環境では、消さなくても、同じ worktree で次に書き込み可能な起動をしたときに、ラッパーも Codex も残っていないことを確かめて目印を消す。
   照合できない場合(PowerShell が無い環境、`note=no-output-last-message` の行がある実行)は、消さないと、同じ worktree で書き込み可能な起動をするたびに警告の行が出続ける。ログが 7 日を過ぎて消えれば、次の起動が目印を消す。

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
配布は、ラッパーとフック(3)を定義(1)より先に行う。
定義を先に配ると、フックのスクリプトが無いあいだはラッパー役の 2 定義の Bash と Write がすべて拒否され、`--header-of` を持たない古いラッパーでは実行 ID を取れないためである。

1. `.claude/agents/` の 5 定義を `%USERPROFILE%\.claude\agents\` に置き換える。
2. `.claude/gpt-agents/` の 5 定義を `%USERPROFILE%\.claude\gpt-agents\` にコピーする。
3. `tools/codex-agent.sh` と `tools/codex-agent-hook.js` を `%USERPROFILE%\.claude\tools\` にコピーする。

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
