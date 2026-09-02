# 残作業と判断事項

2026-09-03 時点の残作業と、開発者の判断を要する事項をまとめる。
判断事項には、作業を止めないために採用した暫定の選択を添える。

## 完了した作業

- `codex-review`、`codex-subagent`、`impl-light`、`impl-standard` の 4 定義を、`.claude/gpt-agents/` の定義と `tools/codex-agent.sh` で `codex exec` を組み立てる方式に統一した(2026-09-03)。
- 4 定義、GPT 側の 4 定義、スクリプトの計 9 ファイルをユーザ定義側(`%USERPROFILE%\.claude\` 配下)に配置した(2026-09-03)。
- 4 定義すべてについて、Claude Code 再起動後に `subagent_type` からの直接呼び出しで、既定ホームの Codex を経由して応答することを確認した(2026-09-03)。`codex-review` はラッパーが Codex を呼ばずに応答した 1 回目の失敗を受けて定義を修正し、監査行付きの応答で再確認した。

## 残作業

1. 2 アカウント段階に進むとき、アカウント A、B でそれぞれ `codex login` を実行する(ブラウザ認証のため手作業)。手順は [setup.md](setup.md)。
2. 2 アカウント段階に進むとき、必要な設定を各 `CODEX_HOME` に置く。
3. 2 アカウント段階に進むとき、利用先プロジェクトに 4 定義とスクリプトをコピーし、Claude Code を再起動して `subagent_type` での直接呼び出しを確認する。
4. `codex-subagent` の書き込み先を worktree で分離する運用を、利用先プロジェクトの `CLAUDE.md` に書く。
5. 実運用で Codex CLI を更新したとき、`CODEX_HOME` の扱いが変わっていないかを `codex exec --help` の `--ephemeral` の説明で再確認する。
6. このリポジトリの定義やスクリプトを変えたときは、`.claude/agents/` の 4 ファイル、`.claude/gpt-agents/` の 4 ファイル、`tools/codex-agent.sh` の計 9 ファイルをユーザ定義側にも反映する。クラウド環境ではユーザ定義側が読まれないため、利用先リポジトリにコミットする必要がある。

## 開発者の判断を要する事項

**GPT 側の effort。**
暫定で `impl-light` を `xhigh`、`impl-standard` を `max` にしている。
既定ホームの `luna-xhigh-worker` と `luna-max-worker` の役割分担に合わせた値である。
実運用での応答時間と品質を見て見直す。

**`codex-review` と `codex-subagent` の GPT 側モデル。**
暫定で既定の GPT-5.6 Sol / medium としている。
実運用でのレビュー品質と応答時間を見て見直す。

**フォールバックの条件。**
`impl-light` と `impl-standard` は、GPT 側のレートリミット(終了コード 75)と GPT 側の未導入(終了コード 3)の 2 系統に限って Claude 側へフォールバックする。
未導入には、`codex` コマンドが PATH に無い場合、`.claude/gpt-agents/<name>.md` が見つからない場合、`tools/codex-agent.sh` 自体がどちらの置き場所にも無い場合が含まれる。
`codex-review` と `codex-subagent` は、終了コードが 0 以外ならフォールバックせず、終了コードと出力の末尾を報告して停止する。
それ以外の失敗は Claude 側で実装し直さず、終了コードと出力の末尾を報告して止める。
Codex 側の一時的な失敗まで自動でフォールバックすると、失敗の原因が報告に残らないためである。

**`impl-hard` は Claude 側のみ(暫定)。**
GPT 側へ委譲するのは `impl-light` と `impl-standard` に限り、`impl-hard` は Claude(Opus 5 / high)が担う。
設計判断を伴う変更や、正しさの検証が難しい変更は、メインセッションと同じ Claude 系に留めたほうが監査しやすいためである。
`impl-light` と `impl-standard` の委譲を実運用で見たうえで、`impl-hard` も委譲するかを判断する。

**Claude 側フォールバックに使うモデル。**
`impl-light` は Sonnet 5、`impl-standard` は Opus 5 で、ユーザ定義の値をそのまま保っている。
Claude 側は転送だけでなく実装も担うため、区分ごとの本来のモデルを下げていない。

**エージェント定義の置き場所。**
このリポジトリ(`.claude/`)とユーザ定義側(`%USERPROFILE%\.claude\`)の両方に同じ内容を置いている。
プロジェクト側の定義がユーザ定義側より優先されるため、利用先プロジェクトで定義を変えたい場合はプロジェクト側に置く。
クラウド環境ではユーザ定義側が読まれないため、利用先リポジトリにコミットする。

**`codex-subagent` のサンドボックス。**
暫定で `workspace-write` にしている。
承認を自動化する `--approve-for-me` は付けていない。
実装タスクで承認待ちが頻発するようなら付けるかを判断する。
`--dangerously-bypass-approvals-and-sandbox` は採用しない。

**既定ホーム(`~/.codex`)のアカウントの扱い。**
現在の既定ホームは Codex プラグインと ai-cross-review が使っている。
アカウント A をレビュー用に固定するなら、既定ホームとレビュー用ホームを同じアカウントにするか、既定ホームを第 3 の用途(対話用)と位置づけるかを決める。
暫定では既定ホームを触らず、用途別ホームを追加する構成にしている。

**`impl-light` の書き込み先。**
暫定で Claude Code と同じ worktree に書かせている。
`impl-light` に任せるのは小規模な変更であり、実行中にメインセッションが同じファイルを編集しない前提で運用する。
書き込み範囲の大きい依頼は `codex-subagent` に回し、worktree を分ける。

**`impl-light` が使う認証ホーム。**
暫定で既定ホーム `~/.codex` を使っている。
既定ホームにはフックや MCP サーバの設定が入っており、実行のたびにフックの出力が混じるため、`tools/codex-agent.sh` で `WARNING` と `hook:` の行を除いている。
将来は `~/.codex-subagent` に移し、この除去を不要にする。

**ai-cross-review 側の `CODEX_HOME` 対応。**
ai-cross-review はレビューに `codex` を直接起動するため、環境変数 `CODEX_HOME` を設定してから `npm run review:codex` を実行すればレビュー用アカウントで動く見込みである。
ただし未検証であり、npm script に `CODEX_HOME` を埋め込むか、実行時に環境変数で渡すかは決めていない。
