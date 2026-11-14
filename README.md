# cmdpal-station-countdown

PowerToys Command Palette の **Dock** に、登録した駅・方面ごとに「**家を出るまであと何分か**」を常に表示する拡張機能です。
徒歩の時間を差し引き、間に合わない電車は Dock に出しません。

```
急行 宝塚 あと3分
12:34発 ／ 次 12:40 普通
```

- 設計書: [`docs/superpowers/specs/2026-11-09-station-countdown-design.md`](docs/superpowers/specs/2026-11-09-station-countdown-design.md)
- 将来の機能: [Issues（`future` ラベル）](https://github.com/Laplusdestiny/cmdpal-station-countdown/issues?q=label%3Afuture)

## 必要なもの

- Windows 10 2004（19041）以降、PowerToys 0.98 以降（Command Palette の Dock を有効にする）
- 開発中のため、配布用のパッケージはまだありません。下の「ビルドと配置」で、自分の PC に配置して使います

## 使い方

### 駅を追加する

Command Palette で「駅カウントダウン」を開き、末尾のコマンドから追加します。

| コマンド | 時刻表の取得元 |
| --- | --- |
| ＋ 阪急の駅を追加 | 阪急公式サイトの駅ページの URL（例: `https://www.hankyu.co.jp/station/okamachi.html`）を入れると、路線・方面を選べます。保存すると、この PC 上で時刻表を取得します（以降は 7 日ごとに自動で再取得） |
| ＋ ファイルで駅を追加 | 自分で用意した時刻表の YAML ファイルを使います。保存すると、書き方の例を置いたフォルダが開きます |

追加した駅は、Dock の編集モードでバンドとしてピン留めできます。バンドをクリックすると、その駅の発車一覧が開きます。
右クリックで「時刻表を再取得」（阪急の駅）・「駅を編集」・「設定フォルダを開く」ができます。

### 設定ファイル

設定と時刻表は `%LOCALAPPDATA%\StationCountdown\` にあり、エディタで直接編集できます（保存すると数秒で反映されます）。
`schemas\` の JSON Schema を使うと、VS Code（YAML 拡張機能）で補完とチェックが効きます。

`stations.yaml` の例（駅名・URL は架空のものです）:

```yaml
# yaml-language-server: $schema=./schemas/stations.schema.json
schemaVersion: 1
settings:
  refreshIntervalDays: 7     # 時刻表を自動で再取得する間隔（日、1〜30）
  departuresListLimit: 30    # 発車一覧の最大件数（5〜100）
stations:
  - id: kakuu-kita           # 半角英小文字・数字と -。64 文字以内
    name: 架空北
    directionLabel: 北方面
    walkMinutes: 8           # 家から駅までの徒歩の分数（0〜120）
    source:
      type: hankyu
      stationPageUrl: https://www.hankyu.co.jp/station/kakuu.html
      line: 架空線             # 駅ページの表記と同じにする
      direction: 北方面
  - id: kakuu-minami
    name: 架空南
    directionLabel: 南方面
    walkMinutes: 10
    source:
      type: file             # timetables/kakuu-minami.yaml を読む
dayTypeOverrides:            # 特定の日のダイヤを上書きする（年末年始など）
  - { date: 2026-12-31, dayType: saturdayHoliday }
```

時刻表 `timetables\{id}.yaml` の例（架空のデータです）:

```yaml
# yaml-language-server: $schema=../schemas/timetable.schema.json
schemaVersion: 1
stationId: kakuu-minami
services:
  weekday:                   # weekday / saturday / holiday / saturdayHoliday
    - 05:00 普通 架空台        # 1 本を 1 行で「HH:MM 種別 行先」（半角スペース区切り）
    - 05:20 急行 架空台
    - 24:10 普通 架空町        # 0 時台以降は 24 時以降で書く（29:59 まで）
  saturdayHoliday:
    - 05:30 普通 架空台
```

- 営業日の区切りは 3:00 です。ダイヤの種別は、上書き → 日曜・祝日 → 土曜 → 平日の順に決まります
- `stations.yaml` に誤りがあっても、最後に正しく読めた設定で動き続け、「駅カウントダウン」の先頭に行番号付きでエラーを出します
- フォームで保存すると、`stations.yaml` 内のコメントは消えます（直前の内容は `stations.yaml.bak` に残ります）

## ビルドと配置

| 対象 | 方法 |
| --- | --- |
| Core / Importers のテスト（Windows・Linux） | `dotnet test --project tests/StationCountdown.Core.Tests`（Importers も同様）。.NET 10 SDK が必要 |
| 拡張機能（Windows） | VS Code のタスク「Extension: ビルドして Command Palette に登録」、または `tools\windows\Build-Extension.ps1 -Register`。Visual Studio と Windows SDK は不要で、.NET 10 SDK と Windows の開発者モードが必要 |
| 拡張機能（WSL から） | `bash tools/windows/build-from-wsl.sh -Register`（Windows のローカルフォルダーに写してから上の ps1 を実行する） |
| 拡張機能（Visual Studio） | `StationCountdown.sln` を開き、`x64` で `StationCountdown.Extension` を「配置」 |

配置したあと、Command Palette で `Reload` を実行すると読み込まれます。詳しい手順と実機での確認項目は [`docs/verification/2026-11-10-dock-prototype-gate.md`](docs/verification/2026-11-10-dock-prototype-gate.md) にあります。

`-p:IncludeImporters=false` を付けてビルドすると、阪急のインポーターを含めない版になります（一般公開に向けた切り替え。阪急の駅は「時刻表未取得」と表示します）。

## 既知の制限

- 長時間の放置・画面ロック・スリープからの復帰のあとも更新が続くか（設計書 §10 の V1〜V3）と、ピン留めを外すと更新が止まるか（V6）は、まだ実機で確認できていません。止まる場合は、PowerToys の既知の問題（[PowerToys#50367](https://github.com/microsoft/PowerToys/issues/50367)）の影響が考えられます
- リアルタイムの遅延情報・運行情報には対応していません（#5）
- 阪急の駅ページには、過去のダイヤ改正版の表示が残っていることがあります。拡張機能は現行ダイヤだけを使います

## データの取り扱い

- **時刻表**: 時刻表の著作権は各事業者に帰属します。取得したデータは私的利用の範囲でお使いください。このリポジトリには鉄道事業者の時刻表データを含めません（テストのデータはすべて架空のものです）。阪急の時刻表は、利用者の PC 上で取得して `%LOCALAPPDATA%\StationCountdown\timetables\` に保存するだけで、ほかへは送りません
- **祝日**: 出典：内閣府『国民の祝日について』（公共データ利用規約 第1.0版）。`src/StationCountdown.Extension/Assets/syukujitsu.csv` に同梱し、足りなくなったら内閣府のサイトから取得します

## ライセンス

[MIT](LICENSE)

一部のコードは [Microsoft PowerToys](https://github.com/microsoft/PowerToys)（MIT License）をもとにしています。

- `src/StationCountdown.Extension/`（Command Palette 拡張機能のテンプレート `src/modules/cmdpal/ExtensionTemplate`）
- `src/StationCountdown.Extension/Dock/OnLoadBandPage.cs`・`OnLoadBandItem.cs`（TimeDate 拡張の `OnLoadDynamicListPage` / `OnLoadDockBandItem`）
- `src/StationCountdown.Extension/Dock/SafeEvents.cs`（Toolkit の `EventHelpers`）

```
Copyright (c) Microsoft Corporation
The MIT License (MIT)
```
