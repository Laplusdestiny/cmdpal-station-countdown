# Station Countdown MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Command Palette の Dock に、登録した駅・方面ごとに「家を出るまであと何分か」を表示する拡張機能（MVP）を作る。

**Architecture:** 3 つのプロジェクトに分ける。OS に依存しない `Core`（YAML の入出力、営業日、次発計算、表示文言）、阪急 HTML を取り込む `Importers`、薄い Windows 専用の `Extension`（Dock バンド、ページ、タイマー、ファイル監視）。ロジックはできるだけ `Core` に置き、Linux の CI でテストする。

**Tech Stack:** .NET 10 / C#、xUnit、YamlDotNet 16.3.0（RepresentationModel）、AngleSharp、Microsoft.CommandPalette.Extensions 0.12.260812002、MSIX、GitHub Actions

**Spec:** `docs/superpowers/specs/2026-11-09-station-countdown-design.md`（各タスクでは、ここに書かれた § 番号を参照する）

## Global Constraints

- `Core` と `Importers` のターゲットは `net10.0`。`<IsAotCompatible>true</IsAotCompatible>` を付け、`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` でトリミングや AOT の警告もエラーにする。`Extension` は公式テンプレートの設定（`net10.0-windows10.0.26100.0`、`TargetPlatformMinVersion 10.0.19041.0`、`x64;ARM64`）に従う。
- パッケージのバージョンは `Directory.Packages.props` でまとめて管理する。`Microsoft.CommandPalette.Extensions` は `0.12.260812002` に固定する。
- リフレクションに頼るシリアライザーは使わない。YAML は YamlDotNet の `YamlStream`、`YamlMappingNode`、`Emitter` で直接扱い、フォームの入力（JSON）は `System.Text.Json.JsonDocument` で読む。
  - 実装やテストの中で YAML の実現が難しいと判断したら、作業を止めて報告する。報告したら、spec §3 の「JSON への切り替え条件」に従って JSON（`JsonSerializerContext`）に切り替える。独自の YAML パーサーは作らない。
- JST は `TimeSpan.FromHours(9)` の固定オフセットで扱う。日本には夏時間がないので、spec §5.1 の `Tokyo Standard Time` と同じ結果になる。
- 時刻の表示は、時をゼロ埋めしない `H:MM` にする（`5:00`、`12:34`、`0:10`）。spec の `{HH:MM}` と `{H:MM}` という表記の揺れは、すべてこの形に統一する。
- 時刻表データ（阪急・東急の実際のデータ）はコミットしない。テストの fixture は、駅名・時刻とも架空のものだけを使う。
- 名前空間は、プロジェクト名と同じ `StationCountdown.Core`、`StationCountdown.Importers`、`StationCountdown.Extension` にする。
- バンドの Command ID は `com.laplusdestiny.stationcountdown.band.{id}` にする。
- コミットメッセージの末尾には、必ず `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` を付ける。
- 実装は `main` から切った `feat/mvp` ブランチで行い、push したら draft PR を作る。

## Review Focus

1. **全角スペースやタブが混じった時刻表の行**（Windows で手入力すると起きやすい）。期待する動作：落ちずに、行番号付きのエラーになる。→ Task 2
2. **PC のタイムゾーンが JST 以外**（`now` が UTC や +01:00 の `DateTimeOffset`）。期待する動作：JST のときと同じ営業日・同じ次発になる。→ Task 5
3. **エディタが保存している途中で、空や途中までの `stations.yaml` を読んだ**。期待する動作：最後に正しく読めた設定を使い続け、エラーだけを表に出す。→ Task 3
4. **メモ帳で保存した BOM 付きの UTF-8 や CRLF の YAML**。期待する動作：BOM や改行コードがない場合と同じ結果で読める。→ Task 2・Task 3
5. **祝日 CSV のヘッダー行・末尾の空行・前後の空白**。期待する動作：無視して読み込む。→ Task 4

---

## File Structure

```
StationCountdown.sln
Directory.Build.props / Directory.Packages.props / nuget.config / .gitignore / .editorconfig
.github/workflows/ci.yml
schemas/stations.schema.json, schemas/timetable.schema.json
src/StationCountdown.Core/
  Model/DayType.cs, Departure.cs, Timetable.cs, StationConfig.cs, StationsDocument.cs
  Yaml/YamlErrors.cs (ConfigError, LoadResult<T>), YamlNodeReader.cs (共通ヘルパ)
  Timetables/TimetableFile.cs
  Config/StationsFile.cs, StationsState.cs
  Calendar/HolidayCalendar.cs, ServiceDayResolver.cs
  Countdown/BandState.cs, NextDepartureCalculator.cs, CountdownFormatter.cs
  Refresh/RefreshPolicy.cs
src/StationCountdown.Importers/
  ITimetableImporter.cs, ImportException.cs, ImporterHttp.cs
  Hankyu/HankyuStationPageParser.cs, HankyuTimetableParser.cs, HankyuImporter.cs
src/StationCountdown.Extension/        (テンプレート由来 + 以下)
  Dock/OnLoadBandPage.cs, OnLoadBandItem.cs, DepartureBand.cs
  Services/AppPaths.cs, Log.cs, StationStore.cs, HolidayService.cs, TickService.cs, RefreshScheduler.cs
  Pages/StationsPage.cs, StationDeparturesPage.cs, AddHankyuStationPage.cs, AddFileStationPage.cs, EditStationPage.cs
  Assets/syukujitsu.csv
tests/StationCountdown.Core.Tests/
tests/StationCountdown.Importers.Tests/ (Fixtures/*.html)
tools/TrimProbe/                       (V4/V5 検証用コンソール)
```

---

### Task 1: ソリューションの土台と CI

**Files:**
- Create: `StationCountdown.sln`, `Directory.Build.props`, `Directory.Packages.props`, `nuget.config`, `.gitignore`（`dotnet new gitignore`）, `.editorconfig`
- Create: `src/StationCountdown.Core/StationCountdown.Core.csproj`, `src/StationCountdown.Importers/StationCountdown.Importers.csproj`
- Create: `tests/StationCountdown.Core.Tests/*.csproj`, `tests/StationCountdown.Importers.Tests/*.csproj`, `tests/StationCountdown.Core.Tests/SmokeTests.cs`
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: 上記プロジェクトの雛形と、`Importers` から `Core` への参照。`InternalsVisibleTo` で、各テストプロジェクトから `internal` にアクセスできるようにする。

- [ ] **Step 1:** `Directory.Packages.props` を作る。登録するパッケージは次のとおり。
  - `YamlDotNet` 16.3.0
  - `AngleSharp`（最新の安定版）
  - `xunit.v3`、`xunit.runner.visualstudio`、`Microsoft.NET.Test.Sdk`（いずれも最新の安定版。実際に使ったバージョンを書き込む）
  - `Microsoft.CommandPalette.Extensions` 0.12.260812002
  - その他、テンプレートの `Directory.Packages.props` にあるもの（`src/modules/cmdpal/ExtensionTemplate/TemplateCmdPalExtension/Directory.Packages.props` から写す）
- [ ] **Step 2:** `Directory.Build.props` を作り、`Nullable=enable`、`LangVersion=latest`、`TreatWarningsAsErrors=true`（`tests/` 以下は false）を指定する。`Core` と `Importers` の csproj には `IsAotCompatible=true` を付ける。
- [ ] **Step 3:** `SmokeTests.Core_assembly_loads`（`Assert.NotNull(typeof(StationCountdown.Core.Model.DayType))`）を書き、`dotnet test` が失敗する（型がまだない）ことを確かめる。
- [ ] **Step 4:** `Model/DayType.cs` に `public enum DayType { Weekday, Saturday, Holiday, SaturdayHoliday }` を追加する。
- [ ] **Step 5:** `dotnet test` を実行し、PASS することを確かめる。
- [ ] **Step 6:** `ci.yml` を作る。
  - ジョブ `test`（`ubuntu-latest`）：`actions/setup-dotnet`（`10.0.x`）を使い、`dotnet test tests/StationCountdown.Core.Tests` と `dotnet test tests/StationCountdown.Importers.Tests` を実行する。
  - ジョブ `extension` は Task 12 で追加する。
- [ ] **Step 7:** コミットする（`chore: scaffold solution and CI`）。push して、CI の `test` ジョブが緑になることを確かめる。

---

### Task 2: 時刻表モデルと `timetables/{id}.yaml` の読み書き（spec §4.3）

**Files:**
- Create: `src/StationCountdown.Core/Model/Departure.cs`, `Model/Timetable.cs`, `Yaml/YamlErrors.cs`, `Yaml/YamlNodeReader.cs`, `Timetables/TimetableFile.cs`, `schemas/timetable.schema.json`
- Test: `tests/StationCountdown.Core.Tests/DepartureTests.cs`, `TimetableFileTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record Departure(int Minutes, string TrainType, string Destination)`
    - `Minutes` は、営業日の 0:00 からの分数（0〜1799）。
    - `public static Departure ParseLine(string line)`：不正な行では `FormatException` を投げる。
    - `public string ToLine()`：`"HH:MM 種別 行先"` の形（時は 2 桁でゼロ埋め）。
    - `public string DisplayTime`：`H:MM` の形。`Minutes / 60 % 24` を時にする。
  - `public sealed record Timetable(string StationId, DateTimeOffset? FetchedAt, IReadOnlyList<string> SourceUrls, IReadOnlyDictionary<DayType, IReadOnlyList<Departure>> Services)`
    - `Services` の各リストは `Minutes` の昇順で、読み込み時に並べ替える。
  - `public sealed record ConfigError(int Line, string Message)`、`public sealed record LoadResult<T>(T? Value, IReadOnlyList<ConfigError> Errors) { public bool Ok => Errors.Count == 0 && Value is not null; }`
  - `public static class TimetableFile`
    - `LoadResult<Timetable> Parse(string yaml, string expectedStationId)`
    - `string Serialize(Timetable timetable)`
  - YAML のキー名と `DayType` の対応：`weekday` / `saturday` / `holiday` / `saturdayHoliday`。対応表は `DayTypeNames` として `TimetableFile` に置き、`internal` で Task 3 と共有する。

- [ ] **Step 1: 失敗するテストを書く**

```csharp
[Fact] public void ParseLine_reads_three_fields() {
    var d = Departure.ParseLine("05:20 普通 架空台");
    Assert.Equal(5 * 60 + 20, d.Minutes); Assert.Equal("普通", d.TrainType); Assert.Equal("架空台", d.Destination);
}
[Theory] [InlineData("24:10 普通 架空台", 1450)] [InlineData("29:59 普通 架空台", 1799)]
public void ParseLine_accepts_hours_up_to_29(string line, int minutes) => Assert.Equal(minutes, Departure.ParseLine(line).Minutes);
[Theory]
[InlineData("30:00 普通 架空台")] [InlineData("05:60 普通 架空台")] [InlineData("5:00 普通 架空台")]
[InlineData("05:00 普通")] [InlineData("05:00 普通 架空 台")]
[InlineData("05:00　普通　架空台")] [InlineData("05:00\t普通\t架空台")]   // Review Focus 1
public void ParseLine_rejects_invalid(string line) => Assert.Throws<FormatException>(() => Departure.ParseLine(line));
[Theory] [InlineData(1450, "0:10")] [InlineData(300, "5:00")] [InlineData(754, "12:34")]
public void DisplayTime_wraps_after_24(int m, string s) => Assert.Equal(s, new Departure(m, "普通", "x").DisplayTime);
```

`TimetableFileTests` に書くテスト：

- `Parse_valid_file`：spec §4.3 の例を、駅名を架空のものに替えて使う。`Services[DayType.Weekday]` が 3 件で、最後の `Minutes` が 1450 になること。
- `Parse_sorts_unsorted_lines`
- `Parse_reports_line_number_for_bad_departure`：5 行目に `05:00　普通　架空台` を置いた YAML で、`Errors` に `Line == 5` のエラーが含まれること。
- `Parse_rejects_stationId_mismatch`
- `Parse_rejects_unknown_service_key`（例：`sunday`）
- `Parse_rejects_no_services`
- `Parse_rejects_schemaVersion_2`
- `Parse_allows_missing_fetchedAt_and_sourceUrls`
- `Parse_handles_bom_and_crlf`：先頭に `"﻿"` を付け、改行を `\r\n` にした入力でも、付けない場合と同じ結果になること（Review Focus 4）。
- `Parse_empty_string_is_error_not_exception`
- `Serialize_then_Parse_roundtrips`

- [ ] **Step 2:** `dotnet test --filter "FullyQualifiedName~Departure|FullyQualifiedName~TimetableFile"` を実行し、コンパイルエラーで FAIL することを確かめる。
- [ ] **Step 3:** `Departure` を実装する。正規表現は `^(?<h>[0-2][0-9]):(?<m>[0-5][0-9]) (?<t>[^\s　]+) (?<d>[^\s　]+)$` を使い、`h <= 29` も確認する。
- [ ] **Step 4:** `YamlNodeReader` を実装する。役割は次のとおり。
  - 先頭の BOM を取り除いてから `YamlStream.Load` する。
  - `YamlException` は `ConfigError(ex.Start.Line, ex.Message)` に変換する。
  - `GetScalar`、`GetInt`、`GetSequence` などの取得ヘルパを用意する。いずれも、エラーには `node.Start.Line` を行番号として付ける。
- [ ] **Step 5:** `TimetableFile.Parse` と `TimetableFile.Serialize` を実装する。
  - `Serialize` は `YamlMappingNode` を組み立てて、`YamlStream.Save` で書き出す。
  - 時刻表の各行は、プレーンなスカラーとして出力する。
  - `fetchedAt` は ISO 8601 形式（`yyyy-MM-ddTHH:mm:sszzz`）で書く。
- [ ] **Step 6:** `schemas/timetable.schema.json` を作る（JSON Schema draft 2020-12）。
  - `schemaVersion` は const 1。
  - `services` は `propertyNames` を 4 種類に限定し、値は文字列の配列。各文字列は Step 3 と同じ正規表現にする。
- [ ] **Step 7:** テストを実行し、すべて PASS することを確かめる。
- [ ] **Step 8:** コミットする（`feat(core): timetable model and YAML file`）。

---

### Task 3: `stations.yaml` の読み書きと、最後に正しく読めた設定の保持（spec §4.2、§6.3、§8）

**Files:**
- Create: `src/StationCountdown.Core/Model/StationConfig.cs`, `Model/StationsDocument.cs`, `Config/StationsFile.cs`, `Config/StationsState.cs`, `schemas/stations.schema.json`
- Test: `tests/StationCountdown.Core.Tests/StationsFileTests.cs`, `StationsStateTests.cs`

**Interfaces:**
- Consumes: `ConfigError`、`LoadResult<T>`、`YamlNodeReader`、`DayTypeNames`（Task 2）
- Produces:
  - `public enum SourceType { Hankyu, File }`
  - `public sealed record StationSource(SourceType Type, string? StationPageUrl = null, string? Line = null, string? Direction = null)`
  - `public sealed record StationConfig(string Id, string Name, string DirectionLabel, int WalkMinutes, StationSource Source) { public string BandCommandId => $"com.laplusdestiny.stationcountdown.band.{Id}"; }`
  - `public sealed record GlobalSettings(int RefreshIntervalDays = 7, int DeparturesListLimit = 30)`
  - `public sealed record DayTypeOverride(DateOnly Date, DayType DayType)`
  - `public sealed record StationsDocument(GlobalSettings Settings, IReadOnlyList<StationConfig> Stations, IReadOnlyList<DayTypeOverride> DayTypeOverrides) { public static StationsDocument Empty { get; } }`
  - `public static class StationsFile`
    - `LoadResult<StationsDocument> Parse(string yaml)`
    - `string Serialize(StationsDocument doc)`：先頭に `# yaml-language-server: $schema=./schemas/stations.schema.json` の 1 行を付ける。
  - `public sealed class StationsState`
    - `StationsDocument Current { get; }`（初期値は `Empty`）
    - `IReadOnlyList<ConfigError> Errors { get; }`
    - `void Apply(LoadResult<StationsDocument> result)`

- [ ] **Step 1: 失敗するテストを書く**

`StationsFileTests` に書くテスト：

- `Parse_spec_example`：spec §4.2 の例を使う。駅は 2 件で、`Source.Type` はそれぞれ `Hankyu` と `File`。`Settings.RefreshIntervalDays` が 7、`DayTypeOverrides[0]` が (2026-12-31, `SaturdayHoliday`) になること。
- `Parse_defaults_when_settings_missing`
- 次の不正な入力は、どれもエラーになり、行番号が付くこと（`[Theory]` にまとめる）。
  - `id` が `Bad_ID`
  - `id` の重複
  - `id` が 65 文字
  - `name` が空
  - `walkMinutes` が -1 と 121
  - `source.type` が `odpt`
  - `hankyu` 型で `line` が欠けている
  - `hankyu` 型の `stationPageUrl` が `https://example.com/`
  - `refreshIntervalDays` が 0 と 31
  - `departuresListLimit` が 4 と 101
  - `dayType` が `sunday`
  - `date` が `2026-13-01`
- `Parse_handles_bom_and_crlf`（Review Focus 4）
- `Serialize_then_Parse_roundtrips`
- `Serialize_starts_with_schema_comment`

`StationsStateTests` に書くテスト：

- `Apply_ok_replaces_current_and_clears_errors`
- `Apply_error_keeps_last_good_document`（Review Focus 3）：正しい文書を適用してから、空文字列と YAML を途中で切ったものを `Parse` して適用する。`Current` は前の文書のまま、`Errors` だけが入っていること。
- `First_apply_error_leaves_empty_document`

- [ ] **Step 2:** テストを実行し、FAIL することを確かめる。
- [ ] **Step 3:** レコード型、`StationsFile`、`StationsState` を実装する。
  - 検証ルールは spec §4.2 の表と、§6.3 の範囲をそのまま使う。
  - `id` の正規表現は `^[a-z0-9]+(-[a-z0-9]+)*$` で、64 文字以内。
  - `stationPageUrl` は `https://www.hankyu.co.jp/` で始まること。
- [ ] **Step 4:** `schemas/stations.schema.json` を作る。内容は Step 3 と同じルールにする。
- [ ] **Step 5:** テストを実行し、PASS することを確かめる。
- [ ] **Step 6:** コミットする（`feat(core): stations.yaml config and last-good state`）。

---

### Task 4: 祝日カレンダー（spec §5.3）

**Files:**
- Create: `src/StationCountdown.Core/Calendar/HolidayCalendar.cs`
- Test: `tests/StationCountdown.Core.Tests/HolidayCalendarTests.cs`

**Interfaces:**
- Produces: `public sealed class HolidayCalendar`
  - `static HolidayCalendar Empty`
  - `static HolidayCalendar ParseCsv(byte[] shiftJisBytes)`
  - `bool IsHoliday(DateOnly date)`
  - `DateOnly? LastDate { get; }`
  - `bool Covers(DateOnly date)`：`LastDate >= date` のとき true。

- [ ] **Step 1: 失敗するテストを書く**。CSV のバイト列は、テストの中で `Encoding.GetEncoding(932)` を使って文字列から作る。テストの静的コンストラクタで `CodePagesEncodingProvider` を登録しておく。
  - `ParseCsv_reads_dates`：入力は `"国民の祝日・休日月日,国民の祝日・休日名称\r\n2026/1/1,元日\r\n2026/11/3,文化の日\r\n"`。2026-11-03 が祝日で、2026-11-04 は祝日でないこと。`LastDate` が 2026-11-03 であること。
  - `ParseCsv_ignores_header_blank_lines_and_whitespace`（Review Focus 5）：末尾に `"\r\n\r\n"` を付け、ある行を `" 2026/5/6 ,休日 "` にする。それでも 2026-05-06 が祝日と判定されること。
  - `Covers_false_after_last_date`
  - `Empty_has_no_holidays_and_no_last_date`
- [ ] **Step 2:** テストを実行し、FAIL することを確かめる。
- [ ] **Step 3:** `ParseCsv` を実装する。
  - 最初に `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` を呼ぶ。何度呼んでも問題ない。
  - 各行の最初の列を `yyyy/M/d` として `DateOnly.TryParseExact` で読み、読めない行は読み飛ばす。
- [ ] **Step 4:** テストを実行し、PASS することを確かめる。
- [ ] **Step 5:** コミットする（`feat(core): holiday calendar from Cabinet Office CSV`）。

---

### Task 5: 営業日とダイヤ種別の決定（spec §5.1、§5.2）

**Files:**
- Create: `src/StationCountdown.Core/Calendar/ServiceDayResolver.cs`
- Test: `tests/StationCountdown.Core.Tests/ServiceDayResolverTests.cs`

**Interfaces:**
- Consumes: `DayType`、`DayTypeOverride`、`HolidayCalendar`
- Produces: `public static class ServiceDayResolver`
  - `static readonly TimeSpan Jst = TimeSpan.FromHours(9)`
  - `const int ServiceDayStartHour = 3`
  - `static DateOnly GetServiceDate(DateTimeOffset now)`：`now` を JST に換算し、時が 3 未満なら前日を返す。
  - `static DateTimeOffset GetServiceDayOrigin(DateOnly serviceDate)`：その日の JST の 0:00。
  - `static DayType? ResolveDayType(DateOnly serviceDate, IReadOnlyCollection<DayType> available, IReadOnlyList<DayTypeOverride> overrides, HolidayCalendar holidays)`：spec §5.2 の 1〜5 を、この順に判定する。

- [ ] **Step 1: 失敗するテストを書く**
  - `GetServiceDate_before_3am_is_previous_day`：`2026-11-10T02:59:00+09:00` なら 11-09。
  - `GetServiceDate_at_3am_is_same_day`：`2026-11-10T03:00:00+09:00` なら 11-10。
  - `GetServiceDate_ignores_input_offset`（Review Focus 2）：`2026-11-09T17:59:00Z` なら 11-09、`2026-11-09T18:00:00Z` なら 11-10。`+01:00` で同じ瞬間を表した値も、結果が同じになること。
  - `ResolveDayType` を `[Theory]` で確かめる。

| 入力 | 期待値 |
| --- | --- |
| 平日、available={Weekday, SaturdayHoliday} | `Weekday` |
| 土曜、available={Weekday, SaturdayHoliday} | `SaturdayHoliday` |
| 土曜、available={Weekday, Saturday, Holiday} | `Saturday` |
| 日曜、available={Weekday, Saturday, Holiday} | `Holiday` |
| 祝日（月曜）、available={Weekday, SaturdayHoliday} | `SaturdayHoliday` |
| 上書き (date→SaturdayHoliday) の平日 | `SaturdayHoliday` |
| 上書きの種別が available にない（Saturday を指定し、available={Weekday, SaturdayHoliday}） | 2 以降の判定で `Weekday` |
| 平日、available={SaturdayHoliday} | `null` |
| `Covers` が false の年の祝日にあたる月曜 | `Weekday`（土日だけで判定） |

- [ ] **Step 2:** テストを実行し、FAIL することを確かめる。
- [ ] **Step 3:** 実装する。
- [ ] **Step 4:** テストを実行し、PASS することを確かめる。
- [ ] **Step 5:** コミットする（`feat(core): service day and day type resolution`）。

---

### Task 6: 次発の計算（spec §5.4、§5.5）

**Files:**
- Create: `src/StationCountdown.Core/Countdown/BandState.cs`, `Countdown/NextDepartureCalculator.cs`
- Test: `tests/StationCountdown.Core.Tests/NextDepartureCalculatorTests.cs`

**Interfaces:**
- Consumes: Task 2・3・4・5 の型
- Produces:
  - `public abstract record BandState`
    - `sealed record Upcoming(Departure Next, int RemainMinutes, Departure? Following) : BandState`
    - `sealed record EndOfService(Departure FirstTrain) : BandState`
    - `sealed record NoTimetable : BandState`
  - `public sealed record ListEntry(Departure Departure, int? RemainMinutes, bool IsNextServiceDay)`：`RemainMinutes == null` は「間に合わない」を表す。
  - `public sealed record CalcContext(StationConfig Station, Timetable? Timetable, IReadOnlyList<DayTypeOverride> Overrides, HolidayCalendar Holidays)`
  - `public static class NextDepartureCalculator`
    - `BandState ComputeBand(DateTimeOffset now, CalcContext ctx)`
    - `IReadOnlyList<ListEntry> ComputeList(DateTimeOffset now, CalcContext ctx, int limit)`

- [ ] **Step 1: 失敗するテストを書く**。共通の時刻表は架空のものにする。

```
weekday:          05:00 普通 A / 12:30 急行 B / 12:40 普通 A / 24:10 普通 A
saturdayHoliday:  06:00 普通 A
```

`walkMinutes` は 8。`now` は、2026-11-10（火）の JST で指定する。

  - `Band_skips_uncatchable`：12:23:00 のとき、12:30 の電車は家を出る時刻が 12:22 なので飛ばす。`Next` は 12:40、`RemainMinutes` は 9、`Following` は 24:10 になること。
  - `Band_floors_remaining_minutes`：12:19:01 のとき、12:30 の電車の `RemainMinutes` が 2 になること。
  - `Band_remain_zero_when_leave_equals_now`：12:22:00 のとき、`Next` が 12:30 で `RemainMinutes` が 0 になること。
  - `Band_after_midnight_uses_previous_service_day`：11-11 の 00:01 のとき、`Next` が 24:10（前日の営業日の電車）になること。
  - `Band_end_of_service_shows_next_day_first_train`：11-11 の 00:03 のとき、24:10 の電車は家を出る時刻が 00:02 なので間に合わない。11-11 は平日なので、`EndOfService.FirstTrain` は 05:00 になること。
  - `Band_end_of_service_before_weekend_uses_weekend_first_train`：11-14（土）の 00:30 のとき、営業日は 11-13（金）で、24:10 の電車にはもう間に合わない。翌営業日の 11-14 は土曜なので、`FirstTrain` は 06:00 になること。
  - `Band_no_timetable_when_null` と `Band_no_timetable_when_day_type_missing`
  - `List_marks_uncatchable_and_appends_next_day`：12:25 のとき、limit を 4 にする。結果は次の 4 件になること。
    - 12:30（`RemainMinutes == null`）
    - 12:40（7）
    - 24:10（`IsNextServiceDay == false`）
    - 05:00（`IsNextServiceDay == true`）
  - `List_excludes_departed`：12:31 のとき、12:30 の電車が含まれないこと。
  - `List_respects_limit`
- [ ] **Step 2:** テストを実行し、FAIL することを確かめる。
- [ ] **Step 3:** 実装する。
  - 発車時刻 `dep` は `GetServiceDayOrigin(date).AddMinutes(d.Minutes)` で求める。
  - 家を出る時刻 `leave` は `dep.AddMinutes(-walkMinutes)`。
  - 残り分数 `remain` は `(int)Math.Floor((leave - now).TotalMinutes)`。
  - 翌営業日を見るのは 1 日分だけ。翌営業日のダイヤ種別が解決できなければ、`NoTimetable` を返す。
- [ ] **Step 4:** テストを実行し、PASS することを確かめる。
- [ ] **Step 5:** コミットする（`feat(core): next departure calculation`）。

---

### Task 7: 表示文言と再取得のタイミング（spec §6.1、§6.2、§8）

**Files:**
- Create: `src/StationCountdown.Core/Countdown/CountdownFormatter.cs`, `Refresh/RefreshPolicy.cs`
- Test: `tests/StationCountdown.Core.Tests/CountdownFormatterTests.cs`, `RefreshPolicyTests.cs`

**Interfaces:**
- Consumes: `BandState`、`ListEntry`、`StationConfig`
- Produces:
  - `public sealed record BandText(string Title, string Subtitle)`
  - `public static class CountdownFormatter`
    - `BandText FormatBand(BandState state, StationConfig station, bool fetchFailed)`
    - `BandText FormatListEntry(ListEntry entry)`
    - `const string NextDaySeparatorTitle = "翌日"`
  - `public static class RefreshPolicy`
    - `const int MaxRetries = 3`
    - `static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30)`
    - `static DateTimeOffset NextAttempt(DateTimeOffset now, DateTimeOffset? fetchedAt, int intervalDays, int consecutiveFailures, DateTimeOffset? lastFailureAt)`

- [ ] **Step 1: 失敗するテストを書く**。期待する文字列は spec §6.1 の表のとおりで、半角・全角の区別も含めて完全に一致させる。

| ケース | Title | Subtitle |
| --- | --- | --- |
| Upcoming(12:34 急行 宝塚, 3, 12:40 普通) | `急行 宝塚 あと3分` | `12:34発 ／ 次 12:40 普通` |
| Upcoming(…, 0, null) | `急行 宝塚 今すぐ` | `12:34発` |
| EndOfService(5:00 普通 宝塚) | `本日終了` | `始発 5:00 普通 宝塚` |
| NoTimetable、駅は 大阪梅田 / 宝塚方面 | `時刻表未取得` | `大阪梅田 宝塚方面` |
| Upcoming で fetchFailed=true | 通常と同じ | `12:34発 ／ 次 12:40 普通 ／ 取得失敗` |
| ListEntry(12:30 急行 B, null) | `12:30 急行 B` | `間に合わない` |
| ListEntry(12:40 普通 A, 7) | `12:40 普通 A` | `あと7分` |
| ListEntry(…, 0) | 〃 | `今すぐ` |

`RefreshPolicy` のテスト（`now` は 2026-11-09T12:00+09:00 とする）：

| 条件 | 期待値 |
| --- | --- |
| fetchedAt が null、失敗 0 回 | `now` |
| fetchedAt が 11-05 12:00、7 日、失敗 0 回 | 11-12 12:00 |
| 失敗 1 回、lastFailureAt が 11:50 | 12:20 |
| 失敗 3 回、lastFailureAt が 11:50 | 12:20 |
| 失敗 4 回、lastFailureAt が 11:50、7 日 | 11-16 11:50 |

- [ ] **Step 2:** テストを実行し、FAIL することを確かめる。
- [ ] **Step 3:** 実装する。
- [ ] **Step 4:** テストを実行し、PASS することを確かめる。
- [ ] **Step 5:** コミットする（`feat(core): countdown text and refresh policy`）。

---

### Task 8: 阪急 駅ページの解析（spec §7.1）

**Files:**
- Create: `src/StationCountdown.Importers/ImportException.cs`, `Hankyu/HankyuStationPageParser.cs`
- Create: `tests/StationCountdown.Importers.Tests/Fixtures/hankyu_station_page.html`
- Test: `tests/StationCountdown.Importers.Tests/HankyuStationPageParserTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ImportException(string message, Exception? inner = null) : Exception(message, inner)`
  - `public sealed record HankyuDirectionLink(string Line, string Direction, Uri WeekdayUrl, Uri HolidayUrl)`
  - `public sealed record HankyuStationPage(IReadOnlyList<HankyuDirectionLink> Current, bool HasRevisedTimetable)`
  - `public static class HankyuStationPageParser`
    - `HankyuStationPage Parse(string html, Uri pageUrl)`：相対リンクは `pageUrl` を基準に解決する。

- [ ] **Step 1:** fixture を作る。中身は架空の駅で、実際のページと同じ構造にする。
  - 現行ダイヤとして、`div.timetable_cnt` が 2 つ。
    - 1 つ目：`h4` が「架空線」、`h5` が「北方面」。`ul.timetable_cnt_link` の中に、`a` が「平日」（`/station/html/HK-99_kk_2_w.html`）と「土曜・休日」（`…_h.html`）。
    - 2 つ目：「架空支線」の「南方面」。
  - そのあとに `p.diagram_txt` と、日付付きディレクトリ（`/station/html/20990101/HK-99_kk_2_w.html`）を使った `div.timetable_cnt` を 1 つ置く。
- [ ] **Step 2: 失敗するテストを書く**
  - `Parse_extracts_current_directions`：`Current` が 2 件で、1 件目が ("架空線", "北方面", `https://www.hankyu.co.jp/station/html/HK-99_kk_2_w.html`, `…_h.html`) になること。
  - `Parse_excludes_dated_revision_and_flags_it`：`Current` に `/20990101/` を含む URL がなく、`HasRevisedTimetable` が true になること。
  - `Parse_throws_when_no_timetable_blocks`：`<html></html>` を渡すと `ImportException` になること。
- [ ] **Step 3:** テストを実行し、FAIL することを確かめる。
- [ ] **Step 4:** AngleSharp の `HtmlParser` で実装する。リンクのパスが `/station/html/\d{8}/` に当てはまるブロックを、「改正版」として扱う。
- [ ] **Step 5:** テストを実行し、PASS することを確かめる。
- [ ] **Step 6:** コミットする（`feat(importers): Hankyu station page parser`）。

---

### Task 9: 阪急 時刻表ページの解析（spec §7.2）

**Files:**
- Create: `src/StationCountdown.Importers/Hankyu/HankyuTimetableParser.cs`
- Create: `tests/StationCountdown.Importers.Tests/Fixtures/hankyu_timetable_weekday.html`
- Test: `tests/StationCountdown.Importers.Tests/HankyuTimetableParserTests.cs`

**Interfaces:**
- Consumes: `Departure`（Core）、`ImportException`
- Produces: `public static class HankyuTimetableParser`
  - `IReadOnlyList<Departure> Parse(string html)`：結果は時刻の昇順。

- [ ] **Step 1:** fixture を作る。実際のページと同じ構造を、架空のデータで再現する。

```html
<div class="sec01_inner_table"><ul>
 <li><ul>
  <li class="timetable_hour hour_w"><span data-wovn-ignore>5</span><span class="about_alt">時</span></li>
  <li class="timetable_mins"><ul>
   <li><a><p class="timetable_min"><span data-wovn-ignore>00</span><span class="about_alt">分</span></p>
       <div class="timetable_att"><p class="timetable_train_type">［普通］</p><p class="timetable_station_name">架空台</p></div></a></li>
   <li><a><p class="timetable_min"><span data-wovn-ignore>30</span>…</p>
       <div class="timetable_att"><p class="timetable_train_type">［急行］</p><p class="timetable_station_name">架空台</p></div></a></li>
  </ul></li>
 </ul></li>
 <!-- 23 時 1 本（23:50 普通 架空台）、0 時 1 本（00:10 普通 架空町）を同じ形で -->
</ul></div>
```

- [ ] **Step 2: 失敗するテストを書く**
  - `Parse_reads_departures`：4 件で、1 件目が `05:00 普通 架空台`（`ToLine()` で比較）、2 件目が `05:30 急行 架空台` になること。
  - `Parse_converts_midnight_hour_to_24`：最後が `24:10 普通 架空町` になること。
  - `Parse_strips_brackets_from_train_type`
  - `Parse_ignores_hour_class_suffix`：`hour_w` を `hour_h` に置き換えた HTML でも、同じ結果になること。
  - `Parse_throws_on_zero_departures`
  - `Parse_throws_on_minute_out_of_range`：`75` を入れる。
  - `Parse_throws_on_non_increasing_hours`：5 時、7 時、6 時の順に並べる。この場合 6 時は 7 時より小さいので 24 を足して 30 時になり、範囲外として `ImportException` になる。
- [ ] **Step 3:** テストを実行し、FAIL することを確かめる。
- [ ] **Step 4:** 実装する。
  - `li.timetable_hour` と、その次にある `li.timetable_mins` を組にして読む。
  - 時の繰り上げは、前の時より小さい時が出てきたら `+24` する。
  - `Departure` を組み立てるときに `FormatException` が出たら、`ImportException` に包み直して投げる。
- [ ] **Step 5:** テストを実行し、PASS することを確かめる。
- [ ] **Step 6:** コミットする（`feat(importers): Hankyu timetable parser`）。

---

### Task 10: 阪急インポーター（HTTP の取得と原子的な書き込み、spec §7.3）

**Files:**
- Create: `src/StationCountdown.Importers/ITimetableImporter.cs`, `ImporterHttp.cs`, `Hankyu/HankyuImporter.cs`, `src/StationCountdown.Core/Timetables/TimetableWriter.cs`
- Test: `tests/StationCountdown.Importers.Tests/HankyuImporterTests.cs`, `tests/StationCountdown.Core.Tests/TimetableWriterTests.cs`

**Interfaces:**
- Consumes: Task 8・9 の型、`Timetable`、`TimetableFile`、`StationConfig`
- Produces:
  - `public interface ITimetableImporter`
    - `bool CanImport(StationConfig station)`
    - `Task<Timetable> ImportAsync(StationConfig station, CancellationToken ct)`
  - `public static class ImporterHttp`
    - `static HttpClient Create(string version, HttpMessageHandler? handler = null)`：User-Agent を `StationCountdown/{version} (+https://github.com/Laplusdestiny/cmdpal-station-countdown)` にし、タイムアウトは 20 秒。
  - `public sealed class HankyuImporter(HttpClient http, TimeProvider time) : ITimetableImporter`
    - `Task<HankyuStationPage> FetchStationPageAsync(Uri pageUrl, CancellationToken ct)`
    - `ImportAsync`：駅ページを取得し、`Line` と `Direction` が一致するリンクを探す。見つからなければ `ImportException`。平日と土曜・休日のページを取得して、`Timetable` を返す。返す値の中身は次のとおり。
      - `Services`：平日ページの結果を `Weekday`、土曜・休日ページの結果を `SaturdayHoliday` にする。
      - `FetchedAt`：`time.GetLocalNow()` を JST に換算した時刻。
      - `SourceUrls`：取得した時刻表ページ 2 件の URL。
    - `event Action<string>? RevisedTimetableDetected`：駅ページの `HasRevisedTimetable` が true のときに、`stationId` を引数にして発火する。Task 15 の `RefreshScheduler` がこれを受けて `Log.Info` に記録する（spec §7.1）。テストに `ImportAsync_raises_revised_timetable_event` を加える。
  - `public static class TimetableWriter`（`Core` の `Timetables/` に置く）
    - `static void WriteAtomic(string path, Timetable t)`：`path + ".tmp"` に書き込んでから `File.Move(tmp, path, overwrite: true)` で置き換える。

- [ ] **Step 1: 失敗するテストを書く**。HTTP は、URL ごとに fixture を返す偽の `HttpMessageHandler` で差し替える。
  - `ImportAsync_builds_timetable_from_both_pages`：`Services` が 2 種類あり、`SourceUrls` が 2 件、`FetchedAt` が偽の `TimeProvider` の値と一致すること。
  - `ImportAsync_sends_user_agent`
  - `ImportAsync_throws_when_direction_not_found`
  - `ImportAsync_wraps_http_error`：500 を返すと、`ImportException` になること。
  - `TimetableWriter_WriteAtomic_replaces_file_and_leaves_no_tmp`（一時ディレクトリで確かめる。テストは `Core.Tests` に置く）
- [ ] **Step 2:** テストを実行し、FAIL することを確かめる。
- [ ] **Step 3:** 実装する。
- [ ] **Step 4:** テストを実行し、PASS することを確かめる。
- [ ] **Step 5:** コミットする（`feat(importers): Hankyu importer and atomic timetable writer`）。

---

### Task 11: トリミングの検証プローブ（spec §10 の V4・V5）

**Files:**
- Create: `tools/TrimProbe/TrimProbe.csproj`（`net10.0` のコンソールアプリ。`PublishTrimmed=true`、`TrimmerSingleWarn=false`、`TreatWarningsAsErrors=true`）, `tools/TrimProbe/Program.cs`
- Modify: `.github/workflows/ci.yml`（`test` ジョブにステップを追加する）

**Interfaces:**
- Consumes: `StationsFile`、`TimetableFile`、`HankyuTimetableParser`。fixture は `tests/StationCountdown.Importers.Tests/Fixtures/hankyu_timetable_weekday.html` を、`Content` としてコピーする。

- [ ] **Step 1:** `Program.cs` を書く。処理の流れは次のとおり。
  1. spec §4.2 の例（架空化したもの）を `StationsFile.Parse` で読み、`Serialize` してもう一度 `Parse` する。
  2. 時刻表の YAML で、同じように往復させる。
  3. fixture を `HankyuTimetableParser.Parse` で解析する。
  4. 3 つとも期待どおりなら `PROBE OK` を表示して終了コード 0 で終わる。どれかが違えば、終了コード 1 で終わる。
- [ ] **Step 2:** `dotnet publish tools/TrimProbe -c Release -r linux-x64 --self-contained` を実行する。トリミングの警告が 0 件で成功すること。
- [ ] **Step 3:** 出力された実行ファイルを動かし、`PROBE OK` が表示されることを確かめる。
- [ ] **Step 4:** CI に、Step 2 と Step 3 を追加する。
- [ ] **Step 5:** コミットする（`test: trimmed publish probe for YAML and HTML parsing`）。push して、CI が緑になることを確かめる。
  - **失敗した場合**は、止まって報告する。
    - YAML の失敗は、spec §3 の「JSON への切り替え条件」に当たる。報告したうえで、Task 2・3・11 を JSON 版に書き換える。テストの名前と期待値はそのまま使い、行番号の検証だけは JSON のパスの検証に置き換える。
    - HTML の失敗は、Task 8・9 の解析を正規表現に書き換える。

---

### Task 12: Extension の雛形と Dock の試作（spec §10 の V1〜V3・V6）

**Files:**
- Create: `src/StationCountdown.Extension/`
  - PowerToys の `src/modules/cmdpal/ExtensionTemplate/TemplateCmdPalExtension/TemplateCmdPalExtension/` を丸ごとコピーし、次のように置き換える。
    - `TemplateCmdPalExtension` → `StationCountdown.Extension`（ファイル名・名前空間・`Executable`）
    - クラス名 → `StationCountdownExtension`、`StationCountdownCommandsProvider`
    - `TemplateDisplayName` → `Station Countdown`
    - GUID の `FFFFFFFF-…` → 新しく生成した GUID。manifest の `com:Class Id` と `[Guid]` の両方を同じ値にする。
  - `Dock/OnLoadBandPage.cs`, `Dock/OnLoadBandItem.cs`：PowerToys の `ext/Microsoft.CmdPal.Ext.TimeDate/Pages/OnLoadDynamicListPage.cs` と `OnLoadDockBandItem.cs` を移植する。冒頭の MIT 著作権表示は残す。
  - `Services/AppPaths.cs`, `Services/Log.cs`, `Services/TickService.cs`
- Modify: `StationCountdown.sln`（Extension を追加する）, `.github/workflows/ci.yml`（ジョブ `extension` を追加する）

**Interfaces:**
- Produces:
  - `internal sealed partial class OnLoadBandItem(IListItem[] items, string id, string title, Action onLoaded, Action onUnloaded) : CommandItem`
  - `internal sealed class TickService : IDisposable`
    - `void Subscribe(object client, Action onTick)`
    - `void Unsubscribe(object client)`
    - `void TriggerNow()`
    - 動き：購読者が 1 人以上いるときだけ、次の JST の分境界に `System.Timers.Timer` を予約する（`AutoReset=false`）。`SystemEvents.TimeChanged` と、`PowerModeChanged`（Resume）のときは `TriggerNow` を呼ぶ。
    - 各購読者の `onTick` は個別に try/catch で囲み、例外は `Log.Error` に記録するだけにする。1 人の例外で、ほかの購読者やタイマーを止めない（spec §8）。
    - 購読者が 0 人になったら、`Log.Info("tick stopped")` を出す。
  - `internal static class AppPaths`：`Root`（`%LOCALAPPDATA%\StationCountdown`）、`StationsYaml`、`StationsBak`、`TimetablesDir`、`TimetablePath(string id)`、`HolidaysCsv`、`SchemasDir`、`LogsDir`。いずれも、必要なディレクトリを作成してからパスを返す。
  - `internal static class Log`
    - `Info(string)`、`Error(string, Exception?)`
    - 出力先は `logs/yyyyMMdd.log`。起動時に、7 日より古いファイルを削除する。
- 試作の段階では、`GetDockBands()` は固定の 1 バンドを返す。中身は `ListItem` 1 つで、Title は `試作 {H:mm}`、Subtitle は `更新 {n} 回目` にする。

- [ ] **Step 1:** テンプレートをコピーし、名前を置き換える。`grep -r "Template" src/StationCountdown.Extension` の結果が 0 件になることを確かめる。
- [ ] **Step 2:** OnLoad 方式・`TickService`・`AppPaths`・`Log` と、試作のバンドを実装する。
- [ ] **Step 3:** CI にジョブ `extension` を追加する（`windows-latest`、`dotnet build src/StationCountdown.Extension -c Release -p:Platform=x64`）。push して、ジョブが緑になることを確かめる。
- [ ] **Step 4:** コミットする（`feat(extension): scaffold extension with prototype dock band`）。
- [ ] **Step 5: 【人による確認・ゲート】** 開発者が Windows で MSIX をデプロイし、次の 4 項目を確かめて、結果を PR に記録する。
  - V1：8 時間以上放置しても、Title の時刻が最新のまま更新されているか
  - V2：画面ロック・解除の後も、表示と更新が続いているか
  - V3：スリープから復帰した後も、表示と更新が続いているか
  - V6：Dock の編集でピン留めを外すと、ログに `tick stopped` が出るか（`TickService` がログを出すようにしておく）

  V1〜V3 で止まる場合は、`Program.cs` で `CoAddRefServerProcess` を呼ぶ対策を入れてから確かめ直す。それでも駄目なら README に既知の制限として書き、次のタスクに進む。Task 13 以降は、このゲートの結果を待ってから始める。

---

### Task 13: StationStore・祝日サービス・本番のバンド（spec §6.1、§5.3、§5.6、§8）

**Files:**
- Create: `src/StationCountdown.Extension/Services/StationStore.cs`, `Services/HolidayService.cs`, `Dock/DepartureBand.cs`, `Assets/syukujitsu.csv`（内閣府の CSV をそのまま入れる。出典は Task 16 で書く）
- Modify: `StationCountdownCommandsProvider.cs`（試作のバンドを置き換える）, csproj（`Assets/syukujitsu.csv` と `schemas/*.json` を Content として追加する）

**Interfaces:**
- Consumes: `StationsState`、`StationsFile`、`TimetableFile`、`HolidayCalendar`、`NextDepartureCalculator`、`CountdownFormatter`、`TickService`、`OnLoadBandItem`、`AppPaths`、`Log`
- Produces:
  - `internal sealed class StationStore : IDisposable`
    - `StationsDocument Current`
    - `IReadOnlyList<ConfigError> Errors`
    - `Timetable? GetTimetable(string id)`
    - `IReadOnlyList<ConfigError> GetTimetableErrors(string id)`
    - `bool IsFetchFailed(string id)`
    - `void SetFetchFailed(string id, bool failed)`
    - `event Action? Changed`
    - `void Save(StationsDocument doc)`：`stations.yaml.bak` にコピーしてから書き込む。
    - `void Reload()`
    - `StationsYaml` と `TimetablesDir` を `FileSystemWatcher` で監視する。変更を検知したら 500ms 待ってから `Reload` する（デバウンス）。読み込みで `IOException` が出たら、200ms あけて 3 回まで再試行する。
    - 初めて起動したときに `SchemasDir` がなければ、`schemas/*.json` をコピーする。
  - `internal sealed class HolidayService`
    - `HolidayCalendar Calendar`
    - `Task EnsureCurrentAsync(HttpClient http, TimeProvider time)`：同梱データとキャッシュのうち、新しいほうを読み込む。今年の 12/31 を `Covers` していなければ、内閣府の URL から取得して `HolidaysCsv` に保存する。取得は 1 日に 1 回までとし、失敗したらログに記録するだけにする。
  - `internal sealed partial class DepartureBand : ListItem`
    - コンストラクタは `DepartureBand(string stationId, StationStore store, HolidayService holidays, TimeProvider time)`。
    - `void Refresh()`：`ComputeBand` → `FormatBand` の結果を `Title` と `Subtitle` に設定する。値が変わらなければ何もしない。
    - `Command` は `StationDeparturesPage`（Task 14）にする。それまでの間は `NoOpCommand` で、Id は `BandCommandId` にする。
    - アイコンは `new IconInfo("")`（Segoe Fluent Icons の Train）。
  - `CommandsProvider.GetDockBands()`：`store.Current.Stations` の各駅について、`OnLoadBandItem([band], station.BandCommandId, $"{name} {directionLabel}", onLoaded: () => tick.Subscribe(band, band.Refresh), onUnloaded: () => tick.Unsubscribe(band))` を返す。`store.Changed` のときは、バンドを作り直して `RaiseItemsChanged()` する。

- [ ] **Step 1:** 実装する。ロジックはすべて Core を呼び出すだけにし、新しく判断を加えない。
- [ ] **Step 2:** CI の `extension` ジョブが緑になることを確かめる。
- [ ] **Step 3:** 【手動】`stations.yaml` に `file` 型の駅を 1 つ書き、`timetables/{id}.yaml` に架空の時刻表を置く。Dock に、`{種別} {行先} あと{n}分` が表示され、毎分更新されることを確かめる。`stations.yaml` を手で書き換えると、数秒以内に反映されることも確かめる。
- [ ] **Step 4:** コミットする（`feat(extension): live departure bands from stations.yaml`）。

---

### Task 14: 駅一覧ページと発車一覧ページ（spec §5.5、§6.2）

**Files:**
- Create: `src/StationCountdown.Extension/Pages/StationsPage.cs`, `Pages/StationDeparturesPage.cs`
- Modify: `StationCountdownCommandsProvider.cs`（`TopLevelCommands` に `StationsPage` を入れる）, `Dock/DepartureBand.cs`（`Command` を差し替える）

**Interfaces:**
- Consumes: `StationStore`、`NextDepartureCalculator.ComputeList`、`CountdownFormatter.FormatListEntry`、`NextDaySeparatorTitle`
- Produces:
  - `internal sealed partial class StationDeparturesPage(string stationId, StationStore store, HolidayService holidays, TimeProvider time) : ListPage`
    - `Id` は `BandCommandId` と同じ値にする。
    - `GetItems()` では、`ComputeList(now, ctx, store.Current.Settings.DeparturesListLimit)` の結果を並べる。最初に `IsNextServiceDay` が true になる要素の前に、`new Separator(NextDaySeparatorTitle)` を入れる。`RemainMinutes == null` の行は、`Tags = [new Tag("間に合わない")]` を付けてアイコンを薄くする。
  - `internal sealed partial class StationsPage(StationStore store, …) : ListPage`
    - タイトルは `駅カウントダウン`。
    - `store.Errors` があれば、先頭に Title が `設定エラー（{line} 行目）`、Subtitle がエラーメッセージの行を出す。
    - 各駅の行は、Title が `{name} {directionLabel}`、Subtitle が `FormatBand` の Title。
    - 末尾に `＋ 阪急の駅を追加`、`＋ ファイルで駅を追加`、`設定フォルダを開く`（`OpenUrlCommand(AppPaths.Root)`）を置く。
    - 右クリックのメニューは Task 15 で追加する。

- [ ] **Step 1:** 実装する。
- [ ] **Step 2:** CI の `extension` ジョブが緑になることを確かめる。
- [ ] **Step 3:** 【手動】バンドをクリックすると一覧が開き、「間に合わない」と「翌日」の区切りが表示されることを確かめる。`stations.yaml` を壊すと、`StationsPage` の先頭にエラーの行が表示されることも確かめる。
- [ ] **Step 4:** コミットする（`feat(extension): stations and departures pages`）。

---

### Task 15: 追加・編集・削除のフォームと、定期的な再取得（spec §6.1、§6.2、§7.3、§8）

**Files:**
- Create: `src/StationCountdown.Extension/Pages/AddHankyuStationPage.cs`, `Pages/AddFileStationPage.cs`, `Pages/EditStationPage.cs`, `Services/RefreshScheduler.cs`
- Modify: `Pages/StationsPage.cs`（右クリックのメニュー）, `Dock/DepartureBand.cs`（`MoreCommands`）

**Interfaces:**
- Consumes: `HankyuImporter`、`ImporterHttp`、`TimetableWriter`、`RefreshPolicy`、`StationStore.Save`
- Produces:
  - `internal sealed class RefreshScheduler(StationStore store, IEnumerable<ITimetableImporter> importers, TimeProvider time) : IDisposable`
    - `Task RefreshNowAsync(string stationId)`
    - 起動したときと、それ以降は 5 分ごとに、`CanImport` が true の各駅について `RefreshPolicy.NextAttempt` を評価する。時刻が来ていれば取り込みを実行する。
      - 成功したら `TimetableWriter.WriteAtomic` で書き込み、`SetFetchFailed(id, false)` にする。
      - 失敗したら `SetFetchFailed(id, true)` にして、失敗回数と最後に失敗した時刻をメモリ上に記録する。
    - `HankyuImporter.RevisedTimetableDetected` を購読し、`Log.Info($"revised timetable available: {stationId}")` を記録する。
  - 各フォームは `ContentPage` で、`FormContent` を 1 つ持つ。`TemplateJson` には Adaptive Card 1.5 の JSON を使う。`SubmitForm(inputs)` では、`JsonDocument` で入力を読み、検証には `StationsFile` と同じルールを使う。
    - 検証にはヘルパー `public static IReadOnlyList<ConfigError> StationsFile.Validate(StationsDocument doc)` を使う。これは `Core` に追加する。
      - YAML のノードがないので、`ConfigError.Line` は 0 にする。
      - `Parse` の内部でも同じ検証を使い、検証ルールを 1 か所にまとめる。
      - テストは `StationsFileTests` に `Validate_matches_parse_rules` を足す。Task 3 の `[Theory]` の各入力を `StationsDocument` で組み立て、`Parse` でエラーになるものは `Validate` でもエラーになることを確かめる。
    - 検証エラーは `CommandResult.ShowToast` で表示し、ページは閉じない。
  - `AddHankyuStationPage` は 2 段階にする。
    1. URL の入力。`FetchStationPageAsync` で駅ページを取得し、取得できたら同じページの `FormContent` を、2 段目のフォームに差し替える。
    2. `Input.ChoiceSet`（`{Line} {Direction}`）、表示名、徒歩の分数（既定は 5）、ID（既定は `hankyu-{連番}`）を入力する。保存すると `RefreshNowAsync` を呼ぶ。
  - `AddFileStationPage`：保存すると、Toast で `時刻表ファイルを {TimetablePath(id)} に置いてください` と知らせ、`TimetablesDir` を開く。
  - `EditStationPage`：表示名・方面・徒歩の分数を変更する。
  - 削除は、`ConfirmationArgs` で `「{name} {directionLabel}」を削除しますか？` と確認する。`IsPrimaryCommandCritical = true` にし、確定すると `Save` を呼ぶ。時刻表ファイルは残す。
  - フォームの上部には、`TextBlock` で `保存すると stations.yaml 内のコメントは消えます（直前の内容は stations.yaml.bak に保存されます）` という注意書きを出す。
  - バンドの `MoreCommands`：「時刻表を再取得」（`hankyu` 型の駅だけ）、「駅を編集」、「設定フォルダを開く」。

- [ ] **Step 1:** `StationsFile.Validate` を追加し、そのテストを Core に書く。テストが通ることを確かめる。
- [ ] **Step 2:** フォームと `RefreshScheduler` を実装する。
- [ ] **Step 3:** CI の `extension` ジョブが緑になることを確かめる。
- [ ] **Step 4:** 【手動】次の 4 点を確かめる。
  - フォームから大阪梅田（宝塚線・宝塚方面）を追加すると、時刻表ファイルが作られ、バンドに表示が出ること。
  - ネットワークを切った状態で「時刻表を再取得」すると、Subtitle に `取得失敗` と付くこと。
  - 編集・削除が `stations.yaml` に反映されること。
  - `stations.yaml.bak` が作られること。
- [ ] **Step 5:** コミットする（`feat(extension): station forms and scheduled refresh`）。

---

### Task 16: 一般公開に向けたビルドの切り替え、表示義務、仕上げ（spec §3、§11）

**Files:**
- Modify: `src/StationCountdown.Extension/StationCountdown.Extension.csproj`, `StationCountdownCommandsProvider.cs`, `README.md`, `.github/workflows/ci.yml`

**Interfaces:**
- Produces:
  - MSBuild のプロパティ `IncludeImporters`（既定は `true`）を追加する。
    - `false` のときは、`Importers` への `ProjectReference` を外し、`DefineConstants` に `NO_IMPORTERS` を加える。
    - `NO_IMPORTERS` のときは、阪急の追加フォーム・再取得・`RefreshScheduler` をコンパイルから外す。`hankyu` 型の駅は `時刻表未取得` と表示する。

- [ ] **Step 1:** 実装する。CI の `extension` ジョブに、`-p:IncludeImporters=false` でビルドするステップも追加する。両方のビルドが緑になることを確かめる。
- [ ] **Step 2:** README に次の内容を追記する。
  - 使い方：インストール、駅の追加、`stations.yaml` と時刻表 YAML の例（架空のデータ）
  - 祝日データの出典：「出典：内閣府『国民の祝日について』（公共データ利用規約 第1.0版）」
  - 時刻表の扱い：「時刻表の著作権は各事業者に帰属します。取得したデータは私的利用の範囲でお使いください」
  - PowerToys 由来のコードの MIT 著作権表示
  - 既知の制限（Task 12 のゲートの結果）
- [ ] **Step 3:** すべてのテストと CI が緑になることを確かめる。
- [ ] **Step 4:** コミットする（`feat: importer-free build switch and README`）。push して、draft PR の本文に V1〜V6 の結果をまとめる。
