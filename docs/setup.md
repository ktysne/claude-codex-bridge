# セットアップ手順

用途別の ChatGPT アカウントで Codex CLI にログインし、Claude Code のサブエージェントとして呼べるようにするまでの手順である。
現段階では 4 定義とも既定ホーム `~/.codex` を使うため、手順 1〜2 は不要である。
手順 1〜2 は 2 アカウント段階に進むときの手順として記載する。
前提として、Codex CLI(検証時は 0.152.1)が PATH にあり、Windows 上で PowerShell または Git Bash を使う。

## 1. 2 アカウント段階で用途ごとの CODEX_HOME でログインする

`CODEX_HOME` は `%USERPROFILE%` 配下に置く。
一時ディレクトリ配下に置くと、Codex がヘルパーバイナリの作成を拒否して警告を出す(処理は継続するが、避けたほうがよい)。

レビュー用(アカウント A)は PowerShell で次を実行し、開いたブラウザでアカウント A にログインする。

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-review"; codex login
```

実装補助用(アカウント B)も同様に、アカウント B でログインする。

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-subagent"; codex login
```

ログイン後、それぞれの `CODEX_HOME` で状態を確認する。
どちらも「Logged in using ChatGPT」と表示されればよい。

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-review"; codex login status
$env:CODEX_HOME="$env:USERPROFILE\.codex-subagent"; codex login status
```

ブラウザのアカウント切り替えを忘れると、両方が同じアカウントになる。
`auth.json` 内の `tokens.account_id` が二つのホームで異なることを確認しておくと確実である。

## 2. 2 アカウント段階で各 CODEX_HOME の設定を置く

`CODEX_HOME` を分けると、既定ホーム `~/.codex` の `config.toml` は読まれない。
モデルと effort は `.claude/gpt-agents/` 側で指定するため、`CODEX_HOME` 側の設定ファイルは無くても動く。
追加設定が必要な場合だけ、各ホームに設定ファイルを置く。

既定ホームの `config.toml` をそのままコピーするのは避ける。
フック、MCP サーバ、通知などの設定が用途別ホームに持ち込まれ、切り分けが難しくなるためである。

## 3. エージェント定義を配置する

このリポジトリの `.claude/agents/` にある 4 定義を、使いたいプロジェクトの `.claude/agents/` にコピーする。
`.claude/gpt-agents/` にある 4 定義と `tools/codex-agent.sh` も、同じプロジェクトへコピーする。
すべてのプロジェクトで使うなら、`agents/` の 4 定義、`gpt-agents/` の 4 定義、`tools/codex-agent.sh` の 3 箇所(`%USERPROFILE%\.claude\agents\`、`%USERPROFILE%\.claude\gpt-agents\`、`%USERPROFILE%\.claude\tools\`)をそろえて置く。

## 4. Claude Code を再起動する

エージェント定義はセッション開始時に読み込まれる。
配置しただけでは `Agent` ツールから見えないので、Claude Code を再起動する。
再起動後、利用可能なエージェント一覧に `codex-review`、`codex-subagent`、`impl-light`、`impl-standard` が並ぶ。

## 5. 動作を確認する

Git Bash で `tools/codex-agent.sh` を直接呼び出し、次のコマンドを実行する。

```bash
bash tools/codex-agent.sh codex-review --effort low <<< "Reply with exactly: PONG-REVIEW"
```

監査行に `model=gpt-5.6-sol`、`sandbox=read-only`、使用中の段階のホームを示す `codex_home=...`(現段階は `~/.codex`、2 アカウント段階はレビュー用が `~/.codex-review`)が出ることを確認する。
続いて `PONG-REVIEW` と `codex-agent: result=ok` が出れば、レビュー用の定義が動いている。
`codex-subagent` は `sandbox=workspace-write` になる点を除き、同じ方法で確認する。
この確認は応答を返すだけでファイルを書き換えないため、`-C` は省略してよい。
実際の依頼では、`codex-subagent` の Claude 側定義が別 worktree の `-C` を必須にしている。

Claude Code を再起動した後、`Agent` ツールで `subagent_type: codex-review` を指定し、`Reply with exactly: PONG-REVIEW` を送る。
`PONG-REVIEW` が返り、その `CODEX_HOME` の `sessions/` にログが増えることを確認する。

## 導入済み Codex プラグインとの関係

Claude Code の Codex プラグイン(`codex:codex-rescue` など)は、この仕組みとは併用しない。
プラグインはセッション共有の broker 経由で `codex app-server` を起動し、broker プロセスの環境変数は起動時に固定される。
呼び出しごとに `CODEX_HOME` を切り替える用途には向かないため、用途別アカウント運用はこのリポジトリのエージェント定義で行う。
プラグインは既定ホーム(`~/.codex`)のアカウントで従来どおり動き続ける。
