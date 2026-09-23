# プロジェクトガイドライン

このファイルは AI コーディングエージェント(Claude Code など)向けの共通指示を記載する。

## このプロジェクトが扱うもの

Claude Code から Codex CLI を、用途別のサブエージェントとして呼び出す仕組みを整備する。
通常利用とレビュー用、サブエージェント用で ChatGPT アカウントを分け、`CODEX_HOME` を分離して認証を切り替える。
全体像は [README.md](README.md)、サブエージェントの構成と設計上の制約は [docs/gpt-agents.md](docs/gpt-agents.md) を参照。
運用の測定結果と、そこから直した内容の記録は [docs/gpt-agent-log-review-2026-09-16.md](docs/gpt-agent-log-review-2026-09-16.md) にある。測り直しの規則も同じ文書にある。
定義ファイルを GUI から書き換える設定コンソール(`gui/`、Windows Forms、.NET Framework 4.8)も含む。使い方は [docs/gui.md](docs/gui.md)、設計は [docs/gui-console-design.md](docs/gui-console-design.md) を参照。

## 言語

このプロジェクトでは日本語を共通言語とする。具体的には以下をすべて日本語で書くこと。

- セッション中の応答、説明、報告などの出力
- コミットメッセージ
- Pull Request のタイトルと本文
- Issue やレビューコメントなど、リポジトリ上のやり取り

コード中の識別子(変数名、関数名など)は慣例どおり英語でよい。コメントやドキュメントは日本語で書く。

例外: `.bat` など cmd.exe が解釈するスクリプトのコメントと表示メッセージは ASCII(英語)で書く。cmd のバッチパーサは UTF-8 の日本語をコマンドとして誤解釈し、実行自体が失敗するためである。

ユーザが目にする日本語の文書(docs 配下の文書、README など)を書く、または推敲するときは、`/japanese-tech-writing` スキルを使い、その文章規範に従うこと。

## 守るべき設計原則

- 用途固定の原則: アカウント A は通常利用とレビュー、アカウント B はサブエージェント専用とする。利用上限の回避を目的にアカウントを切り替える構成(枠が尽きたら別アカウントへ回す、など)は導入しない。OpenAI の利用規約が禁じる rate limit の回避と解釈される余地があるためである。ここでいう**通常利用**とは、Codex CLI の対話、VS Code や Chrome の Codex 拡張、Claude Code の Codex プラグイン(`codex:codex-rescue` など)を指す。**サブエージェント**とは、GPT 側へ実装を委譲する 4 定義(`impl-hard`、`impl-light`、`impl-standard`、`codex-subagent`)を指す。`codex-review` も Claude Code からはサブエージェントとして起動されるが、役割はレビューなのでアカウント A に置く。この原則が固定するのは 5 定義とアカウントの対応であり、既定ホーム側での通常利用は対象外である。
- 認証ホームの配置: アカウント A を既定ホーム `~/.codex` に置き、アカウント B に `~/.codex-subagent` を与える。`codex-review` は `~/.codex` を、他の 4 定義(`impl-hard`、`impl-light`、`impl-standard`、`codex-subagent`)は `~/.codex-subagent` を使う。アカウント A を既定ホームに置くのは、通常利用の入口が既定ホームしか見ないためである。既定ホームを空けると通常利用が未ログイン扱いになり、`~/.codex` が自動で再生成される。
- 認証の分離: `codex` を呼ぶときは必ず `CODEX_HOME` を明示する。既定の `~/.codex` に暗黙に依存する呼び出しを書かない。
- 権限の固定: レビュー用は `--sandbox read-only` を外さない。実装補助用でも `--dangerously-bypass-approvals-and-sandbox` は使わない。設定コンソールからも `codex_sandbox` は編集させない。`codex_home` は設定コンソールから変えられるが、`%USERPROFILE%` 直下に実在する `.codex*` ディレクトリから選ぶだけで、任意のパスは入力させない。
- 認証情報の非コミット: `auth.json`、トークン、アカウント ID をリポジトリに入れない。ドキュメントの例には実値を書かない。
- 委譲の検証: `codex-review` と `codex-subagent` の報告は、Codex を起動できた場合は `codex-agent: agent=` の監査行と `codex-agent: result=` の行、起動する前に止まった場合は `codex-agent:` の理由行を含む。どれも含まない報告は Codex を経由していないため、受け取らずに委譲をやり直す。ただし、冒頭に「進行中」と書かれ、`codex-agent: agent=` の監査行と、`codex-agent: run=` の行または Bash ツールが示した出力ファイルのパスを含む報告は、Codex の実行中にターンを終えた未完了の報告である(`impl-hard`、`impl-light`、`impl-standard` の報告も同じ)。`run=` の行に代えて出力ファイルのパスを認めるのは、古いラッパーが実行中に `run=` の行を出さないためである。この条件を満たさない「進行中」の報告は、Codex を経由した根拠も残った実行を探す手がかりも無いので、無効な報告として扱う。未完了の報告は、完了の報告としては受け取らず、委譲をやり直す理由にもしない。そのまま委譲をやり直すと、同じ作業ツリーに 2 つの Codex が書き込むおそれがあるためである。未完了の報告を受けたら、同じエージェントの次の通知を待つ。サブエージェントは Codex が終わると通常は自動で再開し、その通知で最終報告を返す。待つあいだ、作業ツリーの差分やプロセスを定期的に監視しない。次の通知の前に状態を確かめる必要があるときは、報告に添えられた案内に従い、ファイルの内容を結果として読まずに、サブエージェントへ状態確認のメッセージを送る。終了コードごとの扱い(`impl-hard`、`impl-light`、`impl-standard` の Claude 側へのフォールバックなど)は、サブエージェントの定義にしか無いためである。それでも委譲をやり直すときは、その前に、実行が終わったか止まったかを [docs/gpt-agents.md](docs/gpt-agents.md) の既知の制約の手順で確かめる。
- 実装担当の操作範囲: サブエージェント(`impl-hard`、`impl-light`、`impl-standard`、`codex-subagent`)と、それらが委譲する GPT 側の実行が行うのは、作業ツリーの変更と検証までである。コミット、push、git の履歴やブランチを変える操作、PR や Issue への投稿は、依頼文が求めても行わない。これらはメインセッションが差分を読み、検証コマンドを再実行して監査したうえで行う。依頼文による例外を設けないのは、メインセッションが書いた依頼文だけで実装担当の権限が増える構成を避けるためである。PR コメントの投稿やブランチの操作のような実装でない作業は、実装担当へ回さない。GPT への委譲を止めて Claude 側で実装させたいときは、依頼文の最初の行に `委譲: Claude 側で実装` を置き、直後の `理由:` で始まる行に理由を書く。書き方をこの形に決めておくのは、表現が揺れると実装担当が定義と依頼文のどちらに従うかを裁定することになるためである。指定を最初の行に限るのは、依頼文に引用した定義や文書に同じ行が含まれていても、指定と取り違えないためである。
- 作業ツリーの分離: 1 つの worktree に同時に書き込む担当は 1 つとする。担当には、メインセッション、実装担当(`impl-hard`、`impl-light`、`impl-standard` と、それらが委譲する GPT 側の実行)、`codex-subagent`、`npm run review:codex:fix` を含む。担当するファイルを分けても、ビルドの生成物、テストの実行、git の索引は worktree の中で共有されるためである。並行させたいときは担当ごとに別の worktree を使う。実装担当に同じ worktree を渡すあいだ、メインセッションはその worktree を編集しない。書き込み可能な Codex の起動は、同じ worktree に終わっていない別の実行の目印があると `codex-agent: warning=concurrent-writer` の行を出す。この警告は観測の補助で、起動は止めない。仕組み、限界、警告を受けたときの確かめ方は [docs/gpt-agents.md](docs/gpt-agents.md) の既知の制約にある。

## CLAUDE.md と AGENTS.md の同期

`CLAUDE.md` と `AGENTS.md` は同一内容を保つ。どちらか一方を変更した場合は、必ずもう一方にも同じ変更を反映すること。片方だけを更新した状態でコミットしてはならない。

## 検証コマンド

```bash
codex --version
CODEX_HOME="$USERPROFILE/.codex" codex login status  # 通常利用とレビュー(codex-review)
CODEX_HOME="$USERPROFILE/.codex-subagent" codex login status  # サブエージェント(impl-hard、impl-light、impl-standard、codex-subagent)
bash tools/codex-agent.sh impl-light --effort low <<< "Reply with exactly: PONG-LUNA"  # 実モデルを起動し利用枠を消費する
bash tools/codex-agent.sh codex-review --effort low <<< "Reply with exactly: PONG-REVIEW"  # 同上
dotnet build gui/CodexBridgeConsole.sln -c Release  # 設定コンソール
dotnet test gui/CodexBridgeConsole.sln -c Release
npm run test:codex-agent  # ラッパー(tools/codex-agent.sh)のテスト。偽の codex を使うので利用枠を消費しない。Git Bash から実行する
npm run test:metrics  # 集計スクリプト(tools/agent-log-metrics.js)のテスト。Git Bash から実行する
```

エージェント定義(`.claude/agents/*.md`)の変更は、そのディレクトリがセッション開始時から在れば数秒で次の委譲に反映される。再起動が要る条件は [docs/setup.md](docs/setup.md) の共通手順 6 にある。

## 相互レビュー

実装後は ai-cross-review で Claude と Codex の相互レビューを回す。手順は [docs/cross-review.md](docs/cross-review.md)、観点は `.cross-review.md` を参照。

```bash
npm run review:codex -- --uncommitted
npm run review:claude -- --uncommitted
```

## リモートセッション時の作業について

### モデル役割分担（メインセッションとサブエージェント）
メインセッションは設計・監査・レビューに専念し、実装は下位モデルのサブエージェント（Agent ツール）に切り出すことを基本とする。ただし、実装難易度が特に高い箇所はメインセッションが直接実装してよい。
- サブエージェントは `.claude/agents/` の `impl-hard`、`impl-standard`、`impl-light` から難易度で選ぶ。モデルと effort は定義側に持たせてあり、設定コンソールや手編集で変わりうるため、ここには書かない。
- 実装側のモデルはメインセッションと同等以下にする。メインセッションより上位のモデルを実装に使うと、監査側が実装側の判断を追えなくなる。
- 大きな依頼は、実装、テスト、文書更新などの短い段階に分けて委譲し、各段階の完了時にサブエージェントからメインセッションへ進捗を報告させる。
- 依頼文で GPT への委譲を止めない。止めるときは「守るべき設計原則」の「実装担当の操作範囲」にある指定の形式だけを使い、「Codex へは委譲しない」のような自由文や、コミットや push の指示を依頼文に書かない。
- 依頼文に git の語を入れない。担当はコミットや push を行わないので書く必要が無く、`isolation: "worktree"` で起動した担当では、git の語を含む依頼文の起動が隔離の検査で拒否される(検査が部分一致かは確かめていないため、`GitHub` のように git を含む語も避ける)。
- 報告を受けたら、「守るべき設計原則」の「委譲の検証」の条件で Codex を経由したかを確かめ、Claude 側で実装した場合はその理由を確かめる。理由が依頼文の指示によるものなら、開発者に伝える。
- 計画に沿って PR をスタック式に積む連続対応は、メインセッションの worktree で 1 件ずつ直列に委譲する。並行が要るときだけ担当ごとに別 worktree を使う。隔離した担当の流れは [docs/gpt-agents.md](docs/gpt-agents.md) の既知の制約にある。
- サブエージェントの待機がタイムアウトしただけでは、処理停止と判断しない。
- タイムアウト時は、エージェントの状態、作業ツリーの差分、実行中のツールやプロセスを確認する。
- 差分などから進行が確認できる間は、待機を継続する。
- サブエージェントが「進行中」や「完了の通知を待つ」と報告してターンを終えた場合は、その報告を完了として扱わない。
  - サブエージェントは、待っていた処理が終わると通常は自動で再開し、同じエージェントの次の通知で最終報告を返す。その通知を待つ。
  - 待つあいだ、作業ツリーの差分やプロセスを定期的に監視しない。サブエージェントが読むはずの出力やログも先に読まない。同じ報告を二重に読むことになるためである。
  - 状態を確かめる必要があるときは、報告に書かれた確かめ方に従う。書かれていなければ、上のタイムアウト時の確認を行う。
- 監視を置く場合は、変化のたびに通知する作りにしない。完了、失敗、停止のように判断が要る状態になったときだけ通知する。
- 進捗報告がなく、差分に変化がなく、実行中の処理もない状態が継続した場合に限り、中断または再開を検討する。判断に恣意的な固定時間を設けない。
- 各段階で同じ全テストを繰り返さない。担当中は関連するテストを実行し、変更完了後に全体検証をまとめて実施する。

### AIクロスレビュー（ai-cross-review）
相互レビューの手順の正本は [docs/cross-review.md](docs/cross-review.md)（vendored）と、グローバル SKILL `~/.claude/skills/cross-review/SKILL.md`（無い環境では vendored の [.claude/skills/cross-review/SKILL.md](.claude/skills/cross-review/SKILL.md)）である。
このリポジトリ固有のレビュー観点は `.cross-review.md` にある。
3 択、サーキットブレーカー、PR 運用といった汎用ルールはここに写さず、SKILL を参照する。

- 検証コマンド: `bash -n tools/codex-agent.sh`、`npm run test:codex-agent`、`npm run test:metrics`、`node tools/cross-review.js --help`、GUI は `dotnet build gui/CodexBridgeConsole.sln -c Release` と `dotnet test gui/CodexBridgeConsole.sln -c Release`。
- 基盤の更新: `npm run sync:cross-review`（検査は `npm run sync:cross-review:check`）で上流から取り込む。更新手順は「同期 → 表示された移行ノートの作業 → 上の検証コマンド」の順。
- レビューの起点: 既定のレビュアーは実装者と別のベンダーで、実装を一区切りしたら 3 択を `AskUserQuestion` で提示する（詳細は SKILL）。指摘、対応、妥当性確認は PR コメントに残し、本文は `.cross-review/round-<N>-triage.md` を書いて `node tools/cross-review.js comment --round <N>` で生成する。
