# 設定コンソール(Windows GUI)の設計

bridge が扱う 5 定義(`impl-hard`、`impl-standard`、`impl-light`、`codex-review`、`codex-subagent`)の定義ファイルを Windows の GUI から書き換えるための設定コンソールの設計である。
サブエージェントタブとレビューと実装補助タブは実装済みである。
この文書は両タブを合わせた仕様と制約を記載し、使い方は [gui.md](gui.md) に従う。
末尾の「実装の段階」には、実装時の変更範囲と検証方法を記載している。

## 目的と範囲

設定コンソールは、次の 4 つを GUI から行えるようにする。

- GPT 系サブエージェント経路(`impl-hard`、`impl-standard`、`impl-light` が Codex へ委譲する経路)の有効と無効を切り替える。
- hard、standard、light の各区分で使う Claude 側のモデルと effort をプルダウンで選ぶ。
- hard、standard、light の各区分で使う GPT 側のモデルと effort をプルダウンで選ぶ。GPT モデルの選択肢には「(未設定)」を含み、選ぶとその区分は GPT 側を使わない。
- `codex-review` と `codex-subagent` が Codex を起動するときのモデルと effort をプルダウンで選ぶ。

画面は 2 つのタブに分かれる。
**サブエージェントタブ**は前者 3 つ(`impl-hard`、`impl-standard`、`impl-light`)を、**レビューと実装補助タブ**は後者 1 つ(`codex-review`、`codex-subagent`)を扱う。
タブを分けるのは、2 群の定義で「GPT 側を使わない」状態の意味が違うためである。
前者 3 定義は GPT 側が使えなければ Claude 側にフォールバックするが、後者 2 定義は明示的に Codex へ依頼する定義であり、フォールバックせずに失敗する。
同じ表に並べると、前者向けの「(未設定)」や有効無効の切替が後者にも効くように見える。

設定コンソールは Codex も Claude Code も起動しない。
Claude Code と `tools/codex-agent.sh` がセッション開始時や呼び出し時に読む定義ファイルのフロントマターだけを書き換える設定エディタである。

対象はユーザ定義側(`%USERPROFILE%\.claude\agents\` と `%USERPROFILE%\.claude\gpt-agents\`)だけとする。
利用先プロジェクトの `.claude\` 配下は対象にしない。
初版を小さく保つためであり、後から対象フォルダの選択を足しても、書き換えの規則は変わらない。

## 実装言語と配布形態

C# と Windows Forms で書き、ターゲットフレームワークは .NET Framework 4.8 とする。
Windows 10 1903 以降と Windows 11 には 4.8 が同梱されているため、ランタイムを別途導入せずに exe 単体で起動する。
画面はタブ 2 枚にコンボボックスとチェックボックスが二十個ほど並ぶ静的なフォームであり、デザイナを使わずコードで組み立てる。

ビルドは .NET SDK の `dotnet build` で行う。
`net48` をターゲットにすると SDK が参照アセンブリの NuGet パッケージを自動で取得するため、Visual Studio や Developer Pack は要らない。

## 書き換える項目

定義ファイルは Claude 側(`.claude/agents/<name>.md`)と GPT 側(`.claude/gpt-agents/<name>.md`)の 2 層に分かれる。
二層の役割は [gpt-agents.md](gpt-agents.md) の「定義ファイルの二層」を参照。
設定コンソールが書き換えるキーは次の表のとおりである。

| タブ | 区分 | Claude 側(`agents/<name>.md`) | GPT 側(`gpt-agents/<name>.md`) |
|---|---|---|---|
| サブエージェント | hard(`impl-hard`) | `model`、`effort` | `codex_home`、`codex_enabled`、`codex_model`、`codex_reasoning_effort` |
| サブエージェント | standard(`impl-standard`) | `model`、`effort`(フォールバック時に使う) | 同上 |
| サブエージェント | light(`impl-light`) | 同上 | 同上 |
| レビューと実装補助 | レビュー(`codex-review`) | 書き換えない | `codex_model`、`codex_reasoning_effort` |
| レビューと実装補助 | 実装補助(`codex-subagent`) | 書き換えない | 同上 |

`codex_sandbox` は表示するだけで編集させない。
サンドボックスを GUI から緩められると、[CLAUDE.md](../CLAUDE.md) の「権限の固定」の原則に反するためである。

### レビューと実装補助タブで書き換えない項目

`codex-review` と `codex-subagent` では、GPT 側の `codex_model` と `codex_reasoning_effort` だけを書き換える。
他の項目を対象から外す理由は次のとおりである。

- **Claude 側の `model`**：この 2 定義の Claude 側は、依頼文を `tools/codex-agent.sh` へ転送するだけの薄い包みである。値は Claude Code のモデル別名(`haiku`)であり、`effort` キーも持たない。転送だけの定義でモデルを選ばせても結果は変わらないため、読み込みも表示もしない。
- **`codex_home`**：[CLAUDE.md](../CLAUDE.md) の「認証ホームの配置」が、`codex-review` は `~/.codex`、`codex-subagent` は `~/.codex-subagent` と定めている。画面から変えられると「用途固定の原則」に反するため、`codex_sandbox` と同じく表示だけにする。サブエージェントタブの `codex_home` の選択は、この 2 定義には及ばない。
- **`codex_enabled`**：この 2 定義は明示的に Codex へ依頼する定義であり、Claude 側が代行すると依頼の意味が変わる。サブエージェントタブのトグルはこの 2 定義に書かず、`tools/codex-agent.sh` がこの 2 定義で `codex_enabled` を読む挙動も変えない。
- **「(未設定)」**：`codex_model` が空だとスクリプトは終了コード 3 で止まり、この 2 定義はフォールバックしないので依頼がそのまま失敗する。GPT モデルの選択肢に「(未設定)」を置かず、保存前の検証で空を拒む。

### Claude 側の `model` と `effort`

`impl-light` と `impl-standard` の Claude 側の値は、GPT 側が未導入、無効化、またはレートリミットで使えないときのフォールバックで使われる。
`impl-hard` の Claude 側の値は、GPT 側が未設定、未導入、無効化、またはレートリミットで使えないときに使われる。出荷時の GPT 側定義には `codex_model` を書かないため、`impl-hard` は既定ではこの値だけで動く。
プルダウンにこの違いを示すため、standard と light の Claude 側の列見出しに「(フォールバック時)」を添える。

### GPT 経路の有効と無効

現在の `tools/codex-agent.sh` には無効化の概念がない。
`impl-light` と `impl-standard` が Claude 側にフォールバックするのは、スクリプトが終了コード 3(GPT 側が未導入)で止まったときだけである。
GPT 側定義を別名に退避すれば疑似的に無効化できるが、設定コンソールが途中で異常終了すると定義が退避されたまま残る。
そこで、GPT 側定義のフロントマターにキーを 1 つ足し、スクリプトがそれを解釈する。

- **codex_enabled**：`true` または `false`。省略時は `true`。`false` のときスクリプトは Codex を起動せず、終了コード 3 で止まる。

終了コード 3 の意味を「GPT 側が未導入、無効化、または未設定である」に広げる。
未設定とは、GPT 側定義の `codex_model` が無いか空であることを指す。
`codex_model` の未設定は区分ごとに GPT 経路の有無を切り替える手段であり、`codex_enabled`(3 定義をまとめて止める切替)とは役割が異なる。
`impl-hard` は出荷時のフロントマターに `codex_model` を書いていないため、この経路で既定では Claude 側にフォールバックする。
`impl-hard`、`impl-light`、`impl-standard` は既存の手順どおり Claude 側で実装し、報告の冒頭を「GPT 側が未導入、無効化、または未設定のため Claude 側で実装した」に直す。
設定コンソールのトグルは、`impl-hard`、`impl-light`、`impl-standard` の GPT 側定義に同じ値を書く。

`codex_enabled` の値は `true` と `false` だけを受け付ける。
それ以外の値はスクリプトが終了コード 2(定義の不備)で止める。
キーを書いて値を空にした場合も、省略とみなさず終了コード 2 で止める。
既定が「Codex を起動する」側に倒れるため、書きかけの指定を有効と読ませないためである。
省略時に `true` とするのは、既存の定義ファイルを書き換えずに動かし続けるためである。

### 認証ホームの切り替え

サブエージェントタブの `codex_home` は、`%USERPROFILE%` 直下に実在する `.codex` で始まるディレクトリから選ぶ。
値は `~/.codex-subagent` の形で扱い、任意のパスは入力させない。
このタブの役割は `impl-hard`、`impl-light`、`impl-standard` の設定を切り替えることであり、どのホームをどの用途に割り当てるかは縛らない。
用途の割り当ては [CLAUDE.md](../CLAUDE.md) の「用途固定の原則」が定める。
レビューと実装補助タブでは `codex_home` を定義ごとに表示するだけで、選ばせない(「レビューと実装補助タブで書き換えない項目」を参照)。

選んだ値は、`codex_enabled` と同じく `impl-hard`、`impl-light`、`impl-standard` の 3 つへ書く。
3 定義を 1 つの設定として扱うためである。
3 定義の値が食い違っているときは `impl-light` の値を選択中として表示し、食い違いを画面に示す。
保存すると選択中の値で 3 つが揃う。

現在の値が一覧に無い場合(ディレクトリが存在しない、`$USERPROFILE` 形式など `~/` 以外の書き方、未設定)は、その値に注記を添えて選択肢の先頭に足し、選択状態にする。
選び直さずに保存した場合、その値は書き換えない。
設定コンソールが組み立てていない値を、別の形へ勝手に直さないためである。

選択肢に並べるのは、ディレクトリ名が英数字と `.`、`_`、`-` だけで成り立つものに限る。
値は二重引用符で囲んで書くため、`\` と `"` を含む名前は `fm_get` の読みとずれる(「ファイル書き換えの規則」を参照)。

## 画面

ウィンドウは 1 枚で、リサイズしない。
タブの切り替えで大きさが変わらないよう、ウィンドウの大きさは 2 つのタブの内容のうち大きいほうに合わせる。
対象と再読込、常時表示する注意書き、保存状態、保存と閉じるのボタンはタブの外に置き、どちらのタブでも同じ位置に見せる。

```text
┌ claude-codex-bridge 設定コンソール ───────────────────────────────────────┐
│ 対象: C:\Users\<user>\.claude                                    [再読込]  │
│ ┌ サブエージェント ┐┌ レビューと実装補助 ┐                                 │
│ │                                                                        │ │
│ │ [x] GPT 系サブエージェント経路を有効にする (impl-hard / impl-standard / impl-light) │
│ │                                                                        │ │
│ │ 区分      Claude モデル        effort     GPT モデル        effort     │ │
│ │ hard      [claude-opus-5-5  v] [high   v]  [(未設定)      v]           │ │
│ │ standard  [claude-opus-5-5  v] [medium v]  [gpt-6-luna    v] [max    v]│ │
│ │ light     [claude-sonnet-5  v] [medium v]  [gpt-6-luna    v] [xhigh  v]│ │
│ │                                                                        │ │
│ │ codex_home: [~/.codex-subagent v]  codex_sandbox: workspace-write      │ │
│ │ codex --version: 0.xx.x                                                │ │
│ │ GPT モデル一覧: codex debug models から取得                            │ │
│ └────────────────────────────────────────────────────────────────────────┘ │
│ 保存した値は次の委譲から効く(再起動が要る条件は setup.md 参照)            │
│ 変更はありません。                                                         │
│                                                            [保存] [閉じる] │
└───────────────────────────────────────────────────────────────────────────┘
```

```text
│ ┌ サブエージェント ┐┌ レビューと実装補助 ┐                                 │
│ │                                                                        │ │
│ │ 定義            GPT モデル        effort     codex_home         codex_sandbox │
│ │ codex-review    [gpt-6-sol     v] [medium v] ~/.codex           read-only     │
│ │ codex-subagent  [gpt-6-sol     v] [medium v] ~/.codex-subagent  workspace-write │
│ │                                                                        │ │
│ │ codex --version: ~/.codex 0.xx.x / ~/.codex-subagent 0.xx.x            │ │
│ │ GPT モデル一覧: ~/.codex は codex debug models から取得 / ~/.codex-subagent は既定値 │
│ └────────────────────────────────────────────────────────────────────────┘ │
```

- **対象**：`%USERPROFILE%\.claude` を固定で表示する。定義ファイルが無いときの扱いはタブごとに分ける(「タブごとの保存可否」を参照)。
- **トグル**：チェックを外すと GPT モデルと effort の列を無効表示(灰色)にする。hard を含む 3 行すべてが対象である。値は保持し、再びチェックを入れると元に戻る。レビューと実装補助タブには効かない。
- **プルダウン**：モデルと effort は入力可のコンボボックス(`DropDownStyle = DropDown`)にする。選択肢に無いモデル名を将来使えるようにするためである。一覧はスクロールさせずに全件を並べる(上限 20 件)。スクロールすると、選択中の値より上の候補が隠れて選択肢に無いように見えるためである。サブエージェントタブの GPT モデルの選択肢には、`choices.json` や `codex debug models` の内容によらず先頭に固定の「(未設定)」を加える。選ぶとその区分は `codex_model` を空にし、その行の GPT effort を無効にする。レビューと実装補助タブの GPT モデルには「(未設定)」を加えない。認証ホームだけは選ぶだけにする(「認証ホームの切り替え」を参照)。
- **サブエージェントタブの状態行**：`impl-light` の GPT 側定義から `codex_home` と `codex_sandbox` を読む。`codex_home` は選べるコンボボックス(`DropDownStyle = DropDownList`)にし、`codex_sandbox` は文字列で表示する。`codex --version` と `codex debug models` は、選択中の認証ホームを `CODEX_HOME` に渡して実行する。ホームを切り替えると引き直し、引いた結果はホームごとに持っておく。取得の間は「確認中...」と「取得中...」を表示し、結果が返った時点でそのホームがまだ選ばれているときだけ画面へ反映する。`codex` が無ければ「見つからない」と表示する。`GPT モデル一覧` は、GPT 側のモデルと effort の選択肢が `codex debug models` の目録と既定値のどちらから来ているかを表示する。ログイン状態(`codex login status`)は表示しない。
- **レビューと実装補助タブの状態行**：各定義の GPT 側定義から読んだ `codex_home` と `codex_sandbox` を、その行に文字列で表示する。`codex --version` と `codex debug models` は、2 定義の `codex_home` それぞれについて起動時に引き、結果はサブエージェントタブと同じホーム別の控えに入れる。2 定義の `codex_home` が同じなら 1 回だけ引く。各行の GPT モデルと effort の選択肢は、その行の `codex_home` で引いた目録から作る。目録が引けなければ既定値を使う。状態行は 2 つのホームの結果を並べて表示し、同じホームは 1 つにまとめる。長い表示は省略せず折り返し、必要な高さを確保する。
- **タブごとの保存可否**：サブエージェントタブは Claude 側の定義 3 件と GPT 側の定義 3 件、レビューと実装補助タブは GPT 側の定義 2 件を対象にする。あるタブの対象ファイルが 1 つでも無いか読めなければ、そのタブの入力を無効にする。タブ内には赤字で「このタブは保存できない。」と表示し、ファイルが無い場合は `docs/setup.md` の配置手順と見つからないファイル名を示す。読めない場合は該当するファイル名を示す。もう一方のタブは影響を受けず、対象が揃っていれば単独で保存できる。片方の欠落でもう一方を止めると、`codex-review` の定義を置いていない利用者がサブエージェントの設定を変えられなくなるためである。
- **保存**：対象ファイルが揃ったタブに未保存の変更か修復待ちがあるときだけボタンを押せる。修復待ちとは、`codex_enabled` の不正値と、選択中の値で揃えられる `codex_home` の食い違いである(どちらもサブエージェントタブだけにある)。保存前に入力値を検証し、対象が揃ったタブの変更ファイルだけを書き換える。対象ファイルが欠けているタブは書き換えの対象から外す。レビューと実装補助タブの `codex-review` と `codex-subagent` は `codex_model` が空だと検証エラーになる。状態行は保存状態を示し、検証エラーと保存失敗は赤色で示す。変更項目の列挙と検証エラーには、どのタブの項目かが分かるよう定義名を添える。照合に失敗した場合は保存せず、入力を保持する再読込を促すダイアログを出す。
- **再読込**：8 ファイルを読み直す。未保存の変更があるときは変更項目を列挙し、「はい」で編集した項目だけ入力中の値を残して読み直し(外部でも変わっていた項目は入力中の値を優先して知らせる)、「いいえ」で入力を捨てて読み直し、「キャンセル」で何もしない。既定ボタンは「キャンセル」とする。未保存の変更がないときは確認ダイアログを出さずに読み直す。
- **閉じる**：未保存の変更があるときは変更項目を列挙した確認ダイアログを出し、保存して閉じるか、変更を破棄して閉じるか、キャンセルするかを選ぶ。

Claude Code はエージェント定義をセッション開始時に読み込むため、保存しても起動中のセッションには反映されない。
この注意は常時表示する。

### プルダウンの選択肢

選択肢の既定値は exe に埋め込む。
exe と同じフォルダに `choices.json` があれば、それで既定値を丸ごと置き換える。
既定値は次のとおりである。

| 項目 | 選択肢 |
|---|---|
| Claude モデル | `claude-fable-5-1`、`claude-fable-5`、`claude-opus-5-5`、`claude-opus-5`、`claude-sonnet-5`、`claude-opus-4-8`、`claude-opus-4-7`、`claude-opus-4-6`、`claude-sonnet-4-6`、`claude-haiku-4-5` |
| Claude effort | 選ばれているモデルが受け付ける値。対応表に無いモデルでは `low`、`medium`、`high`、`xhigh`、`max` |
| GPT モデル | `codex debug models` から取得。取れなければ `gpt-6-astra`、`gpt-6-sol`、`gpt-6-luna`、`gpt-5.6-sol`、`gpt-5.6-terra`、`gpt-5.6-luna`、`gpt-5.5` |
| GPT effort | 選ばれているモデルが受け付ける値。取れなければ `low`、`medium`、`high`、`xhigh`、`max`、`ultra` |

GPT 側の 2 つは、起動時に `codex debug models` から取得できればそちらを使う。既定値はその控えである。
取得できる値のうち、`tools/codex-agent.sh` が受け付けない effort があってはならない。スクリプト側の許容値は目録に合わせて広げる。
GPT effort の許容値は 3 箇所にある。`tools/codex-agent.sh` の `validate_effort`、`ConsoleSettings` の `ValidGptEfforts`、`choices.default.json` の `gptEfforts` である。値を足すときは 3 箇所を同時に変える。
Claude 側のモデルには相当する取得手段が無い。Claude Code には非対話でモデル一覧を返すコマンドが無いためである。
そのため Claude 側は、モデルと effort の対応を `claudeModelEfforts` として設定に持つ。この項目は任意であり、無い場合は `claudeEfforts` の一覧をモデルによらず使う。
モデルを変えたとき、そのモデルが現在の effort を受け付けなければ、指定値以下で最も高い対応済みの値に変える。Claude Code 自身が同じ規則で落として実行するためである。

`choices.json` の形は次のとおりである。

```json
{
  "claudeModels": ["claude-opus-5-5", "claude-sonnet-5"],
  "claudeEfforts": ["low", "medium", "high", "xhigh", "max"],
  "claudeModelEfforts": [
    { "model": "claude-opus-5-5", "efforts": ["low", "medium", "high", "xhigh", "max"] }
  ],
  "gptModels": ["gpt-6-astra", "gpt-6-sol", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5"],
  "gptEfforts": ["low", "medium", "high", "xhigh", "max", "ultra"]
}
```

ファイルから読んだ現在値が選択肢に無い場合は、選択肢の先頭に追加して選択状態にする。
既存の設定を開いただけで値が変わることを防ぐためである。

## ファイル書き換えの規則

フロントマターの読み書きは画面から切り離した 1 クラス(`FrontMatterFile`)に集め、単体テストで固定する。
規則は `tools/codex-agent.sh` の読み方と一致させる。

- フロントマターは、1 行目の `---` から次の `---` の行までとする。1 行目が `---` でないファイルは、フロントマターが無いものとして読み込みを失敗させる。
- キーの値は `<キー>:` の後ろの空白を除いた部分とする。「空白 + `#`」以降は行内コメントとして扱い、値には含めない。値を囲む `"` または `'` は外す。二重引用符の中では `\\` を `\`、`\"` を `"` に戻す。
- `<キー>:` の後ろに行内コメントしか無い行(例: `codex_enabled:  # まだ決めていない`)は、値が空であるものとして読む。`tools/codex-agent.sh` の `fm_get` はこの行を `# まだ決めていない` という値として読むため、ここだけ読み方が異なる。設定コンソールは保存時に妥当な値を書き戻すため、この相違が残るのは手で書きかけた定義を開いたときだけである。
- 値を書き換えるときは、対象キーの行だけを置換する。行内コメントは残す(例: `codex_home: ~/.codex-subagent  # サブエージェント専用アカウント` の `#` 以降)。
- 値を書くときは常に二重引用符で囲む(`model: "claude-opus-5-5"`、`codex_enabled: "false"`)。引用符が無いと YAML は `true` や `123` を文字列以外として読み、文字列を期待する定義が壊れるためである。`tools/codex-agent.sh` の `fm_get` は外側の引用符を外して読むため、スクリプト側に変更は要らない。
- 値に含まれる `\` は `\\` に、`"` は `\"` にエスケープする。`fm_get` は引用符を外すだけでエスケープを戻さないため、この 2 文字を含む値を書くとスクリプトと読みがずれる。設定コンソールは値に使える文字を英数字と `.`、`_`、`-`、`/` に限っており、この 2 文字は入力できない。
- 読み込んだ値と書き込む値が等しい行には書かない。等しさは引用符を外した後の値で判定する。そのため、値を変えていない行は引用符の付かない元の形のまま残る。
- キーがフロントマターに無ければ、閉じの `---` の直前に `<キー>: <値>` の行を追加する。`codex_enabled` は既存の定義に無いため、この経路で追加される。
- `codex_model` を「(未設定)」にして保存するとき、キーが元から無い定義にはキーを追加しない。キーが元からある定義には `codex_model: ""` を書く。既存の定義ファイルを不必要に書き換えないためである。
- 本文(閉じの `---` より後ろ)には触れない。
- 改行コード(LF または CRLF)と BOM の有無は、読み込んだファイルのものを保つ。
- 書き込みは同じフォルダの一時ファイルに出してから `File.Replace` または移動で置き換える。途中で失敗しても元のファイルが壊れないようにするためである。
- 値を書き換えるとき、コロンと値の間に空白が無い行には空白を 1 つ補う。Claude 側の定義は Claude Code が YAML として読むため、`model:値` の形になると解釈できなくなるためである。
- 保存の直前に、読み込んだときの内容とファイルの現在の内容を照合する。異なっていれば保存せずに失敗させる。設定コンソールの外で加えられた変更を黙って上書きしないためである。
- 同じキーが複数行ある場合は、最初の行だけを対象にする。スクリプトが最初の一致だけを読むためである。

### 保存前の検証

次のいずれかに当たる場合は保存せず、理由をダイアログで示す。

- Claude 側の `model` または `effort` が空である。
- GPT 側の `codex_reasoning_effort` が `low`、`medium`、`high`、`xhigh`、`max`、`ultra` のいずれでもない。
- `codex-review` または `codex-subagent` の `codex_model` が空である。
- `model`、`effort`、`codex_model` に、英数字と `.`、`_`、`-`、`/` 以外の文字がある。

対象ファイルが欠けているタブの項目は検査しない。
そのタブは書き換えの対象から外れるためである。

`impl-hard`、`impl-standard`、`impl-light` の `codex_model` は、空でもエラーにしない。空はその区分で GPT 側を使わない設定として扱われ、トグルの有効・無効にかかわらず検査しない。
`codex-review` と `codex-subagent` の `codex_model` は空を拒む。
この 2 定義は空だと依頼が失敗するだけで、空にする用途が無いためである。
値は二重引用符で囲んで書くため、`true` や `123` のような YAML の予約語と数値もそのまま保存できる。
使える文字を絞るのは、`fm_get` がエスケープを戻さないためである。

`codex_home` の文字は検証しない。
書くのは選択肢から選んだ値だけであり、その選択肢を書ける文字だけで組み立てているためである。
ただし、書き込む場合は保存の手前で展開先のディレクトリがまだ実在するかを見る。
選択肢は読み込み時に列挙したものであり、その後に消えたホームを書くと、次のサブエージェント起動が認証ホーム不足で止まるためである。

入力を保持する再読込では、`codex_home` の外部変更を `impl-hard`、`impl-light`、`impl-standard` の定義ごとに見る。
画面には `impl-light` の値を代表として出すが、保存では選択値を 3 定義へ書くため、`impl-hard` や `impl-standard` 側だけの外部変更を知らせずに上書きしないようにする。

Claude 側の `effort` は Claude Code が解釈する値であり、設定コンソールは空でないことと使える文字だけを検査する。
モデルごとの対応表は選択肢を絞る補助であり、検査には使わない。対応表に無いモデルや、利用者が手で入れた値を拒まないためである。

## `tools/codex-agent.sh` の変更

フロントマターを読んだ後、他のキーの検証より前に `codex_enabled` を読む。

```bash
codex_enabled="$(fm_get codex_enabled)"
[ -n "$codex_enabled" ] || codex_enabled="true"
case "$codex_enabled" in
  true) ;;
  false) die_missing "GPT 側が無効化されている (codex_enabled: false): $def_file" ;;
  *) die "codex_enabled の値が不正である: $codex_enabled (true または false)" ;;
esac
```

`die_missing` は既存の終了コード 3 の経路である。
他のキーの検証より前に置く。
無効化されているときは、認証ホームが無くても、`codex_model` などが未設定でも止まらないようにするためである。
設定コンソールはトグルが無効のとき `codex_model` を検査しないため、その状態でも終了コード 3 で Claude 側へ渡す必要がある。

`codex_home` の読み方と展開はスクリプト側を変えない。
設定コンソールが書く値は、スクリプトがこれまでも受け付けてきた `~/<ディレクトリ名>` の形だけである。

あわせて `die_missing` に `codex-agent: result=failed exit=3` の出力を足す。
[gpt-agents.md](gpt-agents.md) は「スクリプトは末尾に結果の 1 行を出す」と定めているが、この経路だけが出していなかったためである。
既存の 2 経路(定義ファイルが無い、`codex` コマンドが PATH に無い)にもこの行が出るようになる。

あわせて次の文書と定義を直す。

- [gpt-agents.md](gpt-agents.md)：「フロントマターのキー」に `codex_enabled` を追加し、「フォールバックの条件と終了コード」の 3 の説明を「GPT 側が未導入、または無効化されている」に広げる。
- `.claude/agents/impl-light.md` と `.claude/agents/impl-standard.md`：終了コード 3 の説明と報告冒頭の文言を「未導入または無効化」に直す。
- [setup.md](setup.md)：設定コンソールの配置と使い方への参照を「設定に関する注意」の後ろに 1 段落で足す。

`.claude/gpt-agents/impl-light.md` と `impl-standard.md` には `codex_enabled` を書かない。
省略時が `true` であり、書かなくても現在の挙動が保たれるためである。

`codex_model` が無いか空の定義も、同じ `die_missing` の経路で終了コード 3 にする。
`codex_enabled` の判定より後、`codex_home` の検証より後に置く。
`impl-hard` の出荷時定義は `codex_model` を書かず、設定コンソールの「(未設定)」もこの状態を書くため、この経路が既定の Claude 側フォールバックになる。
設定コンソールは `codex_model` が空でも検証エラーにしないので、トグルの有効無効によらずこの経路で Claude 側へ渡せる。

## リポジトリ上の配置

```text
gui/
├─ CodexBridgeConsole.sln
├─ build.bat                         ダブルクリックで dist へ発行する
├─ start.bat                         exe が無ければビルドしてから起動する
├─ CodexBridgeConsole/
│   ├─ CodexBridgeConsole.csproj     net48、UseWindowsForms、OutputType=WinExe
│   ├─ app.manifest                  高 DPI 対応と対応 Windows の宣言
│   ├─ Program.cs                    エントリポイント
│   ├─ MainForm.cs                   画面の組み立てとイベント
│   ├─ SelectionPreservingComboBox.cs  大きさの変更で選択範囲が変わらないコンボボックス
│   ├─ ConsoleSettings.cs            8 ファイルの読み込み、検証、保存
│   ├─ FrontMatterFile.cs            フロントマターの読み書き
│   ├─ FrontMatterFileChangedException.cs  読み込み後の外部変更を表す例外
│   ├─ CodexModelCatalog.cs          codex debug models の目録
│   ├─ Choices.cs                    選択肢の既定値と choices.json の読み込み
│   └─ choices.default.json          埋め込みリソース
└─ CodexBridgeConsole.Tests/
    ├─ CodexBridgeConsole.Tests.csproj  net48、xunit
    ├─ FrontMatterFileTests.cs
    ├─ ConsoleSettingsTests.cs
    ├─ ChoicesTests.cs
    ├─ CodexModelCatalogTests.cs
    └─ TemporaryDirectory.cs            テスト用の一時フォルダ
```

ビルドと発行のコマンドは次のとおりである。

```bash
dotnet build gui/CodexBridgeConsole.sln -c Release
dotnet test gui/CodexBridgeConsole.sln -c Release
dotnet publish gui/CodexBridgeConsole/CodexBridgeConsole.csproj -c Release -o gui/dist
```

`gui/dist/` と `gui/**/bin/`、`gui/**/obj/` は `.gitignore` に追加する。
`build.bat` は上の発行と同じことをダブルクリックで行い、成功するとエクスプローラーで exe を示す。
`start.bat` は exe が無ければ `build.bat` を呼んでから起動する。
どちらも cmd.exe が読むため、表示する文は ASCII で書く([CLAUDE.md](../CLAUDE.md) の言語の例外)。
exe はリポジトリにコミットせず、必要なら GitHub Release に添付する。

## 実装の段階

3 段階に分け、段階ごとにブランチと PR を作る。
いずれも `impl-standard` に委譲できる粒度である。

### 段階 1：`codex_enabled` のスクリプト対応

- **変更対象**：`tools/codex-agent.sh`、`.claude/agents/impl-light.md`、`.claude/agents/impl-standard.md`、`docs/gpt-agents.md`
- **期待する結果**：GPT 側定義に `codex_enabled: false` を書くと、スクリプトが Codex を起動せず終了コード 3 と `codex-agent: result=failed exit=3` を返す。書かないときと `true` のときは従来どおり動く。不正値は終了コード 2 で止まる。
- **検証方法**：`.claude/gpt-agents/impl-light.md` を一時的に書き換えて次を実行し、終了コードを確認する。確認後は書き戻す。

```bash
bash tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-LUNA"; echo "exit=$?"
```

`codex_enabled: false` のときは Codex を起動しないため利用枠を消費しない。
`true` と省略時の確認は実モデルを起動するため、利用枠を消費する。

### 段階 2：フロントマター読み書きライブラリとテスト

- **変更対象**：`gui/` の新規作成(`FrontMatterFile.cs`、`Choices.cs`、テスト)、`.gitignore`
- **期待する結果**：「ファイル書き換えの規則」の各項目が単体テストで固定される。特に、行内コメントの保持、キー追加の位置、CRLF と BOM の保持、同名キーの重複時の扱いをテストに含める。
- **検証方法**：`dotnet test gui/CodexBridgeConsole.sln -c Release` が成功する。

### 段階 3：画面と文書

- **変更対象**：`MainForm.cs`、`Program.cs`、`docs/gui.md`(使い方)、`docs/setup.md` への参照追加、`README.md` のファイル一覧
- **期待する結果**：「画面」の節のとおりに動く exe が `dotnet publish` で得られる。実際のユーザ定義側の 5 ファイルに対して、トグルの切り替えとモデル、effort の変更が保存され、再読込で読み戻せる。
- **検証方法**：`dotnet build` と `dotnet publish` が成功する。発行した exe を起動し、値を変えて保存した後、対象ファイルの差分を `git diff --no-index` または目視で確認する。確認後は元の値に戻す。

### 追記：`impl-hard` の GPT 側定義への対応

段階 1〜3 の完了後、`impl-hard` にも GPT 側定義(`.claude/gpt-agents/impl-hard.md`)を追加し、サブエージェントタブの対象ファイルを 6 ファイル、GPT 側の区分を 3 つに広げた。
`impl-hard` の GPT モデルは既定で「(未設定)」であり、この状態では `codex_model` を書かず Claude 側だけで実装する。
この節より前の各節は、この対応後の仕様を記載している。

### 追記：レビューと実装補助タブの追加

`impl-hard` の対応後、`codex-review` と `codex-subagent` の GPT 側定義を対象に加え、画面を 2 つのタブに分けた。
変更は次の 3 段階に分けて行った。
この節より前の各節は、この対応後の仕様を記載している。

#### 段階 1：設定クラスの定義単位への一般化

- **変更対象**：`ConsoleSettings.cs`、`ConsoleSettingsTests.cs`
- **期待する結果**：対象ファイルの一覧に `gpt-agents/codex-review.md` と `gpt-agents/codex-subagent.md` を加え、読み込み、差分の列挙、検証、保存が定義単位で回る。欠落と読み込み失敗はタブ単位で集計し、片方のタブの欠落でもう片方の保存が止まらない。`codex_enabled` と `codex_home` の一括書き込みは `impl-hard`、`impl-standard`、`impl-light` の 3 定義に限られたままである。`codex-review` と `codex-subagent` の `codex_model` が空のときは検証エラーになる。既存のテストは変更せずに通る。
- **検証方法**：`dotnet test gui/CodexBridgeConsole.sln -c Release` が成功する。追加するテストには、片方のタブのファイルだけが無い状態での保存、2 定義の `codex_model` が空のときの検証エラー、2 定義の `codex_home` が `codex_enabled` の一括書き込みで変わらないことを含める。

#### 段階 2：画面のタブ化

- **変更対象**：`MainForm.cs`
- **期待する結果**：「画面」の節のとおりに 2 つのタブが表示される。レビューと実装補助タブの GPT モデルには「(未設定)」が無く、`codex_home` と `codex_sandbox` は文字列で表示される。2 定義の `codex_home` それぞれについて `codex --version` と `codex debug models` を引き、各行の選択肢はその行のホームの目録から作られる。変更項目の列挙と検証エラーに定義名が付く。ウィンドウの大きさはタブの切り替えで変わらない。
- **検証方法**：`dotnet build` と `dotnet publish` が成功する。発行した exe を起動し、両タブで値を変えて保存した後、対象ファイルの差分を `git diff --no-index` または目視で確認する。`gpt-agents/codex-review.md` を一時的に別名に退避し、サブエージェントタブだけで保存できることも確認する。確認後は元の値に戻す。

#### 段階 3：文書

- **変更対象**：`docs/gui.md`、`README.md` の設定コンソールの説明、`docs/setup.md` の参照
- **期待する結果**：使い方の文書がタブ 2 枚の画面と、レビューと実装補助タブで書き換えない項目を説明している。
- **検証方法**：文書の記述が「画面」と「書き換える項目」の節と一致することを目視で確認する。

## 既知の制約

- 保存した GPT 側定義は次に `codex-agent.sh` を呼んだときから効き、Claude 側定義はそのディレクトリがセッション開始時から在れば数秒で次の委譲に反映される。再起動が要る条件は [setup.md](setup.md) の共通手順 6 にある。起動済みのサブエージェントには影響しない。
- 保存時の外部変更の検出には、ごく短い競合の余地が残る。照合を終えてから `File.Replace` で置き換えるまでの間に別のプロセスがそのファイルを保存すると、その変更を検出できない。照合から置換までを排他制御で囲むと、一時ファイルを経由した原子的な置き換えと両立しない。単一の利用者が 8 つのファイルを編集するこの用途では、この競合を許容する。
- ユーザ定義側だけを対象にするため、利用先プロジェクトの `.claude/` に同名の定義があると、そちらが優先されて設定コンソールの変更が効かない。優先順位は [gpt-agents.md](gpt-agents.md) の「定義の探索」を参照。
- `codex_enabled: false` は `impl-hard`、`impl-light`、`impl-standard` を Claude 側の実装に切り替えるだけであり、`codex-review` と `codex-subagent` には影響しない。これらは明示的に Codex へ依頼する定義であり、Claude 側が代行すると依頼の意味が変わるためである。
- レビューと実装補助タブは、利用先プロジェクトの `.claude/gpt-agents/` に同名の定義があればそちらに負ける。ai-cross-review も同じ探索順で定義を読むため、この場合は設定コンソールの値がクロスレビューにも効かない。
- レビューと実装補助タブは `codex_home` を書き換えないため、認証ホームの割り当てを変えるには定義ファイルを手で直す。手順は [gpt-agents.md](gpt-agents.md) の「認証ホームの割り当て」にある。
