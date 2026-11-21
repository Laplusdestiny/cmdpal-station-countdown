# 表示する種別の選択（#13）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 駅ごとに `trainTypes`（許可リスト）を設定でき、選んだ種別の電車だけをバンドと発車一覧に出す。種別は「駅を編集」フォームのチェックボックスで選べる。

**Architecture:** 絞り込みは Core の `NextDepartureCalculator` が営業日のダイヤを取り出すところで行い、バンド・一覧が同じルールになる。設定は `StationConfig.TrainTypes` に持ち、`stations.yaml` で読み書きする。フォームの候補作りと選択値の解釈も Core（`TrainTypes`）に置いてテストする。Extension はそれを呼ぶだけ。

**Tech Stack:** C# / .NET 10、xUnit v3（Microsoft.Testing.Platform）、YamlDotNet、Command Palette Toolkit（Adaptive Cards 1.5）

**Spec:** `docs/superpowers/specs/2026-11-19-train-type-filter-design.md`

## Global Constraints

- 許可リスト方式。`trainTypes` が空（省略）なら絞り込まない。空でなければ、書いた種別と**完全一致**（`StringComparison.Ordinal`）する電車だけ表示する。
- 時刻表ファイル・`Timetable` のデータは変えない。絞り込みは表示側（計算）だけ。
- `trainTypes` の要素: 空・空白（全角含む `char.IsWhiteSpace`）を含む・`,` を含む・重複はエラー。
- 文言（そのまま使う）:
  - バンド: Title `表示する種別の電車なし`、Subtitle `{name} {directionLabel}`
  - 一覧のメッセージ: Title `表示する種別の電車がありません`、Subtitle `「駅を編集」で表示する種別を見直してください。`
  - 編集フォームのラベル: `表示する種別`、説明 `何も選ばないと、すべての種別を表示します。`
  - 時刻表がないとき: `時刻表を取得すると、表示する種別を選べます。`
  - 時刻表にない選択済みの種別: `{種別}（時刻表になし）`
  - 追加後のトーストに足す文: `表示する種別は「駅を編集」で選べます。`
- コメント・ドキュメントは日本語、既存のコードの書き方に合わせる。
- テストの実行: `dotnet test --project tests/StationCountdown.Core.Tests`。この環境に .NET SDK がなければ、最初に `dotnet-install.sh --channel 10.0` で `~/.dotnet` に入れ、`PATH` に足す（リポジトリには何も足さない）。Extension は Windows 専用なので、ここではビルドできない。

## Review Focus

1. **既存の `stations.yaml`（`trainTypes` なし）** → 表示は今と同じ。→ Task 1・2 の既存テストがそのまま通ることで確認。Task 1 に「省略時は空」のテストを足す。
2. **名前などを編集フォームで保存したとき** → `trainTypes` が消えない（時刻表がまだない駅でも）。→ Task 4 の `TrainTypes.ApplySelection` のテスト。
3. **再取得で選んだ種別が時刻表から消えた** → バンドは `表示する種別の電車なし`、フォームには `（時刻表になし）` 付きで残る。→ Task 2・3 のテスト。
4. **「次」の電車（Subtitle）と本日終了の始発** → 絞り込んだあとの電車になる。→ Task 2 のテスト。
5. **すべてのチェックを外して保存** → `trainTypes` が消え、全種別表示に戻る。→ Task 3 の `ParseSelection("")` のテスト。

---

### Task 1: `trainTypes` の設定（モデル・YAML・検証・スキーマ）

**Files:**
- Modify: `src/StationCountdown.Core/Model/StationConfig.cs`
- Modify: `src/StationCountdown.Core/Config/StationRules.cs`
- Modify: `src/StationCountdown.Core/Config/StationsFile.cs`（`ParseStations`・`Validate`・`Serialize`）
- Modify: `schemas/stations.schema.json`
- Test: `tests/StationCountdown.Core.Tests/StationsFileTests.cs`、`tests/StationCountdown.Core.Tests/StationsValidateTests.cs`

**Interfaces:**
- Produces:
  - `StationConfig.TrainTypes { get; init; }` : `IReadOnlyList<string>`、既定値 `[]`（位置パラメーターには足さない。既存の `new StationConfig(...)` はそのまま）。
  - `StationConfig` は `Equals(StationConfig?)` / `GetHashCode()` を自前で実装し、`TrainTypes` を `SequenceEqual` で比べる（既存の `Assert.Equal(expected.Stations, actual.Stations)` が値で比べられるように）。
  - `internal static string? StationRules.CheckTrainType(string value)`（1 要素の検証。空/空白を含む/`,` を含む）
  - 重複のエラー文: `trainTypes に同じ種別が複数あります: {種別}`

- [ ] **Step 1: 失敗するテストを書く**

`StationsFileTests`:
- `Parse_trainTypes_defaults_to_empty`: `Valid` を読むと両方の駅の `TrainTypes` が空。
- `Parse_reads_trainTypes`: `Valid` の `"    walkMinutes: 8\n"` を `"    walkMinutes: 8\n    trainTypes: [普通, 準急]\n"` に置き換えて読むと `Stations[0].TrainTypes` が `["普通", "準急"]`。
- `Parse_rejects_invalid_with_line_number` に `InlineData` を追加（いずれも 10 行目の後ろに `trainTypes` 行を挿入し、その行 = 11 行目のエラー）: `[普通, 普通]`（重複）、`["普 通"]`（空白）、`["普通,準急"]`（カンマ）、`[""]`（空）、`普通`（リストでない）。
- `Serialize_then_Parse_roundtrips_trainTypes`: `Stations[0]` に `TrainTypes = ["普通", "準急"]` を設定した文書が、Serialize → Parse で同じ文書になる（`AssertSameDocument`）。
- `Serialize_omits_empty_trainTypes`: `Valid` を Serialize した YAML に `trainTypes` が含まれない。

`StationsValidateTests.InvalidDocuments` に追加（`serializable: true`）: `trainTypes の重複`・`trainTypes に空白`・`trainTypes にカンマ`・`trainTypes が空文字`。

- [ ] **Step 2: テストを実行し、失敗を確認する**

Run: `dotnet test --project tests/StationCountdown.Core.Tests`
Expected: FAIL（`TrainTypes` が定義されていないためのコンパイルエラー）

- [ ] **Step 3: 実装する**

- `ParseStations`: `RejectUnknownKeys` に `"trainTypes"` を足す。`r.GetSequence(map, "trainTypes", required: false)` と `r.ScalarItems(seq, "trainTypes")` で読み、要素ごとに `r.Check(node, StationRules.CheckTrainType(...))`、重複はその要素のノードでエラー。
- `Validate`: 同じルールを行番号 0 で検査する。
- `Serialize`: `TrainTypes.Count > 0` のときだけ、`walkMinutes` の後ろ・`source` の前に `trainTypes` をフロー形式（`[普通, 準急]`、`SequenceStyle.Flow`）で書く。
- スキーマの `station.properties` に追加: `"trainTypes": { "description": "表示する種別（ここに書いた種別だけ表示する。省略するとすべて表示）", "type": "array", "uniqueItems": true, "items": { "type": "string", "pattern": "^[^\\s,]+$" } }`

- [ ] **Step 4: テストを実行し、通ることを確認する**

Run: `dotnet test --project tests/StationCountdown.Core.Tests`
Expected: PASS（既存のテストも含めてすべて）

- [ ] **Step 5: コミット**

```bash
git add src/StationCountdown.Core schemas tests/StationCountdown.Core.Tests
git commit -m "feat(core): add trainTypes to station config (#13)"
```

---

### Task 2: 計算での絞り込みと「表示する種別の電車なし」

**Files:**
- Modify: `src/StationCountdown.Core/Countdown/NextDepartureCalculator.cs`
- Modify: `src/StationCountdown.Core/Countdown/BandState.cs`
- Modify: `src/StationCountdown.Core/Countdown/CountdownFormatter.cs`
- Test: `tests/StationCountdown.Core.Tests/NextDepartureCalculatorTests.cs`、`tests/StationCountdown.Core.Tests/CountdownFormatterTests.cs`

**Interfaces:**
- Consumes: `StationConfig.TrainTypes`（Task 1）
- Produces:
  - `BandState.NoMatchingTrains`（引数なしの sealed record）
  - `public static bool NextDepartureCalculator.IsListHiddenByTrainTypes(DateTimeOffset now, CalcContext ctx, int limit)`: 絞り込んだ一覧が空で、絞り込まない一覧が空でないとき true。

`ComputeBand` の判定順（設計書の「今日・翌営業日ともにない」を、本日分が終わったあとにも当てはめたもの）:

1. 今日の（絞り込む前の）ダイヤがない → `NoTimetable`（今と同じ）
2. 絞り込んだ今日の電車に間に合うものがある → `Upcoming`（`Following` も絞り込んだ列から）
3. 絞り込んだ翌営業日の電車がある → `EndOfService`（その始発）
4. 翌営業日の（絞り込む前の）電車がある、または今日の（絞り込む前の）電車があるのに絞り込むと 0 本 → `NoMatchingTrains`
5. それ以外 → `NoTimetable`

`ComputeList` は、今日・翌営業日とも絞り込んだ列を使う。

- [ ] **Step 1: 失敗するテストを書く**

`NextDepartureCalculatorTests`（既存の `Table`・`Ctx`・`At` を使う。`Filtered(params string[] types)` = `Station with { TrainTypes = types }`）:
- `Band_filter_skips_other_types`: `At(10, 12, 19)`、`TrainTypes = ["普通"]` → `Next` は `12:40 普通 A`、`Following` は `24:10 普通 A`。
- `Band_filter_applies_to_following`: `At(10, 4, 0)`、`["普通"]` → `Next` `05:00 普通 A`、`Following` `12:40 普通 A`（`12:30 急行 B` を飛ばす）。
- `Band_filter_end_of_service_uses_filtered_first_train`: weekday が `05:00 急行 B` `05:10 普通 A` `12:00 普通 A` のテーブル、`At(10, 13, 0)`、`["普通"]` → `EndOfService` の始発は `05:10 普通 A`。
- `Band_no_matching_trains_when_type_missing`: `["特急"]` → `NoMatchingTrains`。
- `Band_no_matching_trains_after_today_ends`: `At(10, 13, 0)`、`["急行"]`、翌日（11 日、水）の weekday にも急行は 12:30 にあるので `EndOfService(12:30 急行 B)` になることを確認。次に、翌日が土休日（`At(13, 13, 0)`、13 日は金、14 日は土）で `["急行"]` → 土休日に急行がないので `NoMatchingTrains`。
- `Band_empty_filter_is_unchanged`: `TrainTypes = []` で、既存の `Band_skips_uncatchable` と同じ結果。
- `List_filter_skips_other_types`: `At(10, 12, 0)`、`["急行"]`、limit 10 → 1 件目は `12:30 急行 B`、普通は含まれない。
- `List_hidden_by_train_types`: `["特急"]` → `ComputeList` が空、`IsListHiddenByTrainTypes` が true。`[]` → false。時刻表なし（`Ctx(table: 空の Services)`）で `["特急"]` → false。

`CountdownFormatterTests`:
- `Band_no_matching_trains`: `FormatBand(new BandState.NoMatchingTrains(), Station, false)` → `new BandText("表示する種別の電車なし", "{Station.Name} {Station.DirectionLabel}")`。`fetchFailed: true` なら Subtitle の末尾に ` ／ 取得失敗`。

- [ ] **Step 2: テストを実行し、失敗を確認する**

Run: `dotnet test --project tests/StationCountdown.Core.Tests`
Expected: FAIL（`NoMatchingTrains`・`IsListHiddenByTrainTypes` がない）

- [ ] **Step 3: 実装する**

`GetDepartures` は今のまま（絞り込む前）にして、`Filter(IReadOnlyList<Departure>, StationConfig)` を足し、上の判定順で使う。`TrainTypes` が空ならリストをそのまま返す。

- [ ] **Step 4: テストを実行し、通ることを確認する**

Run: `dotnet test --project tests/StationCountdown.Core.Tests`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/StationCountdown.Core tests/StationCountdown.Core.Tests
git commit -m "feat(core): filter departures by selected train types (#13)"
```

---

### Task 3: フォーム用の種別の候補と選択値（Core）

**Files:**
- Create: `src/StationCountdown.Core/Timetables/TrainTypes.cs`
- Test: `tests/StationCountdown.Core.Tests/TrainTypesTests.cs`

**Interfaces:**
- Consumes: `Timetable`、`DayType`、`StationConfig.TrainTypes`
- Produces（`public static class TrainTypes`、名前空間 `StationCountdown.Core.Timetables`）:
  - `public sealed record TrainTypeChoice(string Value, bool InTimetable)`
  - `IReadOnlyList<TrainTypeChoice> Choices(Timetable timetable, IReadOnlyList<string> selected)`: 時刻表の種別を、`DayType` の列挙順（Weekday → Saturday → Holiday → SaturdayHoliday）・各ダイヤの時刻順で最初に現れた順に並べ、`,` を含む種別は除く。その後ろに、`selected` のうち時刻表にないものを `InTimetable = false` で選択順に足す。
  - `IReadOnlyList<string> ParseSelection(string value)`: Adaptive Card の複数選択の値（`"普通,準急"`）を分けて、空要素を除いて返す。`""` → 空。
  - `IReadOnlyList<string> ApplySelection(IReadOnlyList<string> current, string? submitted)`: `submitted` が null（フォームに種別の欄がなかった）なら `current` をそのまま返す。それ以外は `ParseSelection(submitted)`。

- [ ] **Step 1: 失敗するテストを書く**

- `Choices_in_order_of_first_appearance`: weekday `05:00 普通 A` `05:10 急行 B`、saturdayHoliday `05:00 準急 A` `06:00 普通 A` → `普通, 急行, 準急`（すべて `InTimetable = true`）。
- `Choices_keep_selected_missing_types`: 上と同じ時刻表、selected `["特急", "普通"]` → `普通, 急行, 準急` の後ろに `TrainTypeChoice("特急", false)`。
- `Choices_skip_types_with_comma`: 種別 `普通,臨時` は候補に出ない。
- `ParseSelection` の `[Theory]`: `""` → `[]`、`"普通"` → `["普通"]`、`"普通,準急"` → `["普通", "準急"]`、`"普通,,準急"` → `["普通", "準急"]`。
- `ApplySelection_null_keeps_current`: `ApplySelection(["普通"], null)` → `["普通"]`。
- `ApplySelection_empty_clears`: `ApplySelection(["普通"], "")` → `[]`。

- [ ] **Step 2: テストを実行し、失敗を確認する**

Run: `dotnet test --project tests/StationCountdown.Core.Tests`
Expected: FAIL（`TrainTypes` クラスがない）

- [ ] **Step 3: `TrainTypes.cs` を実装する**

- [ ] **Step 4: テストを実行し、通ることを確認する**

Run: `dotnet test --project tests/StationCountdown.Core.Tests`
Expected: PASS

- [ ] **Step 5: コミット**

```bash
git add src/StationCountdown.Core/Timetables/TrainTypes.cs tests/StationCountdown.Core.Tests/TrainTypesTests.cs
git commit -m "feat(core): build train type choices for the edit form (#13)"
```

---

### Task 4: Extension（編集フォーム・一覧ページ・トースト）とドキュメント

Extension は Windows 専用でこの環境ではビルドできない。Core の API だけを呼ぶ薄い変更にし、Windows での確認は利用者に頼む（最後に確認項目を伝える）。

**Files:**
- Modify: `src/StationCountdown.Extension/Pages/Forms/Cards.cs`
- Modify: `src/StationCountdown.Extension/Pages/Forms/EditStationPage.cs`
- Modify: `src/StationCountdown.Extension/Pages/Forms/FormInputs.cs`（`TextOrNull` を足す）
- Modify: `src/StationCountdown.Extension/Pages/StationDeparturesPage.cs`
- Modify: `src/StationCountdown.Extension/Pages/Forms/AddHankyuStationPage.cs`（取得成功のトースト）
- Modify: `src/StationCountdown.Extension/Pages/Forms/AddFileStationPage.cs`（保存後のトースト）
- Modify: `README.md`、`docs/superpowers/specs/2026-11-09-station-countdown-design.md`（§4.2・§6.1 の表・§6.2 の表）

**Interfaces:**
- Consumes: `TrainTypes.Choices` / `ApplySelection`（Task 3）、`BandState.NoMatchingTrains`・`NextDepartureCalculator.IsListHiddenByTrainTypes`（Task 2）、`StationConfig.TrainTypes`（Task 1）
- Produces: `Cards.MultiChoiceSet(string id, string label, IEnumerable<(string Title, string Value)> choices, IEnumerable<string> values)`: `Input.ChoiceSet`、`"isMultiSelect": true`、`"style": "expanded"`、`"isRequired": false`、`"value"` は values をカンマでつないだ文字列。

- [ ] **Step 1: `Cards.MultiChoiceSet` を足す**（値は既存と同じく `Str` でエスケープ）

- [ ] **Step 2: `EditStationForm` に種別の欄を足す**

- `Reset`: `store.GetTimetable(stationId)` があれば、`walkMinutes` の後ろに `Cards.Text("何も選ばないと、すべての種別を表示します。", subtle: true)` と `Cards.MultiChoiceSet("trainTypes", "表示する種別", …, s.TrainTypes)` を置く。候補の Title は `InTimetable` なら種別そのまま、そうでなければ `{種別}（時刻表になし）`。時刻表がなければ `Cards.Text("時刻表を取得すると、表示する種別を選べます。", subtle: true)` だけ置く。
- `SubmitForm`: `FormInputs` にキー `trainTypes` があるかを見て（`FormInputs` に `string? TextOrNull(string id)` を足す）、`TrainTypes.ApplySelection(current.TrainTypes, …)` の結果を `edited with { TrainTypes = … }` に入れる。検証は今と同じ `StationsFile.Validate`。

- [ ] **Step 3: `StationDeparturesPage` のメッセージを分ける**

`entries.Count == 0` のとき、時刻表エラーの次に `NextDepartureCalculator.IsListHiddenByTrainTypes(now, ctx, limit)` を見て、true なら `Message("表示する種別の電車がありません", "「駅を編集」で表示する種別を見直してください。")`。

- [ ] **Step 4: トーストに案内を足す**

`AddHankyuStationPage` の取得成功のメッセージと、`AddFileStationPage` の保存後のメッセージの末尾に `表示する種別は「駅を編集」で選べます。` を足す。

- [ ] **Step 5: ドキュメントを更新する**

- README の `stations.yaml` の例の `kakuu-kita` に `trainTypes: [普通, 準急]   # 書いた種別だけ表示する（省略するとすべて表示）` を足し、「使い方」の右クリックの説明の後ろに「『駅を編集』で、表示する種別を選べます（何も選ばないとすべて表示）」を足す。
- 元の設計書の §4.2 に `trainTypes` の説明、§6.1 の表に `表示する種別の電車なし` の行、§6.2 の `EditStationForm` の行に「表示する種別（#13）」を足す。

- [ ] **Step 6: Core のテストがすべて通ることを確認する**

Run: `dotnet test --project tests/StationCountdown.Core.Tests`
Expected: PASS

- [ ] **Step 7: コミット**

```bash
git add src/StationCountdown.Extension README.md docs/superpowers/specs/2026-11-09-station-countdown-design.md
git commit -m "feat(extension): choose displayed train types in the edit form (#13)"
```

**Windows での確認項目（利用者に伝える）:**
1. 「駅を編集」に種別のチェックボックスが出て、選んで保存すると `stations.yaml` に `trainTypes` が書かれる
2. Dock のバンドと発車一覧に、選んだ種別だけが出る
3. 全部外して保存すると `trainTypes` が消え、全種別が出る
4. `trainTypes` に時刻表にない種別だけを書くと、バンドが `表示する種別の電車なし` になる
