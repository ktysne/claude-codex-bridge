# claude-codex-bridge の導入手順

この文書は、claude-codex-bridge を導入する AI(Claude Code など)が読む手順書である。人が読んで手で進めることもできる。
以下では Windows の PowerShell または Git Bash を使う。

## 進め方の決まり

- Claude のホーム(後述)の `settings.json`、`CLAUDE.md`、`agents/` と、`~/.claude/gpt-agents/`、`~/.claude/tools/` は、リポジトリの外にあり、開発者のすべてのセッションに効く。変更する前に、追加する内容(差分)を開発者に示し、確認を得てから書き込む。
- 既存の設定は消さずに統合する。`permissions.allow` の配列や `CLAUDE.md` の節は、既存の内容へ足す。同じ名前の定義が既にあるときは、上書きする前に差分を示す。
- `codex login` はブラウザでアカウントを選ぶ操作を伴う。AI はコマンドを示し、ログインは開発者に行ってもらう。2 アカウントで使うときは、どのホームにどのアカウントでログインするかを開発者に確かめる。
- 連携ツール(ai-cross-review、agent-cockpit)そのものの導入手順は、この文書では扱わない。導入されていなければ、各ツールのドキュメントへ案内する。この文書は、連携ツールが導入済みのときに必要な設定と確認だけを書いている。
- 定義とスクリプトは `~/.claude`(`%USERPROFILE%\.claude`)に置く。`tools/` と `gpt-agents/` は、定義が起動スクリプトを `bash ~/.claude/tools/codex-agent.sh` の固定のパスで呼び、そのスクリプトが `~/.claude/gpt-agents/` を読むため、`CLAUDE_CONFIG_DIR` を設定していても置き場は変わらない。
- Claude Code は Claude 側定義(`agents/`)、`settings.json`、`CLAUDE.md` を **Claude のホーム**から読む。Claude のホームは、`CLAUDE_CONFIG_DIR` が設定されていればその値、無ければ `~/.claude` である。以下の手順で Claude のホームと書いた置き場は、この値に読み替える。導入の前に `CLAUDE_CONFIG_DIR` が設定されているかを確かめる。
- 動作確認の一部は実モデルを起動し、利用枠を消費する。該当する手順にはその旨を書いてある。
- 定義を追加または変更した後は、Claude Code を再起動してから委譲に使う(共通手順 6)。

## パターンの選び方

アカウントの使い方で、パターン 1〜3 から 1 つを選ぶ。まず共通手順 0〜6 を行い、続けて選んだパターンの手順を行う。
そのうえで、導入済みの連携ツールに合わせて「連携: + ai-cross-review」「連携: + agent-cockpit」を足す。2 つの連携は互いに独立していて、どちらか一方だけでも両方でもよい。

| パターン | 使い方 | 配置する Codex 側の定義 |
|---|---|---|
| 1 | 1 アカウントで、実装のサブエージェント委譲だけを使う | `impl-hard`、`impl-light`、`impl-standard` |
| 2 | 1 アカウントで、実装の委譲に加えてレビューや調査も Codex に依頼する | パターン 1 に加えて `codex-review`、`codex-subagent` |
| 3 | 2 アカウントで、通常利用とレビューのアカウントを既定ホーム `~/.codex` に、サブエージェント専用のアカウントを `~/.codex-subagent` に置く | パターン 2 と同じ 5 定義 |

| 連携 | 対象 | 導入済みかの確かめ方 |
|---|---|---|
| + ai-cross-review | 差分のレビューを bridge の定義で回す | レビューを回すリポジトリのルートに `tools/cross-review.js` がある(リポジトリごとに導入するツールである) |
| + agent-cockpit | サブエージェントの表示と経路の切り替え | `http://127.0.0.1:47821/health` が `"app":"agent-cockpit"` を返す(`AGENT_COCKPIT_PORT` でポートを変えていればその値) |

ai-cross-review と連携するなら、`codex-review` と `codex-subagent` を配置するパターン 2 か 3 を選ぶ。パターン 1 では 2 定義が無いので、ai-cross-review は bridge を経由せずに `codex` を直接起動する。

ここでいう**通常利用**は Codex CLI の対話、VS Code や Chrome の Codex 拡張、Claude Code の Codex プラグインを指し、**サブエージェント**は GPT 側へ実装を委譲する `impl-hard`、`impl-light`、`impl-standard`、`codex-subagent` の 4 定義を指す。
`codex-review` も Claude Code からはサブエージェントとして起動されるが、役割はレビューなので既定ホーム側のアカウントを使う。

レートリミットを分散することだけを目的にアカウントを切り替える構成は採用しない。
アカウント A が上限に達したらアカウント B へ回す、というローテーションは OpenAI の利用規約に抵触する恐れがあるためである。
パターン 3 では、レビューとサブエージェントへの委譲を役割として分け、利用上限に応じて処理を別アカウントへ回さない。

### 3 つのツールをまとめて導入するとき

claude-codex-bridge、[ai-cross-review](https://github.com/ktysne/ai-cross-review)、[agent-cockpit](https://github.com/ktysne/agent-cockpit) をまとめて入れるときは、次の順に進める。
どのツールも、ほかのツールの有無を実行時に調べるので、順番を変えても動く。この順にすると、各ツールの連携の確認を、導入した時点でそのまま行える。

1. この文書の共通手順と、パターン 2 か 3 を行う。
2. agent-cockpit を、そのドキュメントに従って導入する(パターン A、B、C)。続けて、この文書の「連携: + agent-cockpit」を確かめる。
3. レビューを回すリポジトリごとに、ai-cross-review をそのドキュメントに従って導入する。続けて、この文書の「連携: + ai-cross-review」を確かめる。
4. agent-cockpit の導入手順のパターン D を行う。

## 共通手順

### 前提

- Claude Code(`claude --version`)
- git(`git --version`)。このリポジトリを取得し、更新するときに使う。
- Node.js(`node --version`)。ラッパー役の定義のフック `codex-agent-hook.js` を node で起動する。
- bash。Windows では Git Bash を使う。Claude Code の Bash ツールが Git Bash で動き、`codex-agent.sh` も bash で実行する。
- Codex CLI は手順 1 で導入する。

### 0. リポジトリを置く

```bash
git clone https://github.com/ktysne/claude-codex-bridge.git
```

置いた場所を、以降の手順の「このリポジトリ」として使う。定義とスクリプトはここから `~/.claude/` へ配置する。
この文書を raw の URL で読んでいるときは、文中の相対リンク(`gpt-agents.md` など)は、置いたリポジトリの `docs/` の下のファイルを読む。

### 1. Codex CLI を導入する

[公式の Codex CLI 導入手順](https://developers.openai.com/codex/cli/)に従って Codex CLI を導入する。
導入後、`codex` コマンドが PATH から呼べることを確認する。

```powershell
codex --version
```

### 2. 1 アカウント運用でログインする

パターン 1 とパターン 2 では、既定の認証ホーム `~/.codex` に使用するアカウントでログインする。

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex"
codex login
codex login status
```

`codex login status` がログイン済みの状態を示せばよい。
パターン 3 のログインは、パターン 3 の追加手順で行う。

### 3. 定義とスクリプトを配置する

このリポジトリの `.claude/agents/` にある必要な Claude 側定義を、Claude のホームの `agents/`(既定は `%USERPROFILE%\.claude\agents\`)にコピーする。
必要な GPT 側定義を、`~/.claude/gpt-agents/` (`%USERPROFILE%\.claude\gpt-agents\`) にコピーする。
`tools/codex-agent.sh` と `tools/codex-agent-hook.js` を `%USERPROFILE%\.claude\tools\` にコピーする。
ラッパー役の定義(窓口の `impl-hard`、`impl-light`、`impl-standard` と、`codex-review`、`codex-subagent`)は、フロントマターのフックで `codex-agent-hook.js` を node で起動する。
スクリプトか node が無いと、これらの定義の Bash と Write はすべて拒否される。
窓口の名前を知らない古い `codex-agent-hook.js` のまま新しい窓口の定義を置いた場合も、同じく拒否される。
そのため、スクリプトは定義より先に置く。仕組みは [gpt-agents.md](gpt-agents.md) の「ラッパー役の定義の道具を絞る」にある。
各パターンで配置する定義は、パターンごとの追加手順に示す。

`.claude/agents/impl-hard.md`、`.claude/agents/impl-hard-claude.md`、`.claude/gpt-agents/impl-hard.md` は、どのパターンでも配置する。
`.claude/gpt-agents/impl-hard.md` は出荷時から GPT 経路が有効であり、他の実装用定義と同じく GPT 側が使えないときに `impl-hard-claude` への再委譲を報告する。
GPT 経路の有効状態やモデル、effort は定義ファイルを正とし、設定コンソールからも変更できる。
次の手順で書く役割分担の表が `impl-hard` を参照するので、配置を省くと表の hard 区分を呼び出せなくなる。

特定の利用先プロジェクトだけで使う場合は、Claude 側定義を `<利用先プロジェクト>/.claude/agents/` に、GPT 側定義を `<利用先プロジェクト>/.claude/gpt-agents/` に置いてもよい。
この場合も、Claude 側定義が呼び出すスクリプトを `%USERPROFILE%\.claude\tools\codex-agent.sh` に置く。
ラッパー役の定義のフックは、プロジェクト側に置いた定義では、そのフォルダのワークスペース信頼が無いと効かない。worktree では元のリポジトリのフォルダの信頼で判定される(詳細は [gpt-agents.md](gpt-agents.md) の「ラッパー役の定義の道具を絞る」)。
プロジェクト側の GPT 側定義は、ユーザー定義側より優先して使われる。

`tools/codex-agent.sh` は行末が LF のまま配置する。
CRLF に変換されると、bash が行末の CR を引数として読み、実行に失敗する。
リポジトリでは `.gitattributes` で LF に固定しているが、エディタやコピーの経路で変換された場合は LF に戻す。

リポジトリを更新して定義やスクリプトの変更を取り込んだときは、同じ手順で再配置する。
配置済みの控えは自動では更新されない。

### 4. Claude Code の権限規則を設定する

Claude のホームの `settings.json`(既定は `%USERPROFILE%\.claude\settings.json`)の `permissions.allow` に、次の 2 規則を追加する。

```json
{
  "permissions": {
    "allow": [
      "Bash(bash ~/.claude/tools/codex-agent.sh *)",
      "Bash(bash tools/codex-agent.sh *)"
    ]
  }
}
```

既存の `permissions.allow` がある場合は、配列に 2 規則を追加する。
許可規則はコマンド文字列の先頭一致で判定される。
そのため、定義からスクリプトを呼ぶ形は `bash ~/.claude/tools/codex-agent.sh` で始まる 1 行にする。
変数への代入や `[ -f ... ] ||` の分岐を前に付けると許可されず、auto mode でサブエージェントが Codex を呼べない。
auto mode でない場合も、同じ規則を入れておけば確認プロンプトを省略できる。

### 5. メインセッションに役割分担を指示する

定義を配置しただけでは、メインセッションはどの依頼をどの定義に切り出すかを知らない。
Claude Code はサブエージェント定義を呼び出せるものとして読み込むだけで、難易度に応じて選ぶ規則は持たないためである。
そこで、Claude のホームの `CLAUDE.md`(既定は `%USERPROFILE%\.claude\CLAUDE.md`)に次の節を追加する。
特定のプロジェクトだけで使う場合は、そのプロジェクトの `CLAUDE.md` に追加する。

```markdown
## モデル役割分担（メインセッションとサブエージェント）
メインセッションは設計・監査・レビューに専念し、実装はサブエージェント(Agentツール)に切り出すことを基本とする。
サブエージェントは`.claude/agents/`の3定義から難易度に応じて選ぶ。モデルとeffortは定義側に持たせてあるので、呼び出し時は`subagent_type`を選ぶだけでよい。

| 区分 | 定義 | 想定するタスク |
|---|---|---|
| hard | `impl-hard` | 複数ファイル・複数層にまたがる設計変更。数値精度、並行処理、状態遷移など正しさの検証が難しいロジック。既存設計の理解が前提になる改修 |
| standard（既定） | `impl-standard` | 仕様が明確な機能追加や不具合修正。テストの追加・更新を伴う通常の変更。既存パターンに沿った新規コンポーネントの実装 |
| light | `impl-light` | 文言・コメント・ドキュメントの修正。レビュー指摘への局所的な追従修正。既存パターンをそのまま踏襲する定型的なテスト追加や小さなリファクタリング |

区分の判断基準は次のとおり。迷ったら一段上の区分に倒す（安い経路で失敗して往復するほうが高くつく）。
- 変更が1ファイルに収まり、既存コードの模倣で済むならlight。
- 仕様は決まっているが、実装の選択肢を考える必要があるならstandard。
- 仕様の解釈や設計判断を実装者が行う必要がある、または誤りの検出が難しいならhard。
- 大きな依頼は、目的と完了条件を一度に渡す。独立に進められる単位があれば担当ごとに分け、各担当の検証結果を確かめてから受け入れる。進捗は担当に TASKS.md などのファイルへ書かせる。置き場は依頼文に具体的なパスで書き、コミット対象にしない(スクラッチパッドか、除外設定済みのパス)。GPT 側で実装する担当は作業ツリーの外に書けないので、除外設定済みのパスを指定する。
- サブエージェントへの依頼文には、目的、変更対象、完了条件(例: 指定のテストが通る、対象の全箇所を移行した)、止まって報告する条件、検証方法を書く。
- サブエージェントの待機がタイムアウトしただけでは、処理停止と判断しない。
- タイムアウト時は、エージェントの状態、作業ツリーの差分、実行中のツールやプロセスを確認する。
- 差分などから進行が確認できる間は、待機を継続する。
- 進捗報告がなく、差分に変化がなく、実行中の処理もない状態が継続した場合に限り、中断または再開を検討する。判断に恣意的な固定時間を設けない。
- 各段階で同じ全テストを繰り返さない。担当中は関連するテストを実行し、変更完了後に全体検証をまとめて実施する。
- 3 定義は Codex へ転送する窓口である。窓口が `再委譲: impl-*-claude` を報告したら、作業ツリーの変更の有無を確かめ、Codex が途中まで書いた変更は既定で捨ててから、同じ依頼文の最初の行に `委譲: Claude 側で実装`、次の行に `理由: 再委譲:` と、窓口が報告した `codex-agent:` の行か拒否の文言を置いて、報告に書かれた `impl-*-claude` へ委譲し直す。`impl-*-claude` は表に載せず、それ以外では選ばない。
```

表にモデルと effort を書かない。
値は各定義のフロントマターが正であり、設定コンソールや手編集で変えたときに表を直さずに済ませるためである。
表の 3 定義は Codex へ転送する窓口で、窓口の値は転送役の起動にだけ使われる。
Claude 側で実装するときのモデルと effort は、`impl-hard-claude`、`impl-light-claude`、`impl-standard-claude` の値が使われる。
3 区分とも既定で GPT 側へ委譲し、GPT 側が使えないときは該当する `impl-*-claude` への再委譲を報告する。
パターン 2 とパターン 3 で配置する `codex-review` と `codex-subagent` は、難易度で選ぶ定義ではないため表に含めない。
レビューや調査を Codex に依頼するときに、メインセッションが明示的に指定する。

### 6. Claude Code を再起動する

`CLAUDE.md` はセッション開始時にだけ読み込まれる。
手順 5 で追加した役割分担を有効にするため、Claude Code を再起動する。
再起動後に表示される定義は、`impl-hard`、`impl-hard-claude` と、選んだパターンで配置したものだけになる。

エージェント定義を追加または変更したときは、Claude Code を再起動してから委譲する。
2026-10-03 に Claude Code 2.1.287 のデスクトップアプリの Code タブで確認した範囲では、再起動前はユーザ側と元のチェックアウトにある定義の変更が反映されず、worktree の `.claude/agents/` は読み込まれなかった。
CLI のセッションで再起動せずに変更が反映される場合があるかは確認していない。

GPT 側定義(`.claude/gpt-agents/`)と `tools/codex-agent.sh` は、`codex-agent.sh` が呼び出しのたびに読む。
`tools/codex-agent-hook.js` も、フックの起動のたびに読まれる。
これらだけを直したときは、再起動せずに次の呼び出しから効く。

### 7. 設定コンソールを導入する(任意)

配置した定義のモデル、effort、GPT 経路の有効状態、サブエージェントの認証ホームを GUI から変えるなら、設定コンソールを導入する。
設定コンソールでは、`codex-review` と `codex-subagent` の GPT 側モデルと effort も変更できる。
`gui\build.bat` をダブルクリックすると `gui\dist\CodexBridgeConsole.exe` ができる。
ビルドには .NET SDK が要る。
以後は exe をダブルクリックして起動し、手順 3 で配置したユーザ定義側の定義(最大 8 ファイル)をタブごとに編集する。パターン 1 のように `codex-review` と `codex-subagent` を配置していない場合、レビューと実装補助タブは使えない。
詳細は [設定コンソールの使い方](gui.md) を参照する。
定義ファイルを手で編集する運用でも差し支えないため、この手順は省略できる。

## パターン 1

### 使う定義

1 アカウントで実装のサブエージェント委譲だけを使う。
リポジトリの GPT 側定義は 2 アカウント運用の値で書かれているため、次のうち 3 つの GPT 側定義の `codex_home` を `~/.codex` に書き換える。

- `.claude/agents/impl-hard.md`
- `.claude/agents/impl-light.md`
- `.claude/agents/impl-standard.md`
- `.claude/agents/impl-hard-claude.md`
- `.claude/agents/impl-light-claude.md`
- `.claude/agents/impl-standard-claude.md`
- `.claude/gpt-agents/impl-hard.md`
- `.claude/gpt-agents/impl-light.md`
- `.claude/gpt-agents/impl-standard.md`

共通手順で `tools/codex-agent.sh` と `tools/codex-agent-hook.js` も配置する。

### 確認

利用先プロジェクトのルートで、配置した定義を直接確認する。

```bash
bash ~/.claude/tools/codex-agent.sh impl-hard --effort low <<< "Reply with exactly: PONG-IMPL-HARD"
bash ~/.claude/tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-IMPL-LIGHT"
bash ~/.claude/tools/codex-agent.sh impl-standard --effort low <<< "Reply with exactly: PONG-IMPL-STANDARD"
```

3 定義それぞれの監査行に `agent=<定義名>` と `sandbox=workspace-write` が出て、応答の末尾に `codex-agent: result=ok` が出ることを確認する。
`codex_home` に `~/.codex` に対応するパスが出て、各応答の末尾に `codex-agent: result=ok` が出ればよい。

Claude Code からも確かめる。導入を進めている AI は自分のセッションを再起動できないので、開発者に再起動を依頼し、共通手順 6 で再起動した新しいセッションで確かめる。サブエージェントは背景で起動する(`run_in_background: false` を指定しない)。`Agent` ツールに `subagent_type: impl-light` を指定し、依頼文に `導入の確認のための依頼です。ファイルは変更せず、最終報告として PONG-AGENT とだけ書いてください。` を渡す。
`Reply with exactly: PONG-AGENT` のような、返す文言だけを指示する依頼文は使わない。窓口が自分への指示として読み、Codex へ転送せずに `PONG-AGENT` とだけ返すことがあり、転送を確かめられないためである。
報告の 1 行目が「GPT 側(Codex)で実行した」で、続けて `codex-agent: agent=impl-light` の監査行、`codex-agent: run=` の行、`codex-agent: result=ok` の行が出ればよい。
窓口の報告には Codex の応答の本文が含まれない。本文は、`run=` の行の実行 ID で `bash ~/.claude/tools/codex-agent.sh --wait <実行 ID>` を実行して取り出し、`PONG-AGENT` を確かめる。
`impl-hard` と `impl-standard` も同じ形で確かめる。
`Agent type '<定義名>' not found` のように定義が見つからないときは、手順 3 の配置と、共通手順 6 の反映条件を確認する。

## パターン 2

### 使う定義

1 アカウントで実装の委譲、レビュー、実装補助をすべて使う。
リポジトリの GPT 側定義は 2 アカウント運用の値で書かれているため、次のうち `impl-hard`、`impl-light`、`impl-standard`、`codex-subagent` の 4 つの `codex_home` を `~/.codex` に書き換える(`codex-review` は既に `~/.codex` である)。

- `.claude/agents/impl-hard.md`
- `.claude/agents/impl-light.md`
- `.claude/agents/impl-standard.md`
- `.claude/agents/impl-hard-claude.md`
- `.claude/agents/impl-light-claude.md`
- `.claude/agents/impl-standard-claude.md`
- `.claude/agents/codex-review.md`
- `.claude/agents/codex-subagent.md`
- `.claude/gpt-agents/impl-hard.md`
- `.claude/gpt-agents/impl-light.md`
- `.claude/gpt-agents/impl-standard.md`
- `.claude/gpt-agents/codex-review.md`
- `.claude/gpt-agents/codex-subagent.md`

共通手順で `tools/codex-agent.sh` と `tools/codex-agent-hook.js` も配置する。

### 確認

利用先プロジェクトのルートで、配置した定義を直接確認する。

```bash
bash ~/.claude/tools/codex-agent.sh impl-hard --effort low <<< "Reply with exactly: PONG-IMPL-HARD"
bash ~/.claude/tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-IMPL-LIGHT"
bash ~/.claude/tools/codex-agent.sh impl-standard --effort low <<< "Reply with exactly: PONG-IMPL-STANDARD"
bash ~/.claude/tools/codex-agent.sh codex-review --effort low <<< "Reply with exactly: PONG-REVIEW"
bash ~/.claude/tools/codex-agent.sh codex-subagent --effort low <<< "Reply with exactly: PONG-SUBAGENT"
```

実装用の 3 定義では、監査行に `sandbox=workspace-write` と `codex_home=...` が出て、応答の末尾に `codex-agent: result=ok` が出ることを確認する。
レビュー用の `codex-review` では `sandbox=read-only` が出ることを確認する。
実装補助用の `codex-subagent` では `sandbox=workspace-write` が出ることを確認する。
5 定義すべての応答の末尾に `codex-agent: result=ok` が出ればよい。
Claude Code の `Agent` ツールからも、窓口の 3 定義と `codex-review`、`codex-subagent` の 5 つの `subagent_type` をそれぞれ指定して確認する。
窓口の 3 定義の報告は、パターン 1 の確認と同じ形になる。`codex-review` と `codex-subagent` の報告には、Codex の応答の本文と `codex-agent: result=ok` の行がそのまま含まれる。
`Agent type '<定義名>' not found` のように定義が見つからないときは、手順 3 の配置と、共通手順 6 の反映条件を確認する。

## パターン 3

### 用途別にログインする

通常利用とレビューに使うアカウントは既定ホーム `%USERPROFILE%\.codex` に置く。
Codex CLI の対話、VS Code や Chrome の Codex 拡張、Claude Code の Codex プラグインは既定ホームしか見ないため、そこを空けると未ログイン扱いになり、`~/.codex` が自動で再生成されるためである。
サブエージェント専用のアカウントには `%USERPROFILE%\.codex-subagent` を与える。

既定ホームにログイン済みなら、`%USERPROFILE%\.codex-subagent` だけログインすればよい。

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-subagent"
codex login
codex login status

$env:CODEX_HOME="$env:USERPROFILE\.codex"
codex login status
```

両方の `codex login status` がログイン済みの状態を示すことを確認する。
2 つのホームに同じアカウントでログインしないよう、ブラウザのアカウント選択を確認する。

既定ホームのアカウントを入れ替えるときは、既定ホームで `codex logout` してから `codex login` する。
`config.toml`、フック、プラグインなどの設定は `auth.json` と別のファイルにあるため、ログインし直してもそのまま残る。

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex"
codex logout
codex login
codex login status
```

#### サブエージェント側のホームを CLI から開く

`%USERPROFILE%\.codex-subagent` の設定値を CLI から確認したり書き換えたりする場合は、PowerShell のプロファイル(`$PROFILE`)に次の関数を追加し、`codex` の代わりに `codex-sub` で起動する。
サブエージェント側のホームは `CODEX_HOME` を明示しないと開けないため、環境変数の設定と `codex` の起動を 1 つの関数にまとめている。

```powershell
function codex-sub {
    $env:CODEX_HOME = "$env:USERPROFILE\.codex-subagent"
    try { codex @args } finally { Remove-Item Env:CODEX_HOME -ErrorAction SilentlyContinue }
}
```

`finally` で `CODEX_HOME` を消しているのは、`codex-sub` を終えた後に同じシェルで `codex` と打ったときに、既定ホームのアカウントへ戻るようにするためである。
戻しの作業は不要であり、`codex-sub` と `codex` を同じシェルで交互に使ってよい。

```powershell
codex-sub login status  # サブエージェント側のログイン状態を確認する
codex-sub               # サブエージェント側のホームで対話を開く
codex login status      # 既定ホーム側に戻っていることを確認する
```

サブエージェント専用のアカウントは、設定値の確認や点検に限って CLI から開く。
実装の依頼やレビューを `codex-sub` で行うと、用途固定の原則(通常利用とレビューは既定ホームのアカウントで行う)から外れる。

### 使う定義

8 つの Claude 側定義(ラッパー役の 5 定義と `impl-*-claude` の 3 定義)、5 つの GPT 側定義、`tools/codex-agent.sh`、`tools/codex-agent-hook.js` を配置する。

- `.claude/agents/impl-hard.md`
- `.claude/agents/impl-light.md`
- `.claude/agents/impl-standard.md`
- `.claude/agents/impl-hard-claude.md`
- `.claude/agents/impl-light-claude.md`
- `.claude/agents/impl-standard-claude.md`
- `.claude/agents/codex-review.md`
- `.claude/agents/codex-subagent.md`
- `.claude/gpt-agents/impl-hard.md`
- `.claude/gpt-agents/impl-light.md`
- `.claude/gpt-agents/impl-standard.md`
- `.claude/gpt-agents/codex-review.md`
- `.claude/gpt-agents/codex-subagent.md`

GPT 側定義の `codex_home` は次の表のとおりに書く。リポジトリの定義の既定値と同じである。

| GPT 側定義 | `codex_home` |
|---|---|
| `.claude/gpt-agents/impl-hard.md` | `~/.codex-subagent` |
| `.claude/gpt-agents/impl-light.md` | `~/.codex-subagent` |
| `.claude/gpt-agents/impl-standard.md` | `~/.codex-subagent` |
| `.claude/gpt-agents/codex-review.md` | `~/.codex` |
| `.claude/gpt-agents/codex-subagent.md` | `~/.codex-subagent` |

モデル、effort、サンドボックスの値は変更しない。
`codex_home` 以外のフロントマターは、リポジトリの定義をそのまま使う。

### 確認

利用先プロジェクトのルートで、配置した定義を直接確認する。

```bash
bash ~/.claude/tools/codex-agent.sh impl-hard --effort low <<< "Reply with exactly: PONG-IMPL-HARD"
bash ~/.claude/tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-IMPL-LIGHT"
bash ~/.claude/tools/codex-agent.sh impl-standard --effort low <<< "Reply with exactly: PONG-IMPL-STANDARD"
bash ~/.claude/tools/codex-agent.sh codex-review --effort low <<< "Reply with exactly: PONG-REVIEW"
bash ~/.claude/tools/codex-agent.sh codex-subagent --effort low <<< "Reply with exactly: PONG-SUBAGENT"
```

`codex-review` の監査行に `codex_home=.../.codex` が出て、他の 4 定義の監査行に `codex_home=.../.codex-subagent` が出ることを確認する。
`codex-review` では `sandbox=read-only`、`codex-subagent` では `sandbox=workspace-write` が出ることを確認する。
5 定義すべての応答の末尾に `codex-agent: result=ok` が出ればよい。
Claude Code の `Agent` ツールからも、窓口の 3 定義と `codex-review`、`codex-subagent` の 5 つの `subagent_type` をそれぞれ指定して確認する。
窓口の 3 定義の報告は、パターン 1 の確認と同じ形になる。`codex-review` と `codex-subagent` の報告には、Codex の応答の本文と `codex-agent: result=ok` の行がそのまま含まれる。
`Agent type '<定義名>' not found` のように定義が見つからないときは、手順 3 の配置と、共通手順 6 の反映条件を確認する。

## 設定に関する注意

既定ホーム以外のホーム(`~/.codex-subagent`)を使う定義では、既定ホーム `~/.codex` の `config.toml` は読み込まれない。
モデルと effort は `.claude/gpt-agents/` 側で指定するため、read-only で動く `codex-review` のホームは設定ファイルがなくても動く。
既定ホームの `config.toml` をそのままコピーすると、フック、MCP サーバ、通知などの設定まで持ち込まれるため避ける。

Windows のサンドボックスは `CODEX_HOME` ごとに設定される。
書き込みを行う定義(`impl-hard`、`impl-light`、`impl-standard`、`codex-subagent`)が使うホームでは、この設定が既定ホームから引き継がれない。
Codex v0.153.4 では、`[windows]` の `sandbox` 設定が無いホームで `--sandbox workspace-write` を指定すると、起動時の見出しに `sandbox: read-only` と出て書き込みが拒否されることを確認している。
見出しの `sandbox:` 行が `read-only` になっていたら、この設定の不足を疑う。
既定ホームの `config.toml` から `[windows]` の `sandbox` の値を写し、作業ディレクトリの信頼設定と合わせて、そのホームの `config.toml` に最小限だけ書く。
信頼設定のパスは、Codex を動かすプロジェクト(または worktree)の絶対パスを小文字で書く。既定ホームの `config.toml` にある `[projects.'...']` の書き方に合わせる。

```toml
[windows]
sandbox = "elevated"  # 既定ホームの config.toml と同じ値にする

[projects.'<プロジェクトの絶対パス>']  # 例: 'd:\projects\my-repo'
trust_level = "trusted"
```

Claude Code の Codex プラグイン(`codex:codex-rescue` など)は、この仕組みとは別に動く。
プラグインはセッション共有の broker 経由で `codex app-server` を起動し、broker プロセスの環境変数を起動時に固定する。
呼び出しごとに `CODEX_HOME` を切り替える用途には向かないため、用途別アカウント運用はこのリポジトリの定義で行う。
プラグインが使うのは既定ホームのアカウント、つまり通常利用とレビューに使うアカウントである。

定義ファイルのモデル、effort、GPT 系サブエージェント経路の有効状態、サブエージェントの認証ホームを GUI から変える場合は、共通手順 7 の設定コンソールを使う。
設定コンソールでは、`codex-review` と `codex-subagent` の GPT 側モデルと effort も変更できる。

## 連携: + ai-cross-review

### 前提

[ai-cross-review](https://github.com/ktysne/ai-cross-review) を、そのドキュメントに従って、レビューを回すリポジトリへ導入しておく。ai-cross-review はリポジトリごとに入れるツールである。
bridge はパターン 2 か 3 で導入し、`codex-review` と `codex-subagent` を配置しておく。

### bridge 側の設定

bridge 側で追加する設定は無い。ai-cross-review は `codex` を起動するときに、環境変数 `CROSS_REVIEW_CODEX_AGENT`、`~/.claude/tools/codex-agent.sh` の順に起動スクリプトを探し、見つかれば bridge 経由で起動する。

- レビューだけのときは定義 `codex-review`、`--fix` を付けたときは定義 `codex-subagent` を使う。
- ai-cross-review は起動の前に、定義ファイルの `codex_sandbox` がレビューなら `read-only`、`--fix` なら `workspace-write` かを確かめ、食い違えば起動しない。`codex-review` の `codex_sandbox` は `read-only` のまま変えない。
- ai-cross-review は、`codex-agent.sh` の `codex exec` に `approval_policy=never` の指定があるときだけ bridge を経由する。古い `codex-agent.sh` を配置したままだと、bridge を経由せずに `codex` を直接起動する。
- 定義が無い、`codex` が無いなどで `codex-agent.sh` が終了コード 3 を返したときは、ai-cross-review は `codex` を直接起動する。このときの認証ホームは、実行した環境の `CODEX_HOME`(未設定なら既定ホーム)になる。

### 確認

1. ai-cross-review を入れたリポジトリのルートで、次を実行する。実モデルを起動するため、利用枠を消費する。

   ```bash
   npm run review:codex -- --uncommitted --no-state
   ```

   未コミットの変更が無いと「レビュー対象の差分がありません。」と出て、Codex を起動しない。そのときは、確認用の小さな変更を作ってから流し、終わったら戻す。
   agent-cockpit の経路設定でレビュアーを Claude にしていると、この実行は拒否される(終了コード 2)。先に `node tools/cross-review.js route` で `default` か `codex` であることを確かめる。

2. 出力(stdout)に「Codex でレビューを実行します: codex-agent.sh 経由 (定義: codex-review)」と出て、`codex-agent: agent=codex-review` の監査行に `sandbox=read-only` が出ればよい。パターン 3 では、監査行の `codex_home=` が `.../.codex` であることも確かめる。
3. 「bridge が未導入のため直接起動へ切り替えます。」と出るときは、`~/.claude/gpt-agents/codex-review.md` の配置、その `codex_enabled` と `codex_model`、`codex` が PATH にあるかを確かめる。
4. 「approval_policy=never を明示していないため直接起動へ切り替えます」と出るときは、リポジトリを更新し、共通手順 3 で `codex-agent.sh` を配置し直す。

## 連携: + agent-cockpit

### 前提

[agent-cockpit](https://github.com/ktysne/agent-cockpit) を、そのドキュメントに従って導入しておく。経路の切り替えを使うには、agent-cockpit の導入手順のパターン C(経路のフック `route.js` の登録)も行う。

### bridge 側の設定

bridge 側で追加する設定は無い。agent-cockpit とは次の形でつながる。

- agent-cockpit は、Claude のホームの `tools/codex-agent.sh` があることで、bridge が導入済みだと判断する。agent-cockpit は `CLAUDE_CONFIG_DIR` が設定されていればそのディレクトリを Claude のホームとして見るが、bridge は `~/.claude` に固定で置く。`CLAUDE_CONFIG_DIR` を `~/.claude` 以外に設定しているときは、agent-cockpit が bridge を検出できない。([ktysne/agent-cockpit#115](https://github.com/ktysne/agent-cockpit/issues/115))
- agent-cockpit は、サブエージェントの Bash に出る `codex-agent.sh <定義名>` と、ラッパーが出す `codex-agent: agent=` の監査行から、Codex のモデル、effort、認証ホームを読んで表示する。
- ダッシュボードの「経路の設定」は、次に起動するサブエージェントに効く。稼働中のサブエージェントは切り替わらない。

経路ごとの動きは次のとおりである。

| 経路 | 窓口(`impl-hard`、`impl-light`、`impl-standard`) | `codex-review`、`codex-subagent` |
|---|---|---|
| 既定 | 通常どおり Codex へ転送する | 通常どおり Codex を起動する |
| Codex | Codex へ転送する。起動時に「Codex で行う」指定が伝えられる | 通常どおり Codex を起動する |
| Claude | `codex-agent.sh` の実行が agent-cockpit のフックに拒否される。窓口は拒否の文言を添えて `再委譲: impl-*-claude` を報告し、メインセッションが `impl-*-claude` へ委譲し直す | `codex-agent.sh` の実行が拒否され、拒否の文言を報告して止まる |

経路を Claude にしたときの再委譲は、窓口の定義の「権限判定やフックに拒否された場合」の扱いによる。agent-cockpit の拒否の文言は `開発者の指定(agent-cockpit の経路設定):` で始まり、`codex-agent-hook:` では始まらない。
メインセッションは、この拒否の文言を `理由: 再委譲:` の後ろに置いて委譲し直す(「委譲の検証」の手順)。
この表は、定義の一般の規則から期待される動きである。経路が Claude のときは起動時にも「codex-agent.sh を呼ばない」指定が伝わるので、窓口が呼び出しを試みずに再委譲を報告することもある。経路が Codex のときは、再委譲で起動した `impl-*-claude` にも「Codex で行う」指定が伝わり、`codex-agent.sh` を実行しない定義と食い違う。agent-cockpit の指定を定義で名指しして扱うことと、実機での確認は [#129](https://github.com/ktysne/claude-codex-bridge/issues/129) で進める。

### 確認

1. 新しいセッションを開き、ダッシュボードを再読み込みする。上部の「経路の設定」に、サブエージェントの段(既定、Codex、Claude)が出ればよい。出ないときは、`~/.claude/tools/codex-agent.sh` の配置と、`CLAUDE_CONFIG_DIR` の設定を確かめる。
2. 経路を「既定」のまま、パターンごとの確認と同じ依頼で `impl-light` を起動する。サブエージェントの行に、Codex のモデルの札が出ればよい。
3. 経路を「Claude」にしてから、同じ依頼で `impl-light` を起動する。窓口が `再委譲: impl-light-claude` と、`開発者の指定(agent-cockpit の経路設定):` を含む拒否の文言を報告すればよい。違う報告になったときは #129 を見る。確かめ終わったら、経路を元の値に戻す。

## 更新するとき

リポジトリを更新して定義やスクリプトの変更を取り込んだときは、共通手順 3 と同じ手順で配置し直す。配置済みの控えは自動では更新されない。

1. `git pull` でリポジトリを更新する。
2. `tools/codex-agent.sh` と `tools/codex-agent-hook.js` を `~/.claude/tools/` に配置し直す。スクリプトは定義より先に置く。
3. 選んだパターンの定義を、共通手順 3 で置いた場所(Claude のホームの `agents/` と `~/.claude/gpt-agents/`、またはプロジェクトの `.claude/agents/` と `.claude/gpt-agents/`)に配置し直す。GPT 側定義の `codex_home`、`codex_enabled`、モデル、effort を変えて使っているときは、上書きの前に差分を示し、変えた値を引き継ぐ。設定コンソールで変えた値も同じである。
4. 共通手順 5 の役割分担の節が変わっていれば、共通手順 5 で節を足した `CLAUDE.md`(Claude のホームか、プロジェクトのもの)の節を書き換える。
5. エージェント定義(`.claude/agents/`)を変えたときは、Claude Code を再起動する。
6. 選んだパターンの「確認」の `codex-agent.sh` のコマンドを流す。

## うまくいかないとき

- `Agent type '<定義名>' not found` と出る:共通手順 3 の配置と、共通手順 6 の再起動を確かめる。
- 窓口やラッパー役の定義の Bash と Write がすべて拒否される:`~/.claude/tools/codex-agent-hook.js` が無い、古い、または node が見つからない。共通手順 3 で、スクリプトを定義より先に配置し直す。
- プロジェクト側に置いた定義で、フックの制限が効かず、窓口が転送以外の操作をする:そのフォルダ(worktree なら元のリポジトリのフォルダ)のワークスペースの信頼を受け入れる。信頼が無いと、フックだけが黙って飛ばされる。
- `codex-agent.sh` が `$'\r': command not found` などで失敗する:行末が CRLF に変わっている。LF に戻す。
- auto mode でサブエージェントが Codex を呼べない:共通手順 4 の `permissions.allow` を確かめる。
- 終了コード 3 で止まる:`codex` が PATH に無い、GPT 側定義が無い、`codex_enabled: false` になっている、または `codex_model` が空である。窓口は `impl-*-claude` への再委譲を報告し、`codex-review` と `codex-subagent` は再委譲せずに止まる。
- `codex_home が存在しない` と出て終了コード 2 で止まる:定義の `codex_home` のディレクトリが無い。パターン 1 と 2 では、「使う定義」の手順で `codex_home` を `~/.codex` に書き換えたかを確かめる。パターン 3 では、`~/.codex-subagent` へのログインを確かめる。
- 終了コード 75 で止まる:利用上限など GPT 側の事情で実行できなかった。窓口は再委譲を報告し、`codex-review` と `codex-subagent` は終了コードと出力の末尾を返して止まる。
- 書き込みを行う定義で、監査行は `sandbox=workspace-write` なのに、Codex の起動時の見出しに `sandbox: read-only` と出る(Windows):そのホームの `config.toml` に `[windows]` の `sandbox` の設定が無い。「設定に関する注意」の手順で足す。
- 「進行中」と書いた報告が返る:Codex の実行中にサブエージェントがターンを終えた。委譲をやり直さずに、そのサブエージェントへ「codex-agent の状態確認」とだけ書いたメッセージを送る。詳しくは [gpt-agents.md](gpt-agents.md) の「既知の制約」にある。
