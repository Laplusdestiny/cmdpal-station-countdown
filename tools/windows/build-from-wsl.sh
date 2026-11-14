#!/usr/bin/env bash
# WSL から、Windows 側で拡張機能をビルド・登録する（VS Code を WSL に接続して使う場合のタスク用）。
#
# MSIX の登録は \\wsl.localhost\... のパスからはできないので、リポジトリを Windows のローカルフォルダーに写してから、
# tools/windows/Build-Extension.ps1 を Windows の PowerShell で実行する。引数はそのまま ps1 に渡す。
#
#   tools/windows/build-from-wsl.sh             # ビルドだけ
#   tools/windows/build-from-wsl.sh -Register   # ビルドして Command Palette 用に登録
#
# 写し先は既定で %USERPROFILE%\dev\cmdpal-station-countdown。環境変数 STATION_COUNTDOWN_WINDIR（Windows のパス）で変えられる。
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

# cmd.exe は作業フォルダーが UNC パスだと警告を出すので、Windows のドライブに移ってから呼ぶ。
win_profile="$(cd /mnt/c && cmd.exe /c 'echo %USERPROFILE%' | tr -d '\r')"
dest_win="${STATION_COUNTDOWN_WINDIR:-${win_profile}\\dev\\cmdpal-station-countdown}"
dest="$(wslpath -u "$dest_win")"

echo "Windows 側にリポジトリを写します: $dest_win"
mkdir -p "$dest"
rsync -a --delete \
    --exclude '.git/' --exclude 'bin/' --exclude 'obj/' --exclude 'out/' --exclude 'TestResults/' \
    "$repo/" "$dest/"

cd "$dest"
exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "${dest_win}\\tools\\windows\\Build-Extension.ps1" "$@"
