# 設定コンソール(Windows GUI)の設計

`impl-hard`、`impl-standard`、`impl-light` の定義ファイルを Windows の GUI から書き換えるための設定コンソールの設計である。
実装は済んでおり、この文書は仕様と制約を残す。
使い方は [gui.md](gui.md) を参照する。
末尾の「実装の段階」には、実装時の変更範囲と検証方法を記載している。

## 目的と範囲

設定コンソールは、次の 3 つを GUI から行えるようにする。

- GPT 系サブエージェント経路(`impl-light` と `impl-standard` が Codex へ委譲する経路)の有効と無効を切り替える。
- hard、standard、light の各区分で使う Claude 側のモデルと effort をプルダウンで選ぶ。
- standard と light の各区分で使う GPT 側のモデルと effort をプルダウンで選ぶ。

設定コンソールは Codex も Claude Code も起動しない。
Claude Code と `tools/codex-agent.sh` がセッション開始時や呼び出し時に読む定義ファイルのフロントマターだけを書き換える設定エディタである。

対象はユーザ定義側(`%USERPROFILE%\.claude\agents\` と `%USERPROFILE%\.claude\gpt-agents\`)だけとする。
利用先プロジェクトの `.claude\` 配下は対象にしない。
初版を小さく保つためであり、後から対象フォルダの選択を足しても、書き換えの規則は変わらない。

## 実装言語と配布形態

C# と Windows Forms で書き、ターゲットフレームワークは .NET Framework 4.8 とする。
Windows 10 1903 以降と Windows 11 には 4.8 が同梱されているため、ランタイムを別途導入せずに exe 単体で起動する。
画面はコンボボックスとチェックボックスが十数個の静的なフォームであり、デザイナを使わずコードで組み立てる。

ビルドは .NET SDK の `dotnet build` で行う。
`net48` をターゲットにすると SDK が参照アセンブリの NuGet パッケージを自動で取得するため、Visual Studio や Developer Pack は要らない。

## 書き換える項目

定義ファイルは Claude 側(`.claude/agents/<name>.md`)と GPT 側(`.claude/gpt-agents/<name>.md`)の 2 層に分かれる。
二層の役割は [gpt-agents.md](gpt-agents.md) の「定義ファイルの二層」を参照。
設定コンソールが書き換えるキーは次の表のとおりである。

| 区分 | Claude 側(`agents/<name>.md`) | GPT 側(`gpt-agents/<name>.md`) |
|---|---|---|
| hard(`impl-hard`) | `model`、`effort` | なし(Codex を呼ばない) |
| standard(`impl-standard`) | `model`、`effort`(フォールバック時に使う) | `codex_enabled`、`codex_model`、`codex_reasoning_effort` |
| light(`impl-light`) | 同上 | 同上 |

`codex_home` と `codex_sandbox` は表示するだけで編集させない。
サンドボックスを GUI から緩められると、[CLAUDE.md](../CLAUDE.md) の「権限の固定」の原則に反するためである。
`codex-review` と `codex-subagent` の 2 定義には触れない。

### Claude 側の `model` と `effort`

`impl-light` と `impl-standard` の Claude 側の値は、GPT 側がレートリミットで使えないときのフォールバックで使われる。
`impl-hard` は常に Claude 側の値で動く。
プルダウンにこの違いを示すため、standard と light の Claude 側の列見出しに「(フォールバック時)」を添える。

### GPT 経路の有効と無効

現在の `tools/codex-agent.sh` には無効化の概念がない。
`impl-light` と `impl-standard` が Claude 側にフォールバックするのは、スクリプトが終了コード 3(GPT 側が未導入)で止まったときだけである。
GPT 側定義を別名に退避すれば疑似的に無効化できるが、設定コンソールが途中で異常終了すると定義が退避されたまま残る。
そこで、GPT 側定義のフロントマターにキーを 1 つ足し、スクリプトがそれを解釈する。

- **codex_enabled**：`true` または `false`。省略時は `true`。`false` のときスクリプトは Codex を起動せず、終了コード 3 で止まる。

終了コード 3 の意味を「GPT 側が未導入、または無効化されている」に広げる。
`impl-light` と `impl-standard` は既存の手順どおり Claude 側で実装し、報告の冒頭を「GPT 側が未導入または無効化されているため Claude 側で実装した」に直す。
設定コンソールのトグルは、`impl-light` と `impl-standard` の GPT 側定義に同じ値を書く。

`codex_enabled` の値は `true` と `false` だけを受け付ける。
それ以外の値はスクリプトが終了コード 2(定義の不備)で止める。
キーを書いて値を空にした場合も、省略とみなさず終了コード 2 で止める。
既定が「Codex を起動する」側に倒れるため、書きかけの指定を有効と読ませないためである。
省略時に `true` とするのは、既存の定義ファイルを書き換えずに動かし続けるためである。

## 画面

ウィンドウは 1 枚で、リサイズしない。

```text
┌ claude-codex-bridge 設定コンソール ───────────────────────────────────────┐
│ 対象: C:\Users\<user>\.claude                                    [再読込]  │
│                                                                           │
│ [x] GPT 系サブエージェント経路を有効にする (impl-light / impl-standard)   │
│                                                                           │
│ 区分      Claude モデル        effort     GPT モデル        effort        │
│ hard      [claude-opus-5    v] [high   v]  (Codex を使わない)             │
│ standard  [claude-opus-5    v] [medium v]  [gpt-5.6-luna  v] [max    v]   │
│ light     [claude-sonnet-5  v] [medium v]  [gpt-5.6-luna  v] [xhigh  v]   │
│                                                                           │
│ codex_home: ~/.codex (存在する)   sandbox: workspace-write                │
│ codex --version: 0.xx.x                                                   │
│ 保存後、Claude Code を再起動すると反映される                              │
│                                                            [保存] [閉じる] │
└───────────────────────────────────────────────────────────────────────────┘
```

- **対象**：`%USERPROFILE%\.claude` を固定で表示する。定義ファイルが 1 つでも無ければ、無いファイル名を示して保存ボタンを無効にする。
- **トグル**：チェックを外すと GPT モデルと effort の列を無効表示(灰色)にする。値は保持し、再びチェックを入れると元に戻る。
- **プルダウン**：入力可のコンボボックス(`DropDownStyle = DropDown`)にする。選択肢に無いモデル名を将来使えるようにするためである。
- **状態行**：`impl-light` の GPT 側定義から `codex_home` と `codex_sandbox` を読み、`codex_home` の展開後パスが存在するかを表示する。`codex --version` は起動時に 1 回だけ実行し、`codex` が無ければ「見つからない」と表示する。ログイン状態(`codex login status`)は初版では表示しない。
- **保存**：検証に通れば 5 ファイル(Claude 側 3 つ、GPT 側 2 つ)を書き換え、値が変わっていないファイルには触れない。書き換えたファイル名を状態行に示す。照合に失敗した場合は保存せず、外部で変更された旨と再読込を促すダイアログを出す。
- **再読込**：ファイルから読み直し、未保存の変更を破棄する。未保存の変更があるときは確認ダイアログを出す。
- **閉じる**：未保存の変更があるときは確認ダイアログを出す。

Claude Code はエージェント定義をセッション開始時に読み込むため、保存しても起動中のセッションには反映されない。
この注意は常時表示する。

### プルダウンの選択肢

選択肢の既定値は exe に埋め込む。
exe と同じフォルダに `choices.json` があれば、それで既定値を丸ごと置き換える。
既定値は次のとおりである。

| 項目 | 選択肢 |
|---|---|
| Claude モデル | `claude-fable-5-1`、`claude-fable-5`、`claude-opus-5`、`claude-sonnet-5`、`claude-opus-4-8`、`claude-opus-4-7`、`claude-opus-4-6`、`claude-sonnet-4-6`、`claude-haiku-4-5` |
| Claude effort | 選ばれているモデルが受け付ける値。対応表に無いモデルでは `low`、`medium`、`high`、`xhigh`、`max` |
| GPT モデル | `codex debug models` から取得。取れなければ `gpt-6-astra`、`gpt-5.6-sol`、`gpt-5.6-terra`、`gpt-5.6-luna`、`gpt-5.5` |
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
  "claudeModels": ["claude-opus-5", "claude-sonnet-5"],
  "claudeEfforts": ["low", "medium", "high", "xhigh", "max"],
  "claudeModelEfforts": [
    { "model": "claude-opus-5", "efforts": ["low", "medium", "high", "xhigh", "max"] }
  ],
  "gptModels": ["gpt-6-astra", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5"],
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
- 値を書き換えるときは、対象キーの行だけを置換する。行内コメントは残す(例: `codex_home: ~/.codex  # 1 アカウント運用の既定値` の `#` 以降)。
- 値を書くときは常に二重引用符で囲む(`model: "claude-opus-5"`、`codex_enabled: "false"`)。引用符が無いと YAML は `true` や `123` を文字列以外として読み、文字列を期待する定義が壊れるためである。`tools/codex-agent.sh` の `fm_get` は外側の引用符を外して読むため、スクリプト側に変更は要らない。
- 値に含まれる `\` は `\\` に、`"` は `\"` にエスケープする。`fm_get` は引用符を外すだけでエスケープを戻さないため、この 2 文字を含む値を書くとスクリプトと読みがずれる。設定コンソールは値に使える文字を英数字と `.`、`_`、`-`、`/` に限っており、この 2 文字は入力できない。
- 読み込んだ値と書き込む値が等しい行には書かない。等しさは引用符を外した後の値で判定する。そのため、値を変えていない行は引用符の付かない元の形のまま残る。
- キーがフロントマターに無ければ、閉じの `---` の直前に `<キー>: <値>` の行を追加する。`codex_enabled` は既存の定義に無いため、この経路で追加される。
- 本文(閉じの `---` より後ろ)には触れない。
- 改行コード(LF または CRLF)と BOM の有無は、読み込んだファイルのものを保つ。
- 書き込みは同じフォルダの一時ファイルに出してから `File.Replace` または移動で置き換える。途中で失敗しても元のファイルが壊れないようにするためである。
- 値を書き換えるとき、コロンと値の間に空白が無い行には空白を 1 つ補う。Claude 側の定義は Claude Code が YAML として読むため、`model:値` の形になると解釈できなくなるためである。
- 保存の直前に、読み込んだときの内容とファイルの現在の内容を照合する。異なっていれば保存せずに失敗させる。設定コンソールの外で加えられた変更を黙って上書きしないためである。
- 同じキーが複数行ある場合は、最初の行だけを対象にする。スクリプトが最初の一致だけを読むためである。

### 保存前の検証

次のいずれかに当たる場合は保存せず、理由をダイアログで示す。

- Claude 側の `model` または `effort` が空である。
- GPT 側の `codex_model` が空である(トグルが有効のときのみ検査する)。
- GPT 側の `codex_reasoning_effort` が `low`、`medium`、`high`、`xhigh`、`max`、`ultra` のいずれでもない。
- `model`、`effort`、`codex_model` に、英数字と `.`、`_`、`-`、`/` 以外の文字がある。

値は二重引用符で囲んで書くため、`true` や `123` のような YAML の予約語と数値もそのまま保存できる。
使える文字を絞るのは、`fm_get` がエスケープを戻さないためである。

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

あわせて `die_missing` に `codex-agent: result=failed exit=3` の出力を足す。
[gpt-agents.md](gpt-agents.md) は「スクリプトは末尾に結果の 1 行を出す」と定めているが、この経路だけが出していなかったためである。
既存の 2 経路(定義ファイルが無い、`codex` コマンドが PATH に無い)にもこの行が出るようになる。

あわせて次の文書と定義を直す。

- [gpt-agents.md](gpt-agents.md)：「フロントマターのキー」に `codex_enabled` を追加し、「フォールバックの条件と終了コード」の 3 の説明を「GPT 側が未導入、または無効化されている」に広げる。
- `.claude/agents/impl-light.md` と `.claude/agents/impl-standard.md`：終了コード 3 の説明と報告冒頭の文言を「未導入または無効化」に直す。
- [setup.md](setup.md)：設定コンソールの配置と使い方への参照を「設定に関する注意」の後ろに 1 段落で足す。

`.claude/gpt-agents/impl-light.md` と `impl-standard.md` には `codex_enabled` を書かない。
省略時が `true` であり、書かなくても現在の挙動が保たれるためである。

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
│   ├─ ConsoleSettings.cs            5 定義の読み込み、検証、保存
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

## 既知の制約

- 設定コンソールは Claude Code の起動中のセッションには影響しない。反映には再起動が要る。
- 保存時の外部変更の検出には、ごく短い競合の余地が残る。照合を終えてから `File.Replace` で置き換えるまでの間に別のプロセスがそのファイルを保存すると、その変更を検出できない。照合から置換までを排他制御で囲むと、一時ファイルを経由した原子的な置き換えと両立しない。単一の利用者が 5 つのファイルを編集するこの用途では、この競合を許容する。
- ユーザ定義側だけを対象にするため、利用先プロジェクトの `.claude/` に同名の定義があると、そちらが優先されて設定コンソールの変更が効かない。優先順位は [gpt-agents.md](gpt-agents.md) の「定義の探索」を参照。
- `codex_enabled: false` は `impl-light` と `impl-standard` を Claude 側の実装に切り替えるだけであり、`codex-review` と `codex-subagent` には影響しない。これらは明示的に Codex へ依頼する定義であり、Claude 側が代行すると依頼の意味が変わるためである。
