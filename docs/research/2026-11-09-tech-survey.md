# 技術調査: Command Palette Dock 用「駅時刻表カウントダウン」拡張

- 調査日: 2026-11-09
- 対象: PowerToys Command Palette 拡張（Dock バンド）
- サンプル駅: 阪急宝塚線 大阪梅田駅 / 東急田園都市線 宮前平駅
- 位置付け: スパイク（実現可能性の調査）。調査中に作った解析スクリプトは使い捨てで、このリポジトリには含めない。

---

## 1. 結論

| 問い | 結論 |
| --- | --- |
| Dock に自前の表示を出せるか | **できる。** `ICommandProvider3.GetDockBands()` でバンドを返す。SDK 0.9 以降が必要で、PowerToys 0.98（2026-03）から Dock を搭載。 |
| 表示を秒単位・分単位で更新できるか | **できる。** `ListItem` の `Title` / `Subtitle` を書き換えると Dock の表示も変わる。組み込みの時計（`NowDockBand`）が同じ方法を使っている。 |
| 阪急 大阪梅田のデータを取れるか | **取れるが、扱いに制約がある。** 阪急は ODPT 未対応。公式サイトの時刻表 HTML は構造が単純で解析しやすい。ただしサイト利用規約で無断の複製・再配布を禁じているため、**データはリポジトリに入れず、利用者の PC 上で取得してそこに保存する**方針にする。 |
| 東急 宮前平のデータを取れるか | **ODPT で取れるが、期限付き。** 東急の駅時刻表は「公共交通オープンデータチャレンジ2026 限定ライセンス」でのみ公開されていて、使えるのはコンテスト期間中（〜2027-03-12）。長期的には別の取得経路が要る。 |
| ODPT の駅全般に対応できるか | **できる。** 基本ライセンスで駅時刻表を公開している事業者（東京メトロ、横浜市交通局など）には、恒久的に対応できる。 |

**推奨構成**: 時刻表の取得（データソース）と、次発の計算・表示を分離する。計算は OS に依存しない Core ライブラリにまとめ、Linux や CI でもテストできるようにする。Dock 拡張そのものは薄い Windows 専用のレイヤーにする（[§7](#7-推奨アーキテクチャ)）。

---

## 2. 開発の前提（2026-11 時点）

| 項目 | 内容 | 出典 |
| --- | --- | --- |
| Dock 搭載バージョン | PowerToys **0.98**（2026-03-17）。0.99 で改良が続いている | devblogs / 各種ニュース |
| Dock API の要件 | `Microsoft.CommandPalette.Extensions` **0.9.260303001 以上** | Learn「Adding Dock support」 |
| SDK の最新版（NuGet） | `0.12.260812002`（公式テンプレートは `0.11.260520004` を参照） | nuget.org / `ExtensionTemplate/Directory.Packages.props` |
| ターゲット | `net10.0-windows10.0.26100.0`、最小 OS は `10.0.19041.0`、`win-x64` / `win-arm64` | テンプレートの csproj |
| パッケージ形式 | MSIX（`EnableMsixTooling`）。拡張は **アウトプロセスの COM サーバー**として動く | テンプレートの `Package.appxmanifest` |
| COM ホスト | `Shmuelie.WinRTServer`。起動引数 `-RegisterProcessAsComServer` で起動 | テンプレートの `Program.cs` |
| 登録方法 | `uap3:AppExtension Name="com.microsoft.commandpalette"` と、`com:ExeServer` の CLSID | 同上 |
| ビルド設定 | AOT / トリミング対応（`IsAotCompatible`、Release では `PublishTrimmed`） | テンプレートの csproj |
| プロジェクトの作り方 | Command Palette 上で `Create extension` コマンドを実行して生成するのが公式手順 | Learn「拡張機能の開発」 |

> **AOT とトリミングの注意**: Release ビルドではトリミングが有効になる。JSON 処理は `System.Text.Json` のソース生成（`JsonSerializerContext`）を使い、リフレクションに頼らない作りにする。

---

## 3. Dock API の詳細

### 3.1 バンドの提供

```csharp
public partial class StationCountdownCommandsProvider : CommandProvider
{
    public override ICommandItem[]? GetDockBands() => [_band];        // ICommandProvider3
    public override ICommandItem? GetCommandItem(string id) => ...;   // ICommandProvider4（入れ子のコマンドを Dock にピン留めする場合）
}
```

- `GetDockBands()` が返す `ICommandItem` 1 つが、Dock 上のバンド 1 つになる。
- `Command.Id` が空のアイテムは**無視される**。駅・方面ごとに固定の ID を付ける（例: `com.laplusdestiny.stationcountdown.band.hankyu-HK01-ta`）。
- `Command` の型によって、表示のされ方が変わる。

| Command の型 | Dock での表示 |
| --- | --- |
| `IInvokableCommand` | ボタン 1 つ（押すとコマンドを実行） |
| `IListPage` | ページ内の各アイテムが、同じバンドの中にボタンとして並ぶ |
| `IContentPage` | 押すとフライアウトが開くボタン 1 つ |

- 複数のボタンを持つバンドは `WrappedDockItem(IListItem[] items, string id, string displayTitle)` で作れる。
- ユーザーは編集モードで、アイテムごとに **Show Titles / Show Subtitles** を切り替えられる。Dock のサイズを **Compact** にすると Subtitle は表示されない。主な情報は **Title に入れる**必要がある。

### 3.2 表示の更新：組み込み時計（`NowDockBand`）の実装

PowerToys のソース（`src/modules/cmdpal/ext/Microsoft.CmdPal.Ext.TimeDate/`、MIT ライセンス）を読んで分かった実装パターン：

1. `NowDockBand : ListItem` が、タイマーのたびに `Title` と `Subtitle` を代入する。値が前回と同じなら何もしない。
2. `ClockUpdateService` が、拡張全体で 1 つの `System.Timers.Timer`（`AutoReset = false`）を持つ。次の分の切り替わり（秒表示が必要なバンドがあれば次の秒）に合わせてタイマーを予約し直す。
3. `OnLoadDockBandItem` / `OnLoadDynamicListPage`: **バンドが実際に表示されているときだけ更新する**ための仕組み。
   - Command Palette は、バンドを画面に出すときにバンドのページの `ItemsChanged` イベントへハンドラーを登録し、画面から外すときに解除する。拡張がバンドの表示状態を知る手段はこれしかない。
   - そこで `add` / `remove` を数えて `Loaded()` / `Unloaded()` を呼び、タイマーの開始・停止につなげている。
   - この 2 つのクラスは TimeDate 拡張の `internal` クラスで、**Toolkit には含まれていない**。同じ方式を使うなら、自前で実装する（MIT ライセンスに従って参考にする）。

本拡張では、この 3 つをほぼそのまま応用できる。

| 要素 | 本拡張での使い方 |
| --- | --- |
| `NowDockBand` | `DepartureBand : ListItem`。Title は「急行 宝塚 あと3分」、Subtitle は「12:34発 / 次 12:40」 |
| `ClockUpdateService` | `TickService`。分の切り替わりで更新する。秒単位のカウントダウンは設定で選べるようにする |
| `OnLoadDockBandItem` | 同じ仕組みを自前で用意する。ピン留めされていない駅のバンドではタイマーを動かさない |

### 3.3 まだリリースされていない API

PowerToys の main ブランチ（`ba946c9`、2026-11-06）には、`Toolkit/Dock/DockLabelWidth.cs` と `DockLabelPresentationExtensions.cs` がある。

- `SetDockLabelTabularDigits()`: 数字を等幅で表示する
- `DockLabelWidth.Characters(n)` / `Sample("00:00")`: ラベルの幅を固定する

カウントダウン表示で数字の幅が変わり、ラベルが伸び縮みするのを防ぐのに向いている。ただし、**NuGet で公開されている 0.11 / 0.12 の Toolkit.dll にはまだ含まれていない**（dll 内の文字列検索で確認）。今後のリリースに入れば採用する。

### 3.4 既知の問題（リスク）

[microsoft/PowerToys#50367](https://github.com/microsoft/PowerToys/issues/50367)（Open）

- ホストがしばらく使われていない拡張を解放すると（`IExtension.Dispose()` が呼ばれ、プロセスが終了する）、Dock のバンドが反応しなくなり、拡張も再起動されない。
- RDP 接続や、コンソールのセッションへの切り替えでも、バンドが消えることがある。報告者は、拡張側ではどうやっても回避できないと結論づけている。
- **影響**: 常に表示しておくカウントダウンにとっては致命的になりうる。最初の試作で、長時間放置したときやロック・解除の前後に表示が止まらないかを検証する。回避策の候補として、報告者が試した方法がある（`CoAddRefServerProcess` でプロセスを生かし続ける）。

---

## 4. データソースの調査

### 4.1 比較

| データソース | 対象 | 取得方法 | 制約 | 評価 |
| --- | --- | --- | --- | --- |
| **ODPT 基本ライセンス** | 東京メトロ、横浜市交通局、りんかい線、つくばエクスプレス、多摩モノレール、ゆりかもめ など | `api.odpt.org` の `odpt:StationTimetable`（JSON、要トークン） | クレジットの表記が必要。トークンは利用者ごとに発行 | ◎ 恒久的に使える |
| **ODPT チャレンジ限定** | **東急電鉄**、西武、東武、京王、京都市営地下鉄 ほか | `api-challenge.odpt.org`（要エントリー＋チャレンジ用トークン） | 利用期間はコンテスト中のみ（〜**2027-03-12**）。第三者への提供は禁止 | △ 期限付き |
| **阪急 公式時刻表 HTML** | 阪急全駅 | `https://www.hankyu.co.jp/station/html/{駅コード}_{路線}_{方面}_{w\|h}.html` | 利用規約で無断の複製・再配布・掲示を禁止 | ○ 私的な利用で、利用者の PC 上でのみ取得 |
| 東急 公式時刻表 | 東急全駅 | 駅のページから NAVITIME（`transfer.navitime.biz/tokyu/...`）にリンク。PDF もある | NAVITIME の規約に左右される。スクレイピングは避けたい | × MVP では使わない |
| 駅すぱあと API などの商用 API | 全国 | 有償の API キー | 費用と契約 | ― 将来の候補 |
| 利用者が用意するファイル | 任意 | JSON / CSV を取り込む | 手作業で更新が必要 | ○ どの駅でも使える最後の手段 |

> ODPT の利用規約とチャレンジ限定ライセンスの全文は、開発者サイト（ログイン後）で確認が必要。上の表の制約は、公開情報（CKAN のデータセットページ、チャレンジ2026 のサイト、過去回の募集要項）から読み取ったもの。

### 4.2 阪急 大阪梅田（宝塚線・宝塚方面）

- 駅のページ: `https://www.hankyu.co.jp/station/umeda.html`。駅コードは `HK-01`、宝塚線は `ta`、方面は `2`、平日は `w`、土曜・休日は `h`。
  - 平日: `/station/html/HK-01_ta_2_w.html`
  - 土曜・休日: `/station/html/HK-01_ta_2_h.html`
  - ダイヤ改正のときは、`/station/html/20250222/...` のように日付付きのディレクトリで新しいダイヤが先に公開される。
- HTML の構造（2026-11-09 時点）:
  - `li.timetable_hour` が「時」、`p.timetable_min` が「分」、`p.timetable_train_type` が種別（`［急行］` など）、`p.timetable_station_name` が行先。
  - 時の欄は `5 … 23, 0` の順に並ぶ。**0 時台は翌日の未明**として扱う必要がある。
  - 時の欄のクラス名は、平日ダイヤが `hour_w`、土休日ダイヤが `hour_h`。解析では、`timetable_hour` だけを見るようにする。
- 使い捨てスクリプトで解析した結果:
  - 平日: 240 本。種別は 普通 123、急行 96、通勤急行 14、直通特急 7。行先は 宝塚、雲雀丘花屋敷、川西能勢口、日生中央、池田、豊中。
  - 土休日: 202 本。種別は 普通 105、急行 97。
- `robots.txt` で禁止されているのは `/station/include/` などのインクルード用ディレクトリだけで、`/station/html/` は禁止されていない。
- **サイト利用規約（著作権等）**の抜粋：「法的に認められる範囲を超えて、著作権者の事前の許諾なく、これらのコンテンツを利用（複製、改変、販売、他のサイトへのアップロード、WEBやその他のメディア上での掲示、ライセンス、出版、頒布、不特定多数者への送信等を含む）することを禁止いたします。」
  - そのため、**時刻表データをリポジトリにコミットしない**（テスト用のデータは架空のものにする）。拡張や取り込みツールは、利用者の PC 上で取得して、そのローカルに保存するだけにする。取得はダイヤ改正の確認を兼ねて週 1 回程度に抑え、サイトに負荷をかけない。
  - 法的な判断はしていない。公開・配布の前に改めて確認する。

### 4.3 東急 宮前平（田園都市線）

- 東急の公式サイトの駅ページ（`/area/miyamaedaira/station/`）から、NAVITIME の時刻表へのリンクがある。
  - 中央林間方面: `TrainDiagram?stCd=00001655&rrCd=00000789&updown=1`
  - 渋谷方面: `TrainDiagram?stCd=00001655&rrCd=00000789&updown=0`
- ODPT の CKAN にある `tokyu__r_station_timetable` データセットは「【チャレンジ2026限定】」。エンドポイントは次のとおり。
  `https://api-challenge.odpt.org/api/v4/odpt:StationTimetable?odpt:operator=odpt.Operator:Tokyu&acl:consumerKey=...`
- チャレンジ2026 の期間: コンテストの実施期間は 2026-07-01〜2027-03-12、応募期間は 2026-10-01〜2027-01-11。
- **未確認の点**: 宮前平の `odpt:Station` ID や、`odpt:railDirection` の値。トークンを取得して、実データで確認する必要がある（`odpt.Railway:Tokyu.DenEnToshi` 配下にあると見込んでいる）。
- 期限後の選択肢: (a) ODPT の基本ライセンスで東急が公開されるのを待つ (b) 利用者が用意するファイルに切り替える (c) 商用 API を使う。

### 4.4 ODPT の `odpt:StationTimetable` の主なフィールド（API 仕様 v4）

| フィールド | 内容 |
| --- | --- |
| `odpt:station` / `odpt:railway` / `odpt:operator` | 駅・路線・事業者の ID |
| `odpt:railDirection` | 方面 |
| `odpt:calendar` | `odpt.Calendar:Weekday` / `SaturdayHoliday` / `Saturday` / `Holiday` など |
| `odpt:stationTimetableObject[]` | `odpt:departureTime`（`HH:MM`）、`odpt:trainType`、`odpt:destinationStation[]`、`odpt:isLast` など |

種別や駅の表示名は、別の API（`odpt:TrainType`、`odpt:Station`）の `dc:title` / `odpt:stationTitle` から引く必要がある。最初の取得時にまとめてキャッシュしておく。

### 4.5 祝日の判定

阪急の「土曜・休日」ダイヤや ODPT の `SaturdayHoliday` を選ぶには、日本の祝日が分かる必要がある。

- 内閣府の `syukujitsu.csv`（`https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv`、Shift_JIS）: 公式データで、HTTP 200 を確認済み。
- `holidays-jp.github.io/api/v1/date.json`: 非公式のミラー。HTTP 200 を確認済み。
- 年末年始（12/30〜1/3 ごろ）や臨時ダイヤは、事業者ごとに扱いが違う。MVP では「祝日のみ休日扱い」とし、利用者が日付ごとに上書きできる設定で補う。

---

## 5. 時刻計算の注意点

- **タイムゾーン**は常に JST（`Tokyo Standard Time` / `Asia/Tokyo`）で計算する。PC のタイムゾーン設定には依存させない。
- **営業日の区切り**: 0 時台・1 時台の電車は、前日のダイヤに属する。区切りの時刻は 3:00 などに固定し、その時刻より前は前日の営業日として扱う。
- **ダイヤの種別**は、営業日（暦の日付ではない）の曜日と祝日から決める。
- **最終電車の後**は「本日の運行は終了 / 始発 5:00」と表示する。
- **ダイヤ改正**: 取得元の更新を週 1 回確認する。阪急は日付付きのディレクトリで先に公開されるので、それも検出する。ODPT は `dct:issued` / `dct:valid` を見る。
- リアルタイムの遅延は対象外（MVP の範囲外）。ODPT の `odpt:TrainInformation` で運行情報を出すことは、将来の拡張として考えられる。

---

## 6. Dock での表示案

| 状態 | Title | Subtitle |
| --- | --- | --- |
| 通常 | `急行 宝塚 あと3分` | `12:34発 ／ 次 12:40 普通` |
| 1 分未満 | `急行 宝塚 まもなく` | `12:34発` |
| 最終電車の後 | `本日終了` | `始発 5:00` |
| データ取得に失敗 | `時刻表未取得` | `クリックで再取得` |

- バンドを押すと、その駅の次発以降を一覧で表示する（`ListPage`）。
- 右クリック（`MoreCommands`）で「時刻表を再取得」「表示形式を変更」などを操作できるようにする。
- 1 つのバンドに 1 つの駅・方面を割り当てる。複数の駅は、別々のバンドとしてピン留めする。

---

## 7. 推奨アーキテクチャ

```
StationCountdown.Core            (net10.0, OS 非依存, xUnit で Linux/CI テスト可)
 ├─ Model:  Departure, StationTimetable, DayType, StationKey
 ├─ ServiceDayResolver           営業日・ダイヤ種別（祝日カレンダー注入）
 ├─ NextDepartureCalculator      now → 次発 / 次々発
 └─ CountdownFormatter           表示文字列

StationCountdown.Sources         (net10.0)
 ├─ ITimetableSource             FetchAsync(StationKey) → StationTimetable
 ├─ OdptStationTimetableSource   api.odpt.org / api-challenge.odpt.org 切替
 ├─ HankyuHtmlTimetableSource    公式 HTML 解析（取得は利用者 PC 上のみ）
 ├─ LocalFileTimetableSource     利用者提供 JSON/CSV
 └─ TimetableCache               %LOCALAPPDATA% に正規化 JSON を保存

StationCountdown.Extension       (net10.0-windows, MSIX, Windows 専用)
 ├─ CommandsProvider             GetDockBands / TopLevelCommands / 設定ページ
 ├─ DepartureBand : ListItem     Title/Subtitle 更新
 ├─ OnLoadBand (自前実装)        表示中のみ Tick
 └─ TickService                  単一タイマー
```

- 設定は Toolkit の `JsonSettingsManager` で行う（0.11 / 0.12 に含まれていることを確認済み）。駅の追加・削除、ODPT のトークン、更新の間隔などを設定できるようにする。ODPT のトークンは、利用者のローカルの設定ファイルにだけ保存する。
- テストは Core と Sources に集中させる。HTML や JSON の解析テストには、**架空のデータで作ったファイル**を使う。

---

## 8. 開発環境・CI

- 拡張のビルド・デバッグ・MSIX 作成には **Windows と Visual Studio 2022 以降**（または .NET 10 SDK と Windows App SDK のビルドツール）が必要。
- Core と Sources は OS に依存しないので、Linux や クラウド環境でもビルド・テストできる。
  - この調査セッションでは .NET SDK を入れられなかった（自動承認モードでインストールが拒否された）ため、**Linux でのビルドは確認していない**。
- CI の案: GitHub Actions で次の 2 つのジョブを作る。
  - `ubuntu-latest`: Core と Sources の `dotnet test`
  - `windows-latest`: 拡張の `dotnet build -p:Platform=x64`。MSIX の作成は手動

---

## 9. リスク・未確認事項

| # | 内容 | 影響 | 対応 |
| --- | --- | --- | --- |
| R1 | PowerToys#50367: 解放やセッションの切り替えでバンドが止まる・消える | 大 | 最初の試作で、長時間の放置・ロック・RDP を検証する |
| R2 | 東急の ODPT データは 2027-03-12 まで | 中 | 期限後は、利用者が用意するファイルか他の API に切り替えられる設計にする |
| R3 | 阪急の利用規約 | 中 | データを再配布しない。取得は利用者の PC 上で、頻度を抑える |
| R4 | 阪急の HTML 構造の変更 | 中 | 解析に失敗したら前回のキャッシュを使い続け、「要更新」と表示する |
| R5 | 臨時ダイヤ・年末年始 | 小 | 日付ごとに上書きできる設定を用意する |
| R6 | 等幅数字・ラベル幅の API がまだリリースされていない | 小 | リリースまでは数字の幅が変わる表示を受け入れる |
| R7 | ODPT の規約全文、宮前平の ID | ― | トークンを取得して確認する |

---

## 10. 次のステップ（提案）

1. **ODPT の開発者登録とチャレンジ2026 へのエントリー**を行い、トークンを取得する（利用者本人の作業）。宮前平の実データで ID や方面を確認する。
2. **Dock の最小試作**を Windows 上で作る。テンプレートから生成し、固定の文字列を毎分更新するだけのバンドで、R1（長時間放置・ロック・RDP）を検証する。
3. 上の結果をもとに、本実装の設計書（spec）と実装計画を書く。

---

## 参考

- [コマンド パレット拡張機能の開発（Microsoft Learn）](https://learn.microsoft.com/ja-jp/windows/powertoys/command-palette/extension-development)
- [Adding Dock support to your extension](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/adding-dock-support)
- [PowerToys Command Palette Dock](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/dock)
- [PowerToys 0.98 リリース記事](https://devblogs.microsoft.com/commandline/powertoys-0-98-is-here-new-keyboard-manager-ux-the-command-palette-dock-and-better-cursorwrap/)
- [microsoft/PowerToys#45432 CmdPal: Add Dock API](https://github.com/microsoft/PowerToys/pull/45432)
- [microsoft/PowerToys#50367 dock band buttons stop working ...](https://github.com/microsoft/PowerToys/issues/50367)
- PowerToys ソース `src/modules/cmdpal/ext/Microsoft.CmdPal.Ext.TimeDate/`（`NowDockBand.cs`, `ClockUpdateService.cs`, `Pages/OnLoadDockBandItem.cs`）、`src/modules/cmdpal/ExtensionTemplate/`
- [NuGet: Microsoft.CommandPalette.Extensions](https://www.nuget.org/packages/Microsoft.CommandPalette.Extensions)
- [公共交通オープンデータセンター データカタログ](https://ckan.odpt.org/)、[東急電鉄 駅時刻表データセット](https://ckan.odpt.org/dataset/tokyu__r_station_timetable)
- [公共交通オープンデータチャレンジ2026](https://challenge2026.odpt.org/)
- [阪急電鉄 大阪梅田駅](https://www.hankyu.co.jp/station/umeda.html)、[サイト利用規約](https://www.hankyu.co.jp/sitepolicy/index.html)
- [東急電鉄 宮前平駅](https://www.tokyu.co.jp/area/miyamaedaira/station/)
- [内閣府 国民の祝日 CSV](https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv)
