# 設計書: Command Palette Dock 駅時刻表カウントダウン（MVP）

- 作成日: 2026-11-09
- 状態: レビュー待ち
- 前提調査: [docs/research/2026-11-09-tech-survey.md](../../research/2026-11-09-tech-survey.md)

## 1. 目的と範囲

### 1.1 目的

PowerToys Command Palette の Dock に、登録した駅・方面ごとに「**家を出るまであと何分か**」を常に表示する。徒歩時間を考慮して、間に合わない電車は Dock に出さない。

### 1.2 利用形態

- まずは開発者本人と身近な人が使う。ソースコードは GitHub で public・MIT ライセンス。
- 将来は一般公開（#4）も検討する。そのため、公式サイトから時刻表を取得する処理（インポーター）は、ほかの部分から切り離せる構成にする。

### 1.3 サンプル駅

| ID | 駅 | 方面 | 取得元 |
| --- | --- | --- | --- |
| `hankyu-umeda-takarazuka` | 阪急 大阪梅田 | 宝塚線 宝塚方面 | 阪急公式サイトの時刻表 HTML（`hankyu` インポーター） |
| `tokyu-miyamaedaira-shibuya` | 東急 宮前平 | 田園都市線 渋谷方面 | 利用者が用意する YAML ファイル（`file`） |
| `tokyu-miyamaedaira-chuorinkan` | 東急 宮前平 | 田園都市線 中央林間方面 | 同上 |

### 1.4 MVP の範囲外（Issue に登録済み）

| Issue | 内容 |
| --- | --- |
| #2 | 東急 PDF 時刻表インポーター |
| #3 | ODPT API 対応（トークンが必要なため見送り） |
| #4 | 一般公開（ギャラリー / WinGet / Store） |
| #5 | 運行情報（遅延・見合わせ）の表示 |
| #6 | Dock ラベルの等幅数字・幅固定 API の採用 |
| #7 | 時間帯で駅・方面を切り替えるバンド |
| #8 | Dock から直接フライアウトで一覧を表示 |

あわせて、リアルタイムの遅延情報、乗り換え検索、秒単位のカウントダウンも MVP の範囲外とする。

## 2. 前提環境

- PowerToys 0.98 以降（Command Palette の Dock を有効にしておく）
- `Microsoft.CommandPalette.Extensions` 0.12.260812002。Dock の API は 0.9 以降が必要。
- 拡張機能のターゲットは `net10.0-windows10.0.26100.0`。最小 OS は `10.0.19041.0`、プラットフォームは `x64` / `ARM64`、配布形式は MSIX。
- 公式テンプレートの AOT・トリミング設定はそのまま使う。リフレクションに頼るシリアライザーは使わない。

## 3. 全体構成

案 1（拡張機能 1 つに全部入れ、中のプロジェクトを分ける）を採用する。

```
StationCountdown.sln
├─ src/StationCountdown.Core          net10.0（OS 非依存）
│   ├─ Model/       StationConfig, StationsDocument, Timetable, Departure, DayType
│   ├─ Config/      StationsFile（stations.yaml の読み書き・検証）
│   ├─ Timetables/  TimetableFile（timetables/{id}.yaml の読み書き・検証）
│   ├─ Calendar/    HolidayCalendar（内閣府 CSV の解析）, ServiceDayResolver
│   ├─ Countdown/   NextDepartureCalculator, CountdownFormatter
│   └─ Yaml/        YamlDotNet 用の静的コンテキスト
├─ src/StationCountdown.Importers     net10.0（OS 非依存）
│   ├─ ITimetableImporter
│   └─ Hankyu/      HankyuStationPageParser, HankyuTimetableParser, HankyuImporter
├─ src/StationCountdown.Extension     net10.0-windows（MSIX）
│   ├─ StationCountdownCommandsProvider
│   ├─ Dock/        DepartureBand, OnLoadBandItem / OnLoadBandPage（自前で実装）
│   ├─ Pages/       StationsPage, StationDeparturesPage, AddHankyuStationForm,
│   │               AddFileStationForm, EditStationForm
│   ├─ Services/    TickService, StationStore（設定の監視・保持）,
│   │               RefreshScheduler, HolidayService, Log
│   └─ Assets/      syukujitsu.csv（同梱）
└─ tests/
    ├─ StationCountdown.Core.Tests        xUnit
    └─ StationCountdown.Importers.Tests   xUnit（架空データで作った fixture）
```

- 依存の向きは `Extension → Importers → Core` だけ。`Core` と `Importers` は Windows に依存しないので、Linux や CI でテストできる。
- 一般公開版（#4）では、`Extension` から `Importers` への参照を外し、`hankyu` 型の駅を無効にしてビルドできるようにする。MSBuild のプロパティ `IncludeImporters`（既定は `true`）で切り替える。
- YAML の処理には YamlDotNet を使う。AOT 対応のため、`Vecc.YamlDotNet.Analyzers.StaticGenerator` で静的コンテキストを生成する。

## 4. データ

### 4.1 保存先

`%LOCALAPPDATA%\StationCountdown\` に次のファイルを置く。

| パス | 内容 | 誰が書くか |
| --- | --- | --- |
| `stations.yaml` | 設定の正本 | フォーム、または利用者が手で編集 |
| `stations.yaml.bak` | フォームで保存する直前の内容 | 拡張機能 |
| `timetables/{id}.yaml` | 正規化した時刻表 | インポーター、または利用者 |
| `holidays.csv` | 祝日 CSV の更新版キャッシュ | 拡張機能 |
| `schemas/*.schema.json` | エディタで補完・チェックするための JSON Schema | 拡張機能（初回起動時にコピー） |
| `logs/` | ログ（7 日でローテーション） | 拡張機能 |

**時刻表データ（阪急など）は、リポジトリにコミットしない。** テストには、架空のデータで作った fixture だけを使う。

### 4.2 `stations.yaml`

```yaml
# yaml-language-server: $schema=./schemas/stations.schema.json
schemaVersion: 1
settings:
  refreshIntervalDays: 7
  departuresListLimit: 30
stations:
  - id: hankyu-umeda-takarazuka
    name: 大阪梅田
    directionLabel: 宝塚方面
    walkMinutes: 8
    source:
      type: hankyu
      stationPageUrl: https://www.hankyu.co.jp/station/umeda.html
      line: 宝塚線
      direction: 宝塚方面
  - id: tokyu-miyamaedaira-shibuya
    name: 宮前平
    directionLabel: 渋谷方面
    walkMinutes: 10
    source:
      type: file
dayTypeOverrides:
  - { date: 2026-12-31, dayType: saturdayHoliday }
```

検証ルール（違反があれば、行番号付きのエラーにする）:

| 項目 | ルール |
| --- | --- |
| `schemaVersion` | `1` のみ |
| `id` | `^[a-z0-9]+(-[a-z0-9]+)*$`、64 文字以内、重複不可。バンドの Command ID は `com.laplusdestiny.stationcountdown.band.{id}` |
| `name` / `directionLabel` | 空文字は不可 |
| `walkMinutes` | 0〜120 の整数 |
| `source.type` | `hankyu` または `file`。`hankyu` の場合は `stationPageUrl`・`line`・`direction` が必須で、URL は `https://www.hankyu.co.jp/` で始まること |
| `dayTypeOverrides[].dayType` | §4.3 に挙げた 4 種類のいずれか |
| `settings.*` | §6.3 の範囲内であること。省略可 |

### 4.3 `timetables/{id}.yaml`

```yaml
# yaml-language-server: $schema=../schemas/timetable.schema.json
schemaVersion: 1
stationId: hankyu-umeda-takarazuka
fetchedAt: 2026-11-09T12:00:00+09:00
sourceUrls:
  - https://www.hankyu.co.jp/station/html/HK-01_ta_2_w.html
  - https://www.hankyu.co.jp/station/html/HK-01_ta_2_h.html
services:
  weekday:
    - 05:00 普通 宝塚
    - 05:20 普通 宝塚
    - 24:10 普通 雲雀丘花屋敷
  saturdayHoliday:
    - 05:00 普通 宝塚
```

- 1 本を 1 行で書き、`"HH:MM 種別 行先"` の形にする。項目は半角スペースで区切る。種別と行先にスペースは使えない。
- `HH` は 00〜29（24 以降は営業日の翌日の未明）、`MM` は 00〜59。
- 同じ種別のダイヤの中では、時刻の昇順に並べる。順番が違っても読み込めるようにし、読み込むときに並べ替える。
- `services` のキーは `weekday`・`saturday`・`holiday`・`saturdayHoliday` のいずれか。最低 1 つは必要。
- `stationId` は、ファイル名の `{id}` と一致していること。
- `fetchedAt` と `sourceUrls` は、`file` 型の駅では省略してよい。

## 5. 計算ルール

### 5.1 基準時刻と営業日

- 常に JST（`TimeZoneInfo` の `Tokyo Standard Time`）で計算する。PC のタイムゾーン設定には依存させない。
- **営業日の区切りは 3:00**。0:00〜2:59 は前日の営業日として扱い、時刻表上の時刻は 24:00〜26:59 にあたる。

### 5.2 ダイヤ種別の決め方

営業日の日付 D について、上から順に判定する。

1. `dayTypeOverrides` に D があれば、それを使う。ただし、その種別のダイヤが時刻表にない場合は、2 以降で判定し直す。
2. D が日曜または祝日なら、`holiday` があればそれを、なければ `saturdayHoliday` を使う。
3. D が土曜なら、`saturday` があればそれを、なければ `saturdayHoliday` を使う。
4. それ以外の日は `weekday` を使う。
5. 該当するダイヤが時刻表にない場合、そのバンドは `時刻表未取得` と表示する（§7）。

### 5.3 祝日データ

- 内閣府の `syukujitsu.csv` を `Assets/` に同梱する。文字コードは Shift_JIS で、`yyyy/M/d,名称` の形式。
  - 出典表示: 「出典：内閣府『国民の祝日について』（公共データ利用規約 第1.0版）」を README と About に記載する。
- 起動時に、同梱データとキャッシュの最終日が今年の 12/31 より前であれば、`https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv` を取得して `holidays.csv` を更新する。取得は 1 日 1 回まで。
- 祝日データが手に入らない年は、土日だけで判定する。

### 5.4 次発の計算

各時刻は次のように定める（すべて JST）。

- 発車時刻 `dep` ＝ 営業日 D の 0:00 ＋ 時刻表の `HH:MM`
- 家を出る時刻 `leave` ＝ `dep` − `walkMinutes`
- 残り分数 `remain` ＝ (`leave` − `now`) を分単位で切り捨てたもの

次のように選ぶ。

- **Dock に出す電車**: 今の営業日の電車のうち、`leave ≥ now` を満たす最初の 1 本。
- **Subtitle の「次」**: その次の電車で、同じく `leave ≥ now` を満たすもの。
- 今の営業日に該当する電車がなければ、`本日終了` の状態にする（§6.1）。このときは翌営業日の始発を表示する。

### 5.5 一覧（StationDeparturesPage）

- 今の営業日の電車のうち、まだ発車していない（`dep ≥ now`）ものを並べる。
- `leave < now` の電車には `間に合わない` と付け、表示も薄くする。
- 件数が上限（既定は 30 件）に届かなければ、翌営業日の始発から続けて並べる。その境目には区切り（`Separator`）として「翌日」を入れる。

### 5.6 更新のタイミング

- `TickService` がタイマーを 1 本だけ持ち、毎分 0 秒（JST）に全バンドを計算し直す。
- 次のイベントのときにも、すぐに計算し直す。
  - `SystemEvents.TimeChanged`（時刻設定の変更）
  - `SystemEvents.PowerModeChanged`（Resume、スリープからの復帰）
  - `stations.yaml` や時刻表ファイルの変更
- タイマーに登録するのは、Dock に表示されているバンドだけ（§6.1 の OnLoad 方式）。どのバンドも表示されていない間は、タイマーを止める。

## 6. UI

### 6.1 Dock のバンド

- 駅と方面の組み合わせごとに 1 つのバンドにする。`GetDockBands()` は、`stations.yaml` にあるすべての駅のバンドを返す。Dock に置くかどうかは、利用者が Dock の編集画面で選ぶ。
- バンドの作りは、`OnLoadBandItem`（`CommandItem`）の中に `OnLoadBandPage`（`IDynamicListPage`）があり、その中に `DepartureBand`（`ListItem`）が 1 つある構成にする。
  - PowerToys の TimeDate 拡張にある `OnLoadDockBandItem` / `OnLoadDynamicListPage` と同じ方式で、`ItemsChanged` の `add` / `remove` を数えて、表示されているかどうかを知る。
  - 参考元は MIT ライセンスなので、コピーした場合は著作権表示を残す。
- `DepartureBand.Command` は `StationDeparturesPage` にする。クリックすると Command Palette がその駅の一覧ページで開く。

| 状態 | Title | Subtitle |
| --- | --- | --- |
| 通常 | `{種別} {行先} あと{remain}分` | `{HH:MM}発 ／ 次 {HH:MM} {種別}`（「次」がない場合は `{HH:MM}発` だけ） |
| `remain = 0` | `{種別} {行先} 今すぐ` | 同上 |
| 本日終了 | `本日終了` | `始発 {H:MM} {種別} {行先}` |
| 時刻表がない、または該当するダイヤがない | `時刻表未取得` | `{name} {directionLabel}` |
| 取得に失敗し、前回のデータで表示中 | 通常と同じ | 通常の Subtitle の末尾に ` ／ 取得失敗` を付ける |

- 時刻表の 24 時以降の時刻は、表示するときに 0 時台に戻す（`24:10` は `0:10` と表示）。
- アイコンは Segoe Fluent Icons の電車のグリフにする。
- 右クリックのメニュー（`MoreCommands`）は「時刻表を再取得」（`hankyu` 型のみ）、「駅を編集」、「設定フォルダを開く」の 3 つ。

### 6.2 Command Palette 内のページ

| ページ / コマンド | 内容 |
| --- | --- |
| トップレベル「駅カウントダウン」（`StationsPage`） | 登録した駅の一覧。各行の Title は `{name} {directionLabel}`、Subtitle はバンドの Title と同じ文言。右クリックで「編集」「削除」「再取得」ができる。末尾に「＋ 阪急の駅を追加」「＋ ファイルで駅を追加」「設定フォルダを開く」を置く。設定に誤りがあれば、先頭にエラーの行を出す。 |
| `StationDeparturesPage` | §5.5 の一覧。1 行は `{HH:MM} {種別} {行先}` の形で、Subtitle は `あと{remain}分` か `間に合わない`。 |
| `AddHankyuStationForm` | 2 段階のフォーム。(1) 駅ページの URL を入力すると、拡張機能が駅ページを取得・解析し、路線と方面の候補を作る。(2) 路線・方面を選び、表示名（既定は駅名）・徒歩の分数（既定は 5）・ID（既定は自動生成）を入力する。保存すると、`stations.yaml` に追記して時刻表を取得する。 |
| `AddFileStationForm` | ID・表示名・方面・徒歩の分数を入力する。保存すると `stations.yaml` に追記し、時刻表ファイルを置く場所を Toast で知らせて、フォルダを開く。 |
| `EditStationForm` | 表示名・方面・徒歩の分数を変更する。 |
| 削除 | `ConfirmationArgs` で確認してから、`stations.yaml` から取り除く。時刻表ファイルは残す。 |

フォームから保存するときの決まり:

- 保存する直前に、`stations.yaml` を `stations.yaml.bak` にコピーする。
- 保存すると、手で書いたコメントは消える。フォームの上部に、その旨を注意書きとして出す。

### 6.3 全体の設定

設定はすべて YAML で管理する方針なので、Toolkit の `JsonSettingsManager` は使わない。全体の設定も `stations.yaml` の `settings:` に置く（§4.2）。どの項目も省略でき、省略した項目は既定値になる。

| キー | 内容 | 既定値 | 範囲 |
| --- | --- | --- | --- |
| `settings.refreshIntervalDays` | 時刻表を自動で再取得する間隔（日） | 7 | 1〜30 |
| `settings.departuresListLimit` | 一覧に出す最大件数 | 30 | 5〜100 |

## 7. インポーター（阪急）

### 7.1 駅ページの解析（`HankyuStationPageParser`）

- 入力は駅ページ（例: `umeda.html`）の HTML。
- 現行ダイヤの `div.timetable_cnt` ごとに、`h4`（路線）・`h5`（方面）・リンク（「平日」と「土曜・休日」）を取り出す。
- 日付付きディレクトリ（例: `/station/html/20250222/`）は「ダイヤ改正版」として区別する。MVP では現行版だけを使い、改正版があればログに記録する。

### 7.2 時刻表ページの解析（`HankyuTimetableParser`）

- `li.timetable_hour` から「時」、`p.timetable_min` から「分」、`p.timetable_train_type` から種別（`［］` を除く）、`p.timetable_station_name` から行先を取り出す。
- 時の欄は 5→23→0 の順に並ぶ。**直前の時より小さい時が出てきたら、24 を足す**（0 は 24、1 は 25 になる）。
- 平日のページは `weekday`、「土曜・休日」のページは `saturdayHoliday` として扱う。
- 次のどれかに当たれば、解析失敗として扱う。
  - 取り出せた電車が 0 本
  - 分が範囲外
  - 時刻が昇順になっていない
- HTML の解析には AngleSharp を使う。AOT 対応はビルドで検証する（§10）。

### 7.3 取得のルール

- HTTP の User-Agent は `StationCountdown/{version} (+https://github.com/Laplusdestiny/cmdpal-station-countdown)` にする。
- 取得するのは、駅を追加したとき、手動で再取得したとき、`fetchedAt` から設定の日数が過ぎたときだけ。1 回の取得で送るリクエストは、駅ページ 1 件と時刻表ページ 2 件。
- 書き込みは一時ファイル経由で行い、成功したときだけ置き換える。

## 8. エラー処理

| 状況 | 動き |
| --- | --- |
| ネットワークエラーや HTTP エラー | 前回の時刻表で表示を続け、Subtitle に `取得失敗` と付ける。30 分おきに 3 回まで再試行し、それでも駄目なら次回の定期取得まで待つ。 |
| HTML の解析に失敗した | 同上。時刻表ファイルは上書きしない。 |
| `stations.yaml` が読めない、または検証に失敗した | 最後に正しく読めた設定で動き続ける。`StationsPage` の先頭に、エラー内容と行番号を表示する。初めて起動して読めない場合は、駅 0 件として扱う。 |
| 時刻表ファイルがない、壊れている、または `stationId` が一致しない | その駅だけ `時刻表未取得` と表示し、エラーをログに記録する。 |
| ハンドルされない例外 | タイマーやイベントの処理は、すべて try/catch で囲む。例外はログに記録して、拡張機能のプロセスは落とさない。 |

## 9. テスト

| 対象 | 方法 | 主なケース |
| --- | --- | --- |
| `Core` | xUnit（Linux / CI） | 営業日の区切り（2:59 と 3:00）、ダイヤ種別の決め方（上書き・祝日・振替休日・`saturday` がある場合とない場合）、徒歩時間の差し引きと切り捨て、`今すぐ`、本日終了と翌営業日の始発、一覧の「間に合わない」と翌日分の追加、YAML の読み書きと検証エラーの行番号、祝日 CSV（Shift_JIS）の解析 |
| `Importers` | xUnit と架空の HTML fixture | 駅ページから路線と方面を取り出す、改正版ディレクトリの区別、0 時台の 24 時台への変換、`［］` の除去、解析失敗の判定（0 本・昇順違反） |
| `Extension` | 手動で確認。CI では `windows-latest` でビルドする | Dock への表示、毎分の更新、一覧ページ、フォーム、手で編集したときの自動読み込み |

- 時計は `TimeProvider` で差し込み、固定の時刻でテストする。
- CI（GitHub Actions）:
  - `ubuntu-latest`: `dotnet test`（Core / Importers）
  - `windows-latest`: `dotnet build` で Extension を x64 の Release 構成でビルドする

## 10. 最初に検証すること（本実装の前）

最小限の試作（固定の文字列を毎分更新するバンド）を作り、Windows 上で次の 6 項目を確かめる。

| # | 検証項目 | 合格の条件 |
| --- | --- | --- |
| V1 | 8 時間以上放置した後も更新が続くか | 表示が最新の時刻のまま |
| V2 | 画面ロック・解除の後 | バンドが表示され、更新も続く |
| V3 | スリープからの復帰の後 | 同上 |
| V4 | YamlDotNet の静的コンテキストで、Release ビルド（トリミングあり）が動くか | 読み書きのラウンドトリップが成功する |
| V5 | AngleSharp を使った Release ビルド（トリミングあり）が動くか | fixture の解析が成功する |
| V6 | `OnLoad` 方式で、表示されているかどうかを判定できるか | ピン留めを外すとタイマーが止まる |

- V1〜V3 で止まる場合は、`CoAddRefServerProcess` でプロセスを維持する方法を試す。それでも駄目なら、既知の制限として README に書く（PowerToys#50367）。
- V4・V5 が駄目な場合は、代わりの手段を検討する。YAML は自前の最小限のパーサー（§4 の形式に限る）、HTML は正規表現での解析。

## 11. ライセンスと表示

- 本リポジトリは MIT ライセンス。
- PowerToys のコードを参考にした部分（`OnLoad` 方式）は、MIT ライセンスの著作権表示を残す。
- 祝日データの出典を README に記載する（§5.3）。
- 阪急・東急の時刻表データは配布しない。README に「時刻表の著作権は各事業者に帰属する。取得したデータは私的な利用の範囲で使う」と明記する。
