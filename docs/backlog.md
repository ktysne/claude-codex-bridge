# 残作業と判断事項

2026-09-03 時点の残作業と、開発者の判断を要する事項をまとめる。
判断事項には、作業を止めないために採用した暫定の選択を添える。

## 残作業

1. アカウント A、B でそれぞれ `codex login` を実行する(ブラウザ認証のため手作業)。手順は [setup.md](setup.md)。
2. 各 `CODEX_HOME` に最小の `config.toml` を置く。
3. 利用先プロジェクトにエージェント定義をコピーし、Claude Code を再起動して `subagent_type` での直接呼び出しを確認する。
4. `codex-subagent` の書き込み先を worktree で分離する運用を、利用先プロジェクトの `CLAUDE.md` に書く。
5. 実運用で Codex CLI を更新したとき、`CODEX_HOME` の扱いが変わっていないかを `codex exec --help` の `--ephemeral` の説明で再確認する。

## 開発者の判断を要する事項

**サブエージェントの薄いラッパーに使うモデル。**
暫定で `haiku` にしている。
ラッパーは Bash を 1 回呼ぶだけで推論を要さないため、最も安価なモデルで足りるという判断である。
依頼文の整形(重大度の付与を補う、など)をもう少し任せたいなら `sonnet` に上げる。

**エージェント定義の置き場所。**
暫定でプロジェクト単位(`<project>/.claude/agents/`)にしている。
全プロジェクトで使うなら `%USERPROFILE%\.claude\agents\` に置く。
既存の `impl-*` 定義と並ぶため、名前の衝突はない。

**`codex-subagent` の既定サンドボックス。**
暫定で `workspace-write` にしている。
承認を自動化する `--approve-for-me` は付けていない。
実装タスクで承認待ちが頻発するようなら付けるかを判断する。
`--dangerously-bypass-approvals-and-sandbox` は採用しない。

**既定ホーム(`~/.codex`)のアカウントの扱い。**
現在の既定ホームは Codex プラグインと ai-cross-review が使っている。
アカウント A をレビュー用に固定するなら、既定ホームとレビュー用ホームを同じアカウントにするか、既定ホームを第 3 の用途(対話用)と位置づけるかを決める。
暫定では既定ホームを触らず、用途別ホームを追加する構成にしている。

**ai-cross-review 側の `CODEX_HOME` 対応。**
ai-cross-review はレビューに `codex` を直接起動するため、環境変数 `CODEX_HOME` を設定してから `npm run review:codex` を実行すればレビュー用アカウントで動く見込みである。
ただし未検証であり、npm script に `CODEX_HOME` を埋め込むか、実行時に環境変数で渡すかは決めていない。
