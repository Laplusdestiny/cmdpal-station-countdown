# Dock バンドの実機確認（Task 12 のゲート）

実装計画の Task 12 手順 5 で、Windows の実機で確かめる項目（spec §10 の V1〜V3・V6）の手順です。
あわせて、Task 12 で追加したマニフェストの設定（V7）も確かめます。結果は draft PR の「V1〜V6 の検証結果」の表に書きます。
もとは試作のバンドで確かめる予定でしたが、Task 13〜16 を先に進めたため、本番のバンドで確かめます。

## 準備

- PowerToys 0.98 以降。Command Palette の設定で Dock を有効にしておく
- Windows の「開発者モード」をオンにする（設定 → システム → 開発者向け。PowerShell で `start ms-settings:developers` を実行すると開く）
- Windows 側の .NET 10 SDK。管理者権限なしで入れる場合は、PowerShell で次を実行する（`%LOCALAPPDATA%\Microsoft\dotnet` に入る。ビルド用のスクリプトはここも探す）

  ```powershell
  $s = "$env:TEMP\dotnet-install.ps1"; Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $s; & $s -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet" -NoPath
  ```

- Visual Studio と Windows SDK は不要（VS Code で配置する場合）。Visual Studio で配置する場合は、Visual Studio 2022 17.14 以降または 2026 に、「.NET デスクトップ開発」と「Windows アプリケーション開発」のワークロードを入れる

## 配置

### VS Code で配置する場合

1. VS Code でリポジトリを開く（Windows で開いても、WSL に接続して開いてもよい）
2. コマンド パレットで「タスク: ビルド タスクの実行」（Ctrl+Shift+B）→「Extension: ビルドして Command Palette に登録」を選ぶ
   - 中身は `tools/windows/Build-Extension.ps1 -Register`。`dotnet build` で MSIX のレイアウトを作り、`Add-AppxPackage -Register` で登録する（Visual Studio の「配置」と同じこと）
   - WSL に接続している場合は、`tools/windows/build-from-wsl.sh` が先に `%USERPROFILE%\dev\cmdpal-station-countdown` へリポジトリを写す（MSIX は `\\wsl.localhost\…` からは登録できないため）
   - 動いている拡張機能のプロセスは、ビルドの前に止める（登録したパッケージはビルド出力のフォルダーを指していて、ファイルがロックされるため）
3. 下の「共通」の手順に進む

### Visual Studio で配置する場合

1. `feat/mvp` ブランチを取得し、`StationCountdown.sln` を Visual Studio で開く
2. 構成を `Debug` / `x64` にする（ツールバーの「Any CPU」を「x64」に変える）
3. ソリューション エクスプローラーで `StationCountdown.Extension` を右クリック →「配置」
4. 下の「共通」の手順に進む

### 共通

1. Command Palette を開き、`Reload` コマンド（「拡張機能を再読み込み」）を実行する
2. Command Palette で「駅カウントダウン」を開き、「＋ 阪急の駅を追加」などで駅を 1 つ以上追加する
3. Dock の編集モードで、追加した駅（例:「岡町 大阪梅田方面」）のバンドをピン留めする
   - Title が `急行 宝塚 あと3分` の形になり、毎分 0 秒ごろに残り分数が減る

ログは `%LOCALAPPDATA%\StationCountdown\logs\yyyyMMdd.log` に出ます（「ログフォルダを開く」でも開けます）。バンドが表示されている間は、毎分 `tick (subscribers: n)` が 1 行出ます。

## 確認項目

| # | 手順 | 合格の条件 | ログで見るもの |
| --- | --- | --- | --- |
| V7 | 配置の直後に、エクスプローラーのアドレス欄に `%LOCALAPPDATA%\StationCountdown` と入力して開く | `logs` フォルダとログファイルがある（パッケージ専用のフォルダに振り替えられていない） | `extension process started` |
| V1 | バンドを表示したまま 8 時間以上放置する（夜間など。スリープしない設定で） | 残り分数が、今の時刻から見て正しい（時刻表と照らし合わせる） | `tick` が毎分途切れずに出ている |
| V2 | Win+L で画面をロックし、数分待ってから解除する | バンドが表示されていて、解除の直後に正しい残り分数になり、その後も毎分更新される | `session unlocked` のあとに `tick` が続く |
| V3 | スリープし、数分以上たってから復帰する | V2 と同じ | `resumed from sleep` のあとに `tick` が続く |
| V6 | Dock の編集モードで、バンドのピン留めを外す（ほかのバンドもすべて外す） | 外した後は更新が止まる | `band unloaded` と `tick stopped` が出て、以降 `tick` が出ない |

- V6 のあとにもう一度ピン留めすると、`band loaded` と `tick subscribed` が出て、更新が再開するはずです
- 拡張機能のプロセスが終わった場合は、`extension process exiting` が出ます。止まった時刻の前後のログを見せてください

## うまくいかなかった場合

- **V1〜V3 で更新が止まる**: 止まった時刻の前後のログを共有してください。計画どおり、`Program.cs` で `CoAddRefServerProcess` を呼んでプロセスを維持する対策を入れて、もう一度確かめます。それでも駄目なら、README に既知の制限（PowerToys#50367）として書きます
- **V7 でフォルダがない**: `%LOCALAPPDATA%\Packages\Laplusdestiny.StationCountdown_*\LocalCache\Local\StationCountdown` にできていないかを見てください。そこにある場合は、書き込みの仮想化の無効化が効いていません
- **バンドが一覧に出ない、配置に失敗する**: Visual Studio の出力ウィンドウのエラーと、Command Palette 側のログ（`%LOCALAPPDATA%\Microsoft\PowerToys` の下の CmdPal のログ）を共有してください
