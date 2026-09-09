# 設定コンソールの使い方

設定コンソールは、ユーザ定義側のエージェント定義ファイルのフロントマターだけを書き換える Windows GUI である。
Codex の依頼処理も Claude Code のセッションも起動しないため、定義ファイルの設定値を編集する用途に限って使う。
ただし、画面にバージョンを表示するため、起動時に `codex --version` だけを一度実行する。

## ビルドと配置

リポジトリのルートで次のコマンドを実行する。

```powershell
dotnet publish gui/CodexBridgeConsole/CodexBridgeConsole.csproj -c Release -o gui/dist
```

発行に成功すると `gui/dist/CodexBridgeConsole.exe` が得られる。
生成した exe はリポジトリにコミットしない。

設定コンソールは .NET Framework 4.8 を対象とする。
Windows 10 1903 以降と Windows 11 には .NET Framework 4.8 が同梱されているため、ランタイムを別途導入する必要はない。

発行後は `gui/dist/CodexBridgeConsole.exe` を起動する。
プルダウンの選択肢を変更する場合は、後述の `choices.json` を exe と同じフォルダに置く。
サブエージェント全体の配置手順は [setup.md](setup.md) を参照する。

## 書き換えの対象

既定の対象は `%USERPROFILE%\.claude` であり、画面には環境変数を展開した絶対パスが表示される。
設定コンソールが書き換えるファイルは、次の 5 ファイルだけである。

| 区分 | ファイル |
|---|---|
| Claude 側 hard | `%USERPROFILE%\.claude\agents\impl-hard.md` |
| Claude 側 standard | `%USERPROFILE%\.claude\agents\impl-standard.md` |
| Claude 側 light | `%USERPROFILE%\.claude\agents\impl-light.md` |
| GPT 側 standard | `%USERPROFILE%\.claude\gpt-agents\impl-standard.md` |
| GPT 側 light | `%USERPROFILE%\.claude\gpt-agents\impl-light.md` |

利用先プロジェクトの `.claude/` に同名の定義があると、プロジェクト側の定義が優先されるため、設定コンソールで変更した値は使われない。
定義の優先順位は [gpt-agents.md](gpt-agents.md) の「定義の探索」を参照する。

## 画面の項目

ウィンドウは固定サイズであり、最大化できない。

### 対象と再読込

「対象」には、読み書きする `%USERPROFILE%\.claude` の実パスを表示する。
「再読込」を押すと、5 ファイルをディスクから読み直す。
未保存の変更がある場合は確認ダイアログを表示し、「はい」を選ぶと変更を破棄して読み直す。

### GPT 系サブエージェント経路

「GPT 系サブエージェント経路を有効にする (impl-light / impl-standard)」は、GPT 側の 2 定義に書く `codex_enabled` の値を切り替える。
チェックを外すと GPT モデルと effort の入力欄を無効にするが、入力済みの値は保持する。
チェックを変えて保存すると、`impl-light` と `impl-standard` の両方に同じ値を書く。
無効にして保存すると `codex_enabled: false` が両方に反映される。
チェックを操作していない場合は `codex_enabled` に触れない。
キーが無い定義を有効のまま保存した場合も、省略時の既定値が `true` であるためキーを追加しない。

`codex_enabled` に `true` と `false` 以外の値が書かれている場合は、無効として表示し、そのファイル名を画面に示す。
`tools/codex-agent.sh` は不正値を終了コード 2 で止めるため、有効として扱えないからである。
保存すると表示どおりの値へ直す。

2 定義の `codex_enabled` が食い違っている場合は、両方が有効なときだけ有効として表示する。
食い違いは画面に示す。
片方だけ無効の状態を有効として表示すると、別の項目を保存したときに無効側が有効へ戻るためである。

食い違いを解消するには、チェックを操作してから保存する。
チェックを一度反転させて戻した場合も操作として扱い、表示どおりの値を両方へ書き戻す。

`codex_enabled: false` のとき、`tools/codex-agent.sh` は Codex を起動せず、終了コード 3 で停止する。
`impl-light` と `impl-standard` はこの終了コードを受けると Claude 側で実装する。
`codex-review` と `codex-subagent` には影響しない。

### モデルと effort

表の Claude 側の列は、`impl-hard`、`impl-standard`、`impl-light` の Claude 側定義に書く `model` と `effort` を編集する。
`standard` と `light` の Claude 側のモデルと effort は、GPT 側がレートリミットで使えないときのフォールバックで使われる。
`impl-hard` は常に Claude 側のモデルと effort で動く。

表の GPT 側の列は、`impl-standard` と `impl-light` の GPT 側定義に書く `codex_model` と `codex_reasoning_effort` を編集する。
`hard` の GPT 側の列には「Codex を使わない」と表示し、入力欄を設けていない。

すべてのコンボボックスは、選択肢から選ぶだけでなく値を直接入力できる。
現在の設定値が選択肢に無い場合は、その値を選択肢の先頭に追加して選択状態にする。

### 状態表示

`codex_home` と `codex_sandbox` は GPT 側 `impl-light` 定義のフロントマターから読み取り、表示だけを行う。
`codex_home` は記載された値を表示し、`~`、`$USERPROFILE`、`%USERPROFILE%` を展開したパスが存在するかを括弧内に示す。
`codex --version` は画面の起動時に一度だけ実行し、実行中は「確認中...」と表示する。
実行時は GPT 側 `impl-light` 定義の `codex_home` を展開した値を `CODEX_HOME` に渡す。
既定のホームへ暗黙に依存しないためである。
`codex_home` を読めない場合は `codex` を起動せず、「認証ホーム未設定のため確認しない」と表示する。
取得した結果は保持するため、再読込しても表示は戻らない。
コマンドが見つからない場合は「見つからない」、5 秒以内に終了しない場合は「タイムアウト」と表示する。
ログイン状態は表示しない。

5 ファイルのいずれかが無い場合、またはフロントマターが壊れていて読めない場合は、その理由を画面に示し、「保存」を無効にする。
定義が壊れていても画面は開く。定義を直すための道具が、定義が壊れているときに起動できないと使えないためである。
保存後も起動中の Claude Code セッションには反映されないため、Claude Code を再起動する必要がある。

## 保存

「保存」を押すと、入力値を検証してから定義ファイルのフロントマターだけを書き換える。
フロントマターの後ろにある役割文や本文は変更しない。

保存前に、3 つの Claude 側定義の `model` と `effort` が空でないことを検証する。
GPT 経路が有効な場合は、2 つの GPT 側定義の `codex_model` も空でないことを検証する。
GPT 側の `codex_reasoning_effort` は、GPT 経路の有効状態にかかわらず `low`、`medium`、`high`、`xhigh`、`max` のいずれかであることを検証する。
モデル名と effort に使えるのは、英数字と `.`、`_`、`-`、`/` だけである。
先頭を `-` にはできず、英数字を 1 つ以上含む必要がある。`model: -` のように意味の定まらない行を書かないためである。
値は引用符を付けずにフロントマターへ書き戻すため、`model: foo: bar` のように YAML の意味を変える文字を通すと定義ファイルが読めなくなるためである。
検証に失敗すると理由をダイアログに示し、ファイルを書き換えない。

検証に通ると、値が変わったファイルだけを書き換え、書き換えたファイル名を状態行に表示する。
保存の前に 5 ファイルが揃っていることを見る。読み込み後に消されたファイルがあれば保存しない。
書き換えるファイルについては、読み込んだときの内容と現在の内容を照合する。設定コンソールの外で変更されていた場合は、その時点で保存を中断する。
この場合は警告ダイアログで再読込を促すため、再読込してからもう一度編集と保存を行う。
ファイルは順に保存するため、後続ファイルで外部変更を検出した場合は、先に保存されたファイルだけが書き換わる。
その場合はダイアログと状態行が、中断までに保存されたファイル名を示す。

「閉じる」を押すかウィンドウを閉じると、未保存の変更がある場合は保存して閉じるか、変更を破棄して閉じるか、キャンセルするかを選ぶ。
保存に失敗した場合は画面を閉じない。

## `choices.json` による選択肢の変更

exe と同じフォルダに `choices.json` を置くと、埋め込みの既定値を丸ごと置き換えられる。
JSON は次の形で、4 つの配列をすべて指定する。

```json
{
  "claudeModels": ["claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5-20251001"],
  "claudeEfforts": ["low", "medium", "high"],
  "gptModels": ["gpt-5.6-luna", "gpt-5.6-sol"],
  "gptEfforts": ["low", "medium", "high", "xhigh", "max"]
}
```

各配列は空にできず、空白だけの選択肢も指定できない。
`choices.json` が壊れている場合や配列が欠けている場合は、埋め込みの既定値に戻る。
読み込んだ定義ファイルの現在値が選択肢に無い場合も、現在値を先頭に追加して保持する。

## 編集できない設定

`codex_home` と `codex_sandbox` は画面に表示するだけで、設定コンソールから編集できない。
サンドボックスを GUI から緩めると、[CLAUDE.md](../CLAUDE.md) の「権限の固定」の原則に反するためである。
