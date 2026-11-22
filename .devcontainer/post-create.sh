#!/usr/bin/env bash
# devcontainer.json の postCreateCommand。コンテナーを作ったときに 1 回だけ、リポジトリのルートで動く。
set -euo pipefail

# 1. Claude Code の設定のボリューム（~/.claude）。新しいボリュームは root の持ち物なので、vscode ユーザーが書けるようにする
sudo chown vscode:vscode "$HOME/.claude"

# 2. .NET（拡張機能を除いたプロジェクト）
dotnet restore StationCountdown.CrossPlatform.slnf

# 3. APM（Agent Package Manager）。バージョンは apm.lock.yaml を作ったものに合わせる。
#    公式のインストーラーで ~/.local/bin に入れる。PATH は devcontainer.json の remoteEnv で通すので、
#    シェルのプロファイルは書き換えさせない（APM_NO_MODIFY_PATH=1）
apm_version="$(sed -n "s/^apm_version: *['\"]\{0,1\}\([0-9.]*\).*/\1/p" apm.lock.yaml)"
[ -n "$apm_version" ] || { echo "apm.lock.yaml から apm_version を読めませんでした" >&2; exit 1; }
apm="$HOME/.local/bin/apm"
if ! "$apm" --version 2>/dev/null | grep -qF "$apm_version"; then
  curl -fsSL https://aka.ms/apm-unix | APM_NO_MODIFY_PATH=1 sh -s -- "@v$apm_version"
fi

# 4. Claude Code 用のスキル・フック（.claude/。git には入れていない）を、apm.lock.yaml のとおりに入れる
"$apm" install --frozen
