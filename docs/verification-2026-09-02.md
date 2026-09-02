# ChatGPT 回答の妥当性検証と動作確認(2026-09-02)

ChatGPT に「複数の ChatGPT Plus アカウントを Codex CLI から用途別に使い分け、Claude Code から呼び分けられるか」を質問し、[その回答](chatgpt-answer-2026-09-02.md)を得た。
本文書は、その回答の技術的主張をローカル環境と公式ソースで裏取りした結果と、サブエージェント定義、呼び出しの最小限の動作確認の記録である。

検証環境は Windows 10、codex-cli 0.152.1、Claude Code(Fable 5.1)である。
ソースの参照先は openai/codex の main ブランチ(2026-09-02 時点)である。

## 結論

回答の中核である「`CODEX_HOME` を分離すればアカウントを用途別に分けられる」は正しく、実測でも確認できた。
Claude Code のサブエージェント定義から、別 `CODEX_HOME` の Codex を呼び出して応答を得ることにも成功した。
ただし、回答が触れていない制約が三つある(後述)。

## 正しいと確認できた主張

認証情報は `$CODEX_HOME/auth.json` にのみ保存される。
ソース上、認証ファイルのパスを返す関数は `codex_home.join("auth.json")` を返すだけで、他の場所を参照しない。
OS のキーリングに保存するモードでも、ストアのキーは `CODEX_HOME` の正規化パスの SHA-256 から作られるため、ディレクトリごとに分離される。

実測でも、`CODEX_HOME` を空ディレクトリに向けると `codex login status` は「Not logged in」となり、`codex exec` は 401 で失敗した。
既定の `~/.codex` へのフォールバックは起きない。

セッションログ、履歴、SQLite、`thread-writer-locks` などのロックファイルも、すべて `CODEX_HOME` 配下に作られた。
`AppData\Local\Codex` と `AppData\Roaming\Codex` にもディレクトリがあるが、中身はデスクトップアプリのログと Web 資産であり、認証には関係しない。

`--profile` は `$CODEX_HOME/<name>.config.toml` を重ねるだけで、認証は分離しない。
`codex exec --ephemeral` のヘルプにも「auth still uses CODEX_HOME」と明記されている。
回答の「`--profile` より `CODEX_HOME` 分離を優先する」という判断は正しい。

## 回答が触れていない制約

**導入済みの Codex プラグインは使えない。**
Claude Code の Codex プラグイン(`codex-rescue` エージェント)は `codex exec` ではなく、セッション共有の broker 経由で `codex app-server` を起動する。
broker プロセスの環境変数は起動時に固定されるため、呼び出しごとに `CODEX_HOME` を切り替える構成には向かない。
用途別アカウント運用は、プラグインを介さず `codex exec` を直接呼ぶ独自のエージェント定義で行う必要がある。

**`CODEX_HOME` を一時ディレクトリ配下に置くと警告が出る。**
Codex は `E:\Temp` 配下にはヘルパーバイナリを作らず、警告を出した(処理は継続した)。
回答どおり `%USERPROFILE%` 配下に置くのが正しい。

**新しいエージェント定義はセッション再起動後に有効になる。**
定義ファイルを作成した直後に `Agent` ツールから `subagent_type: codex-review` を指定したところ、「not found」で失敗した。
Claude Code はエージェント定義をセッション開始時に読み込むためである。

## 回答に対する補正

ラッパースクリプト(`C:\Tools\*.ps1`)は必須ではない。
Claude Code のエージェント定義の本文で `CODEX_HOME` を設定してから `codex exec` を呼べば足りる。
ただし、エージェント定義のフロントマターに環境変数を書く仕組みはないため、設定は Bash コマンド側で行う。

同時実行の懸念は小さい。
ロックファイルは `CODEX_HOME` 内に閉じているため、ホームを分ければ競合しない。
作業ツリーの衝突は、レビュー側を `--sandbox read-only` にし、書き込み側は別の worktree で動かせば回避できる。

規約面は技術検証の対象外だが、回答の「役割固定は問題なし、上限回避のローテーションは避ける」という整理に反する情報は見当たらなかった。
最終判断は利用規約の原文確認による。

## 動作確認の手順と結果

第 2 アカウントのログインはブラウザ認証が必要で自動化できないため、既存アカウントの `auth.json` を別の `CODEX_HOME` に複製し、「アカウント A を別ホームから呼ぶ」機構を検証した。
複製した `auth.json` は検証後に削除した。

1. 別ホームで `codex login status` を実行し、「Logged in using ChatGPT」を確認した。
2. 別ホームで `codex exec --sandbox read-only -C <作業ディレクトリ> "Reply with exactly: PONG-REVIEW-HOME"` を実行し、`PONG-REVIEW-HOME` を得た。セッションログは別ホームの `sessions/` にのみ書かれた。
3. サブエージェント定義(`codex-review.md`)を作成した。同セッションでは登録されていなかったため、汎用サブエージェントに定義本文を与えて実行し、`PONG-VIA-CLAUDE-SUBAGENT` を得た。別ホームのセッションログが 2 件に増えたことを確認した。

以上から、`CODEX_HOME` 分離による呼び分けは、Claude Code のサブエージェント経由でも成立する。
