## インストール・更新

1. 下の **Assets** から `StationCountdown-<バージョン>.zip` をダウンロードし、すべて展開する
2. `install.cmd` をダブルクリックする
   - 初回だけ、署名の証明書（`CN=Laplusdestiny`、自己署名）を「信頼されたユーザー」に登録するため、管理者の確認が表示されます
3. Command Palette で `Reload`（拡張機能の再読み込み）を実行する
4. 「駅カウントダウン」→「＋ 阪急の駅を追加」などで駅を追加し、Dock の編集モードでバンドをピン留めする

必要なもの: Windows 10 2004 以降（x64 / ARM64）、PowerToys 0.98 以降（Command Palette の Dock を有効にする）。
設定と時刻表（`%LOCALAPPDATA%\StationCountdown`）は、更新やアンインストール（`uninstall.cmd`）で消えません。
zip の SHA-256 は `StationCountdown-<バージョン>.zip.sha256` にあります。
