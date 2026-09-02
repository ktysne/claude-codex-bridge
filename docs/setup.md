# セットアップ手順

用途別の ChatGPT アカウントで Codex CLI にログインし、Claude Code のサブエージェントとして呼べるようにするまでの手順である。
前提として、Codex CLI(検証時は 0.152.1)が PATH にあり、Windows 上で PowerShell または Git Bash を使う。

## 1. 用途ごとの CODEX_HOME でログインする

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

## 2. 各 CODEX_HOME の設定を最小にする

新しい `CODEX_HOME` には `config.toml` が存在しない。
既定の `~/.codex/config.toml` は読まれないため、必要な設定だけを各ホームに置く。
最小構成は次のとおりである。

```toml
model = "gpt-5.6-sol"
model_reasoning_effort = "medium"
```

既定ホームの `config.toml` をそのままコピーするのは避ける。
フック、MCP サーバ、通知などの設定が用途別ホームに持ち込まれ、切り分けが難しくなるためである。

## 3. エージェント定義を配置する

このリポジトリの `.claude/agents/codex-review.md` と `.claude/agents/codex-subagent.md` を、使いたいプロジェクトの `.claude/agents/` にコピーする。
すべてのプロジェクトで使うなら `%USERPROFILE%\.claude\agents\` に置いてもよい。

## 4. Claude Code を再起動する

エージェント定義はセッション開始時に読み込まれる。
配置しただけでは `Agent` ツールから見えないので、Claude Code を再起動する。
再起動後、利用可能なエージェント一覧に `codex-review` と `codex-subagent` が並ぶ。

## 5. 動作を確認する

Claude Code から `Agent` ツールで `subagent_type: codex-review` を指定し、次のような依頼を渡す。

```text
作業ディレクトリ: <任意のディレクトリ>
依頼文: Reply with exactly: PONG-REVIEW
```

`PONG-REVIEW` が返り、`%USERPROFILE%\.codex-review\sessions\` にセッションログが増えていれば、レビュー用ホームで実行されている。
`codex-subagent` も同様に確認する。

## 導入済み Codex プラグインとの関係

Claude Code の Codex プラグイン(`codex:codex-rescue` など)は、この仕組みとは併用しない。
プラグインはセッション共有の broker 経由で `codex app-server` を起動し、broker プロセスの環境変数は起動時に固定される。
呼び出しごとに `CODEX_HOME` を切り替える用途には向かないため、用途別アカウント運用はこのリポジトリのエージェント定義で行う。
プラグインは既定ホーム(`~/.codex`)のアカウントで従来どおり動き続ける。
