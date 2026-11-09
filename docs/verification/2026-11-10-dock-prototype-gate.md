# Dock 試作バンドの実機確認（Task 12 のゲート）

実装計画の Task 12 手順 5 で、Windows の実機で確かめる項目（spec §10 の V1〜V3・V6）の手順です。
あわせて、Task 12 で追加したマニフェストの設定（V7）も確かめます。結果は draft PR の「V1〜V6 の検証結果」の表に書きます。
Task 13 以降は、この確認が終わってから始めます。

## 準備

- PowerToys 0.98 以降。Command Palette の設定で Dock を有効にしておく
- Windows の「開発者モード」をオンにする（設定 → システム → 開発者向け）
- Visual Studio 2022 17.14 以降、または 2026。ワークロードは「.NET デスクトップ開発」と「Windows アプリケーション開発」（Windows App SDK の C# テンプレート）
- .NET 10 SDK

## 配置

1. `feat/mvp` ブランチを取得し、`StationCountdown.sln` を Visual Studio で開く
2. 構成を `Debug` / `x64` にする（ツールバーの「Any CPU」を「x64」に変える）
3. ソリューション エクスプローラーで `StationCountdown.Extension` を右クリック →「配置」
4. Command Palette を開き、`Reload` コマンド（「拡張機能を再読み込み」）を実行する
5. Command Palette で「駅カウントダウン」が出ることを確かめる（中身は「ログフォルダを開く」「設定フォルダを開く」だけ）
6. Dock の編集モードで、「駅カウントダウン（試作）」のバンドをピン留めする
   - Title が `試作 H:mm`（JST の今の時刻）、Subtitle が `更新 n 回目` になる
   - 毎分 0 秒ごろに時刻が進み、回数が増える

ログは `%LOCALAPPDATA%\StationCountdown\logs\yyyyMMdd.log` に出ます（「ログフォルダを開く」でも開けます）。試作の間は、更新のたびに `prototype band refreshed: …` が 1 行出ます。

## 確認項目

| # | 手順 | 合格の条件 | ログで見るもの |
| --- | --- | --- | --- |
| V7 | 配置の直後に、エクスプローラーのアドレス欄に `%LOCALAPPDATA%\StationCountdown` と入力して開く | `logs` フォルダとログファイルがある（パッケージ専用のフォルダに振り替えられていない） | `extension process started` |
| V1 | バンドを表示したまま 8 時間以上放置する（夜間など。スリープしない設定で） | Title の時刻が今の時刻と合っている。回数が放置した分だけ増えている | `prototype band refreshed` が毎分途切れずに出ている |
| V2 | Win+L で画面をロックし、数分待ってから解除する | バンドが表示されていて、解除の直後に今の時刻になり、その後も毎分更新される | `session unlocked` のあとに `refreshed` が続く |
| V3 | スリープし、数分以上たってから復帰する | V2 と同じ | `resumed from sleep` のあとに `refreshed` が続く |
| V6 | Dock の編集モードで、バンドのピン留めを外す | 外した後は更新が止まる | `band unloaded` と `tick stopped` が出て、以降 `refreshed` が出ない |

- V6 のあとにもう一度ピン留めすると、`band loaded` と `tick subscribed` が出て、更新が再開するはずです
- 拡張機能のプロセスが終わった場合は、`extension process exiting` が出ます。止まった時刻の前後のログを見せてください

## うまくいかなかった場合

- **V1〜V3 で更新が止まる**: 止まった時刻の前後のログを共有してください。計画どおり、`Program.cs` で `CoAddRefServerProcess` を呼んでプロセスを維持する対策を入れて、もう一度確かめます。それでも駄目なら、README に既知の制限（PowerToys#50367）として書きます
- **V7 でフォルダがない**: `%LOCALAPPDATA%\Packages\Laplusdestiny.StationCountdown_*\LocalCache\Local\StationCountdown` にできていないかを見てください。そこにある場合は、書き込みの仮想化の無効化が効いていません
- **バンドが一覧に出ない、配置に失敗する**: Visual Studio の出力ウィンドウのエラーと、Command Palette 側のログ（`%LOCALAPPDATA%\Microsoft\PowerToys` の下の CmdPal のログ）を共有してください
