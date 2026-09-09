# セットアップ手順

Codex CLI を Claude Code のサブエージェントとして呼び出すための導入手順である。
以下では Windows の PowerShell または Git Bash を使う。

## どのパターンを選ぶか

- **パターン 1**：実装のサブエージェント委譲だけを使い、1 アカウントで足りる場合に選ぶ。
- **パターン 2**：実装の委譲に加えてレビューや調査も Codex に依頼し、1 アカウントで運用する場合に選ぶ。
- **パターン 3**：レビューを実装用から分けたい場合に、実装用とレビュー用の認証を分け、役割ごとにアカウントを固定する。

レートリミットを分散することだけを目的にアカウントを切り替える構成は採用しない。
アカウント A が上限に達したらアカウント B へ回す、というローテーションは OpenAI の利用規約に抵触する恐れがあるためである。
パターン 3 では、実装用とレビュー用を役割として分け、利用上限に応じて処理を別アカウントへ回さない。

## 共通手順

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

このリポジトリの `.claude/agents/` にある必要な Claude 側定義を、`~/.claude/agents/` (`%USERPROFILE%\.claude\agents\`) にコピーする。
必要な GPT 側定義を、`~/.claude/gpt-agents/` (`%USERPROFILE%\.claude\gpt-agents\`) にコピーする。
`tools/codex-agent.sh` を `%USERPROFILE%\.claude\tools\` にコピーする。
各パターンで配置する定義は、パターンごとの追加手順に示す。

`.claude/agents/impl-hard.md` は、どのパターンでも Claude 側定義として配置する。
この定義は Codex を呼ばず Claude(Opus 5 / high)だけで実装するため、GPT 側定義とスクリプトを持たない。
次の手順で書く役割分担の表が `impl-hard` を参照するので、配置を省くと表の hard 区分を呼び出せなくなる。

特定の利用先プロジェクトだけで使う場合は、Claude 側定義を `<利用先プロジェクト>/.claude/agents/` に、GPT 側定義を `<利用先プロジェクト>/.claude/gpt-agents/` に置いてもよい。
この場合も、Claude 側定義が呼び出すスクリプトを `%USERPROFILE%\.claude\tools\codex-agent.sh` に置く。
プロジェクト側の GPT 側定義は、ユーザー定義側より優先して使われる。

### 4. Claude Code の権限規則を設定する

`%USERPROFILE%\.claude\settings.json` の `permissions.allow` に、次の 2 規則を追加する。

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
そのため、定義からスクリプトを呼ぶ形は `bash ~/.claude/tools/codex-agent.sh <name> ...` の 1 行から変えてはいけない。
変数への代入や `[ -f ... ] ||` の分岐を前に付けると許可されず、auto mode でサブエージェントが Codex を呼べない。
auto mode でない場合も、同じ規則を入れておけば確認プロンプトを省略できる。

### 5. メインセッションに役割分担を指示する

定義を配置しただけでは、メインセッションはどの依頼をどの定義に切り出すかを知らない。
Claude Code はサブエージェント定義を呼び出せるものとして読み込むだけで、難易度に応じて選ぶ規則は持たないためである。
そこで、利用先の `%USERPROFILE%\.claude\CLAUDE.md` に次の節を追加する。
特定のプロジェクトだけで使う場合は、そのプロジェクトの `CLAUDE.md` に追加する。

```markdown
## モデル役割分担（メインセッションとサブエージェント）
メインセッションは設計・監査・レビューに専念し、実装はサブエージェント(Agentツール)に切り出すことを基本とする。
サブエージェントは`.claude/agents/`の3定義から難易度に応じて選ぶ。モデルとeffortは定義側に持たせてあるので、呼び出し時は`subagent_type`を選ぶだけでよい。

| 区分 | 定義 | モデル / effort | 想定するタスク |
|---|---|---|---|
| hard | `impl-hard` | Opus 5 / high | 複数ファイル・複数層にまたがる設計変更。数値精度、並行処理、状態遷移など正しさの検証が難しいロジック。既存設計の理解が前提になる改修 |
| standard（既定） | `impl-standard` | Opus 5 / medium | 仕様が明確な機能追加や不具合修正。テストの追加・更新を伴う通常の変更。既存パターンに沿った新規コンポーネントの実装 |
| light | `impl-light` | Sonnet 5 / medium | 文言・コメント・ドキュメントの修正。レビュー指摘への局所的な追従修正。既存パターンをそのまま踏襲する定型的なテスト追加や小さなリファクタリング |

区分の判断基準は次のとおり。迷ったら一段上の区分に倒す（安い経路で失敗して往復するほうが高くつく）。
- 変更が1ファイルに収まり、既存コードの模倣で済むならlight。
- 仕様は決まっているが、実装の選択肢を考える必要があるならstandard。
- 仕様の解釈や設計判断を実装者が行う必要がある、または誤りの検出が難しいならhard。
```

表の「モデル / effort」は Claude 側定義の値である。
`impl-light` と `impl-standard` は既定で GPT 側へ委譲し、Claude 側の値はレートリミット時のフォールバックで使われる。
パターン 2 とパターン 3 で配置する `codex-review` と `codex-subagent` は、難易度で選ぶ定義ではないため表に含めない。
レビューや調査を Codex に依頼するときに、メインセッションが明示的に指定する。

### 6. Claude Code を再起動する

エージェント定義と `CLAUDE.md` はセッション開始時に読み込まれる。
配置した定義と追加した役割分担を有効にするため、Claude Code を再起動する。
再起動後に表示される定義は、`impl-hard` と、選んだパターンで配置したものだけになる。

## パターン 1

### 使う定義

1 アカウントで実装のサブエージェント委譲だけを使う。
`codex_home` は、次のうち 2 つの GPT 側定義で `~/.codex` のままにする。

- `.claude/agents/impl-hard.md`
- `.claude/agents/impl-light.md`
- `.claude/agents/impl-standard.md`
- `.claude/gpt-agents/impl-light.md`
- `.claude/gpt-agents/impl-standard.md`

共通手順で `tools/codex-agent.sh` も配置する。

### 動作確認

利用先プロジェクトのルートで、配置した 2 定義を直接確認する。

```bash
bash ~/.claude/tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-IMPL-LIGHT"
bash ~/.claude/tools/codex-agent.sh impl-standard --effort low <<< "Reply with exactly: PONG-IMPL-STANDARD"
```

それぞれの監査行に `agent=impl-light` または `agent=impl-standard` と `sandbox=workspace-write` が出ることを確認する。
`codex_home` に `~/.codex` に対応するパスが出て、各応答の末尾に `codex-agent: result=ok` が出ればよい。
Claude Code の `Agent` ツールからも `subagent_type: impl-light` と `subagent_type: impl-standard` をそれぞれ指定して同じ応答を確認する。

## パターン 2

### 使う定義

1 アカウントで実装の委譲、レビュー、実装補助をすべて使う。
`codex_home` は、次のうち 4 つの GPT 側定義で `~/.codex` のままにする。

- `.claude/agents/impl-hard.md`
- `.claude/agents/impl-light.md`
- `.claude/agents/impl-standard.md`
- `.claude/agents/codex-review.md`
- `.claude/agents/codex-subagent.md`
- `.claude/gpt-agents/impl-light.md`
- `.claude/gpt-agents/impl-standard.md`
- `.claude/gpt-agents/codex-review.md`
- `.claude/gpt-agents/codex-subagent.md`

共通手順で `tools/codex-agent.sh` も配置する。

### 動作確認

利用先プロジェクトのルートで、配置した 4 定義を直接確認する。

```bash
bash ~/.claude/tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-IMPL-LIGHT"
bash ~/.claude/tools/codex-agent.sh impl-standard --effort low <<< "Reply with exactly: PONG-IMPL-STANDARD"
bash ~/.claude/tools/codex-agent.sh codex-review --effort low <<< "Reply with exactly: PONG-REVIEW"
bash ~/.claude/tools/codex-agent.sh codex-subagent --effort low <<< "Reply with exactly: PONG-SUBAGENT"
```

実装用の 2 定義では、監査行に `sandbox=workspace-write` と `codex_home=...` が出ることを確認する。
レビュー用の `codex-review` では `sandbox=read-only` が出ることを確認する。
実装補助用の `codex-subagent` では `sandbox=workspace-write` が出ることを確認する。
4 つすべての応答の末尾に `codex-agent: result=ok` が出ればよい。
Claude Code の `Agent` ツールからも、4 つの `subagent_type` をそれぞれ指定して確認する。

## パターン 3

### 用途別にログインする

実装用とレビュー用の `CODEX_HOME` を `%USERPROFILE%` 配下に作り、それぞれ別のアカウントでログインする。
ブラウザ認証では、実装用とレビュー用に対応するアカウントを選ぶ。

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-subagent"
codex login
codex login status

$env:CODEX_HOME="$env:USERPROFILE\.codex-review"
codex login
codex login status
```

両方の `codex login status` がログイン済みの状態を示すことを確認する。
2 つのホームに同じアカウントでログインしないよう、ブラウザのアカウント選択を確認する。

### 使う定義

5 つの Claude 側定義、4 つの GPT 側定義、`tools/codex-agent.sh` を配置する。

- `.claude/agents/impl-hard.md`
- `.claude/agents/impl-light.md`
- `.claude/agents/impl-standard.md`
- `.claude/agents/codex-review.md`
- `.claude/agents/codex-subagent.md`
- `.claude/gpt-agents/impl-light.md`
- `.claude/gpt-agents/impl-standard.md`
- `.claude/gpt-agents/codex-review.md`
- `.claude/gpt-agents/codex-subagent.md`

GPT 側定義の `codex_home` を、次の表のとおりに書き分ける。

| GPT 側定義 | `codex_home` |
|---|---|
| `.claude/gpt-agents/impl-light.md` | `~/.codex-subagent` |
| `.claude/gpt-agents/impl-standard.md` | `~/.codex-subagent` |
| `.claude/gpt-agents/codex-review.md` | `~/.codex-review` |
| `.claude/gpt-agents/codex-subagent.md` | `~/.codex-subagent` |

モデル、effort、サンドボックスの値は変更しない。
`codex_home` 以外のフロントマターは、リポジトリの定義をそのまま使う。

### 動作確認

利用先プロジェクトのルートで、配置した 4 定義を直接確認する。

```bash
bash ~/.claude/tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-IMPL-LIGHT"
bash ~/.claude/tools/codex-agent.sh impl-standard --effort low <<< "Reply with exactly: PONG-IMPL-STANDARD"
bash ~/.claude/tools/codex-agent.sh codex-review --effort low <<< "Reply with exactly: PONG-REVIEW"
bash ~/.claude/tools/codex-agent.sh codex-subagent --effort low <<< "Reply with exactly: PONG-SUBAGENT"
```

実装用の 3 定義の監査行に `codex_home=.../.codex-subagent` が出ることを確認する。
レビュー用の `codex-review` の監査行に `codex_home=.../.codex-review` が出ることを確認する。
`codex-review` では `sandbox=read-only`、`codex-subagent` では `sandbox=workspace-write` が出ることを確認する。
4 つすべての応答の末尾に `codex-agent: result=ok` が出ればよい。
Claude Code の `Agent` ツールからも、4 つの `subagent_type` をそれぞれ指定して確認する。

## 設定に関する注意

`CODEX_HOME` を分けると、既定ホーム `~/.codex` の `config.toml` は読み込まれない。
モデルと effort は `.claude/gpt-agents/` 側で指定するため、各 `CODEX_HOME` に設定ファイルがなくても動く。
追加設定が必要な場合だけ、用途ごとのホームに個別に設定する。
既定ホームの `config.toml` をそのままコピーすると、フック、MCP サーバ、通知などの設定まで持ち込まれるため避ける。

Claude Code の Codex プラグイン(`codex:codex-rescue` など)は、この仕組みとは別に動く。
プラグインはセッション共有の broker 経由で `codex app-server` を起動し、broker プロセスの環境変数を起動時に固定する。
呼び出しごとに `CODEX_HOME` を切り替える用途には向かないため、用途別アカウント運用はこのリポジトリの定義で行う。

Windows で定義ファイルのモデル、effort、GPT 系サブエージェント経路の有効状態を変更する場合は、[設定コンソールの使い方](gui.md) に従って `gui/CodexBridgeConsole/CodexBridgeConsole.csproj` を `gui/dist` へ発行し、生成された `CodexBridgeConsole.exe` を起動する。
