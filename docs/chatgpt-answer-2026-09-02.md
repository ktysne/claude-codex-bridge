# ChatGPT Plus 複数契約と Codex CLI 複数アカウント運用メモ

作成日: 2026-09-02

## 目的

この文書は、以下の検討内容を Claude Code 等で技術検証するために整理したものです。

- ChatGPT Plus を複数アカウントで契約することの利用規約上の扱い
- PC 上で複数の ChatGPT Plus アカウントを Codex CLI から用途別に使い分ける構成
- Claude Code から Codex CLI をレビュー用途／サブエージェント用途として呼び分ける構成

---

## 1. ChatGPT Plus を複数契約することについて

2026-09-02 時点で確認した範囲では、**同一人物が複数の ChatGPT アカウントを持ち、それぞれで ChatGPT Plus を契約すること自体を明示的に禁止する規定は確認できませんでした。**

OpenAI は ChatGPT Web で複数アカウントの切り替え機能を提供しており、会話履歴・設定・請求・サブスクリプションはアカウントごとに分離されます。

参考:

- OpenAI Help: Account switching
  - https://help.openai.com/en/articles/20001068
- OpenAI Help: ChatGPT subscription transfer
  - https://help.openai.com/ja-jp/articles/9135236-can-i-transfer-my-chatgpt-subscription-to-a-new-account

### 注意点

OpenAI の利用規約では、rate limits や各種 restrictions を回避する行為が禁止されています。

参考:

- OpenAI Terms of Use
  - https://openai.com/policies/terms-of-use/

したがって、以下は分けて考える必要があります。

### 用途固定での複数アカウント運用

例:

- Account A: コードレビュー専用
- Account B: サブエージェント／実装補助専用

このように用途を固定し、恒常的に別役割として使う構成は、複数アカウント運用そのものとしては直ちに禁止とは確認できませんでした。

### 利用上限回避を目的としたローテーション

例:

- Account A の Codex 利用枠を使い切る
- Account B に切り替える
- Account B も使い切ったら Account C に切り替える

このような使い方は、**rate limit や利用制限の circumvention と解釈されるリスクがあります。**

そのため、「複数契約 = 利用可能量を自由に合算できる」とは考えない方が安全です。

---

## 2. Codex CLI で複数アカウントを用途別に使い分ける構成

技術的には、**`CODEX_HOME` をアカウントごとに分離する構成**が有力です。

Codex CLI は ChatGPT アカウントでログインでき、認証情報や設定は `CODEX_HOME` 配下に保存されます。

公式リポジトリ:

- https://github.com/openai/codex

関連実装の確認先:

- `auth.json` の保存先など
  - https://github.com/openai/codex/blob/main/codex-rs/config/src/types.rs
- 設定ロード周り
  - https://github.com/openai/codex/blob/main/codex-rs/config/src/loader/mod.rs
- 認証ストレージ周り
  - https://github.com/openai/codex/blob/main/codex-rs/login/src/auth/storage.rs

想定される保存構造:

```text
$CODEX_HOME/auth.json
$CODEX_HOME/config.toml
```

このため、Windows 上で例えば以下のように分離できます。

```text
C:\Users\<User>\.codex-review
C:\Users\<User>\.codex-subagent
```

---

## 3. Windows での初期セットアップ例

### Account A: レビュー用

PowerShell:

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-review"
codex login
```

ブラウザが開いたら Account A でログインします。

### Account B: サブエージェント用

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-subagent"
codex login
```

ブラウザが開いたら Account B でログインします。

結果として、概念的には以下のようになります。

```text
%USERPROFILE%\.codex-review\auth.json
  -> Account A

%USERPROFILE%\.codex-subagent\auth.json
  -> Account B
```

---

## 4. Claude Code から用途別に Codex CLI を呼び分ける

用途ごとに PowerShell のラッパースクリプトを作る構成がシンプルです。

### レビュー用ラッパー

例: `C:\Tools\codex-review.ps1`

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-review"
codex exec @args
```

### サブエージェント用ラッパー

例: `C:\Tools\codex-subagent.ps1`

```powershell
$env:CODEX_HOME="$env:USERPROFILE\.codex-subagent"
codex exec @args
```

Claude Code からは、それぞれ別コマンドとして呼び出せます。

### レビュー用途

```powershell
powershell -File C:\Tools\codex-review.ps1 "Review the current git diff..."
```

### サブエージェント用途

```powershell
powershell -File C:\Tools\codex-subagent.ps1 "Implement the following task..."
```

---

## 5. 同時実行について

`CODEX_HOME` を分けることで、認証情報や設定を別ディレクトリに保持できます。

想定構成:

```text
Claude Code
├─ Process 1
│  ├─ CODEX_HOME=.codex-review
│  └─ Codex / Account A
└─ Process 2
   ├─ CODEX_HOME=.codex-subagent
   └─ Codex / Account B
```

この形であれば、同時実行時にも同一 `auth.json` や同一設定ファイルを共有しないため、アカウント切替競合を避けやすくなります。

ただし、Claude Code 側から同時起動する場合は、以下を追加で検証する価値があります。

- Codex CLI がセッションロックや一時ファイルを `CODEX_HOME` 外に作成しないか
- 同時実行時に Git worktree や working directory が衝突しないか
- 各 Codex プロセスの cwd が適切に分離されるか
- Claude Code からの標準出力／標準エラー取り回し
- タイムアウト処理
- 終了コードの扱い

---

## 6. `--profile` との違い

Codex には `--profile` 系の設定切り替えがありますが、今回の用途では **アカウント認証自体を分離したいため、`CODEX_HOME` 分離の方が適しています。**

考え方:

```text
CODEX_HOME
  -> アカウント・認証・ベース設定の分離

profile / config.toml
  -> 同一アカウント内での設定差分
```

したがって、例えば以下だけでは認証アカウント分離用途としては不十分な可能性があります。

```bash
codex --profile review
codex --profile subagent
```

アカウントごとの完全な分離が必要なら、まず `CODEX_HOME` を分ける方が安全です。

---

## 7. 想定アーキテクチャ

```text
Developer
  |
  +-- Claude Code main agent
        |
        +-- Codex Review Agent
        |     +-- CODEX_HOME=.codex-review
        |     +-- ChatGPT Plus Account A
        |
        +-- Codex Subagent
              +-- CODEX_HOME=.codex-subagent
              +-- ChatGPT Plus Account B
```

### Account A: Review Agent

想定役割:

- git diff レビュー
- Swift / iOS コードレビュー
- バグ検出
- テスト不足検出
- 設計レビュー

推奨制約:

- 原則コードを書き換えない
- 問題点と改善案を返す
- severity を付ける
- 必要に応じてファイル名／行単位で指摘する

### Account B: Subagent

想定役割:

- 技術調査
- 実装案作成
- テスト作成
- リファクタリング案
- Claude Code から委譲された独立タスク

---

## 8. Claude Code 側で追加検証したい技術項目

この構成を実運用する前に、以下を Claude Code で検証するとよいです。

### Codex CLI 側

1. `CODEX_HOME` が認証情報の分離に十分か
2. `auth.json` 以外にアカウント依存データが保存される場所がないか
3. セッション情報の保存場所
4. 同時実行時のロックファイル有無
5. Windows PowerShell からの環境変数継承挙動
6. `codex exec` 実行時に親プロセスの環境変数だけで完全分離できるか
7. CLI アップデート後もこの挙動が維持されるか

### Claude Code 側

1. Tool 定義または custom command から複数ラッパーを呼び分けられるか
2. Review Agent / Subagent を明示的にルーティングできるか
3. 並列実行可能か
4. Git リポジトリの同一 worktree を複数 AI が同時編集しないよう制御できるか
5. Review Agent に read-only 相当の運用制約を与えられるか
6. Codex の出力を Claude Code が構造化して扱えるか
7. 失敗時のリトライやフォールバック設計

---

## 9. 規約面で避けるべき運用

以下は避けた方がよいです。

```text
Account A の利用枠枯渇
  -> Account B に切替
    -> Account B の利用枠枯渇
      -> Account C に切替
```

これを「利用制限を突破するための仕組み」として構築すると、OpenAI 利用規約の rate limits / restrictions の circumvention に抵触する可能性があります。

より安全な設計は以下です。

```text
Account A = Review 専用
Account B = Subagent 専用
```

役割を固定し、それぞれ独立した用途として使います。

---

## 10. 現時点での結論

### 規約

- 複数 ChatGPT アカウントの所有自体は、明示的な禁止を確認できなかった
- 各アカウントで Plus を個別契約すること自体も、明示的な禁止を確認できなかった
- ただし利用上限や rate limit を回避する目的で複数アカウントをローテーションするのは避けるべき

### 技術

- Codex CLI は `CODEX_HOME` を分けることで、複数アカウントを用途別に分離できる可能性が高い
- Windows では PowerShell ラッパーを用途ごとに用意するのが扱いやすい
- Claude Code から review / subagent の2系統として呼び分ける構成が可能
- `--profile` よりも `CODEX_HOME` 分離を優先した方がよい

### 推奨構成

```text
Claude Code
├─ codex-review.ps1
│  └─ CODEX_HOME=.codex-review
│     └─ ChatGPT Plus Account A
└─ codex-subagent.ps1
   └─ CODEX_HOME=.codex-subagent
      └─ ChatGPT Plus Account B
```

この構成をベースに、Codex CLI の現在のソースコードと Windows 実環境で、認証分離・同時実行・セッション保存場所を追加検証するのがよいです。
