# Stoneshard Save Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A double-click Windows `.exe` that opens the owner's Stoneshard saves in a game-styled window and edits inventory, gold, stats, skills and reputation, writing saves the game accepts.

**Architecture:** All save logic lives in a tested .NET library (`Core`). Game-file readers (`GameData`) extract icons, item tables and names from the owner's install at runtime. A Razor class library (`UI`) holds the screens and an in-process `EditorService`; a WinForms window (`App`) shows them through Blazor Hybrid (`BlazorWebView`). There is no web server and no JavaScript build.

**Tech Stack:** .NET 10 (C#), Blazor Hybrid (Razor components in WinForms `BlazorWebView`, WebView2), SharpZipLib (BZip2), xunit.v3, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-22-stoneshard-save-editor-design.md`. Read it first. Section numbers below (§) refer to it.

## Global Constraints

- **Platform:** Windows 11 x64.
- **Target frameworks:** `net10.0` for libraries and tests, `net10.0-windows` for `App`. Nullable enabled, `TreatWarningsAsErrors` true. SDK pinned by `global.json` to `10.0.401` with `rollForward: latestFeature`.
- **NuGet packages allowed:** `SharpZipLib`, `Microsoft.AspNetCore.Components.WebView.WindowsForms`, `bunit`, `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`.
- Any other dependency needs a one-line reason in its commit message.
- **Never write to the owner's real characters** (`%LOCALAPPDATA%\StoneShard\characters_v1\character_1` and `character_2`) from tests, the Lab tool or dev runs. Tests use temp copies only (`Fixtures.CopyRealSavesToTemp()`). Writes into the live saves folder go only to test characters made by the Lab tool (§10). The one exception is `characters.map`, which the Lab tool's `point-continue` command may rewrite after backing it up.
- **Game files are read-only.** Never modify `data.win` or `StoneShard.exe`. Never commit anything extracted from them (PNGs, table dumps). The only exceptions are the two hand-made data files `tree-unlocks.json` and `skill-trees.json`, which hold skill object names and positions only.
- **App data root:** `%LOCALAPPDATA%\StoneShardSaveEditor\`, with `backups\`, `cache\`, `webview2\` and `settings.json` inside.
- **Supported save version:** `wipeVersion` `"0.9"`. Any other version opens read-only.
- **Writing numbers:** new values are formatted with `value.ToString("0.0###############", CultureInfo.InvariantCulture)`, so `2864 → "2864.0"`. Untouched numbers keep their original text.
- **Writing strings:** new strings escape `\` → `\\`, `"` → `\"`, `/` → `\/`, and anything below 0x20 or above 0x7E → `\uXXXX`.
- **Real fixtures:** a snapshot of the owner's saves taken 2026-10-22 lives at `tests/fixtures/real/characters_v1/`. It is git-ignored; never re-copy it. Velmir's `data.sav` stored hash there is `1286653c2cd1cbdeee110fd96dff7970`. Tests that need it, or need the game install (env `STONESHARD_DIR`, default `D:\SteamLibrary\steamapps\common\Stoneshard`), call `Assert.Skip` when it is missing.
- **UI copy, exact:** product name "Stoneshard Save Editor". Buttons: "Save to game", "Undo all", "Backups", "+ Add item", "Duplicate", "Delete", "Respec all", "Refund this tree", "Unlock tree". Toggle: "Follow game rules".
- **Style tokens (from the approved mockups):**

  | Token | Value |
  |---|---|
  | bg | `#1b1815` |
  | panel | `#24201b` |
  | border | `#4a3f31` |
  | grid line | `#3a3229` |
  | text | `#e8dcc4` |
  | text-dim | `#cdbd9b` |
  | muted | `#a8987a` |
  | label | `#b9a67f` |
  | accent | `#e2b24a` |
  | button | `#3a3127` / border `#6b5a3f` |
  | gold button | `#6b5122` / border `#c9a14a` |
  | danger | `#5a2620` / border `#a5463b` |
  | durability | `#7fb04f` |
  | available | `#7a8f5a` |

  Font: Georgia, serif. Icons use `image-rendering: pixelated` at exactly 2× scale, so a bag cell is 54 px. Dark theme only.
- **Git:** all work on branch `feature/save-editor`. No push until the owner has tested (owner's standing rule).

## Review Focus

Failure modes the spec implies but no feature test exercises directly, most likely first. Each has a test added in the task that owns the code.

1. **The slot changed on disk after it was opened** (the game autosaved or exited meanwhile). Save refuses with "changedOnDisk" and offers a reload; it never overwrites. Tests: Task 4 `Save_refused_when_file_changed_on_disk`, Task 14 `Save_reports_changedOnDisk`.
2. **Stoneshard is running.** Save is blocked and the UI says why. Tests: Task 4 `Save_refused_while_game_running`, Task 14 `Save_reports_gameRunning`, Task 15 `Editor_shows_game_running_banner_and_disables_save`.
3. **Damaged or unsupported slot** (bad zlib, hash mismatch, `wipeVersion` ≠ 0.9). The picker still lists it with a warning, it opens read-only, and it is never written. Tests: Task 3 `Damaged_slot_is_reported_not_thrown`, `Hash_mismatch_is_damaged`, `Unsupported_version_is_flagged`; Task 4 `ReadOnly_session_never_writes`.
4. **Bag full on Add or Duplicate.** A clear error ("No room in the bag"), and the save is unchanged. Tests: Task 9 `Bag_full_rejects_duplicate_and_add`, Task 14 `Edit_rejections_carry_the_message`.
5. **Invalid numbers** (empty, letters, negative, fractional for whole-number fields, NaN/∞, out of range). Rejected with a message, nothing changed. Tests: Task 8 `Set_rejects_invalid`, Task 9 `Inventory_rejects_invalid_numbers`, Task 15 `NumberField_rejects_non_numeric_input`.

---

## File Structure

```
stoneshard-save-editor/
  StoneshardSaveEditor.sln   global.json   Directory.Build.props   .gitignore
  src/
    StoneshardSaveEditor.Core/        save format + editing (no game-file access)
      Json/      GmNode.cs  GmJson.cs  GmDiff.cs
      Files/     SaveCodec.cs  Salt.cs  SaveLocator.cs  GameDate.cs  SaveSession.cs
                 BackupService.cs  GameProcessProbe.cs  TestCharacterCloner.cs
      Editing/   EditRejectedException.cs  CharacterEditor.cs  ReputationEditor.cs
                 InventoryEditor.cs  BagGrid.cs  SkillsEditor.cs  SkillRules.cs
    StoneshardSaveEditor.GameData/    reads data.win + StoneShard.exe
      GameInstall.cs
      DataWin/   DataWinFile.cs  SpriteTable.cs  ObjectTable.cs  TexturePages.cs  GmQoi.cs
      Imaging/   Rgba32Image.cs  Png.cs
      Icons/     IconService.cs
      Tables/    ExeTables.cs  GameTable.cs  Localization.cs
      Catalog/   ItemCatalog.cs  ItemPropsBuilder.cs  GameItemShapes.cs
      Skills/    SkillCatalog.cs  tree-unlocks.json  skill-trees.json   (both embedded resources)
    StoneshardSaveEditor.UI/          Razor class library: screens + EditorService (in-process)
      Services/  EditorService.cs  GameDataProvider.cs  Settings.cs  Views.cs  Names.cs  IAssetUrls.cs
      App.razor  Screens/ SavePicker.razor  Editor.razor
      Tabs/      CharacterTab.razor  InventoryTab.razor  SkillsTab.razor  ReputationTab.razor
      Components/ NumberField.razor  Icon.razor  Banner.razor  BackupsDialog.razor  CatalogDialog.razor
      wwwroot/app.css
    StoneshardSaveEditor.App/         WinForms window hosting the UI in BlazorWebView
      Program.cs  MainForm.cs  WebViewAssets.cs  wwwroot/index.html
  tools/StoneshardSaveEditor.Lab/      console tool for test characters and experiments
      Program.cs  Experiments.cs
  tests/
    StoneshardSaveEditor.Core.Tests/      (TestSupport/Fixtures.cs shared by link)
    StoneshardSaveEditor.GameData.Tests/
    StoneshardSaveEditor.Lab.Tests/
    StoneshardSaveEditor.UI.Tests/       (bUnit)
    fixtures/real/characters_v1/          git-ignored snapshot (already present)
  docs/research/in-game-findings.md       results of the owner's in-game checks
```

---

## Phase 1: Save foundation

### Task 1: Solution scaffold and GameMaker JSON (byte-exact round trip)

**Files:**
- Create:
  - `global.json`, `Directory.Build.props`, `StoneshardSaveEditor.sln`
  - `src/StoneshardSaveEditor.Core/StoneshardSaveEditor.Core.csproj`
  - `src/StoneshardSaveEditor.Core/Json/GmNode.cs`, `src/StoneshardSaveEditor.Core/Json/GmJson.cs`
  - `tests/StoneshardSaveEditor.Core.Tests/StoneshardSaveEditor.Core.Tests.csproj`
  - `tests/StoneshardSaveEditor.Core.Tests/TestSupport/Fixtures.cs`
  - `tests/StoneshardSaveEditor.Core.Tests/Json/GmJsonTests.cs`
- Modify: `.gitignore` (append `bin/`, `obj/`, `node_modules/`, `web/dist/`, `artifacts/`, `.superpowers/`)

**Interfaces:**
- Produces (namespace `StoneshardSaveEditor.Core.Json`):
  - `abstract class GmNode` with subclasses:
    - `GmObject`: ordered entries, keys kept as raw token text.
      - `IReadOnlyList<KeyValuePair<string, GmNode>> Entries`
      - `GmNode? this[string key]`
      - `void Set(string key, GmNode value)` replaces in place, appends if missing.
      - `bool Remove(string key)`
    - `GmArray`: `List<GmNode> Items`.
    - `GmString`: `string Raw` (escaped content, no quotes), `string Value` (decoded), `static GmString From(string value)`.
    - `GmNumber`: `string Raw`, `double Value`, `static GmNumber From(double value)`.
    - `GmBool`: `bool Value`.
    - `GmNull`.
  - Extensions on `GmNode`:
    - `bool IsTruthy()`: `true`, or a number ≠ 0.
    - `double AsDouble()`: bool → 1/0.
    - `GmNode DeepClone()`.
  - `static class GmJson`: `GmNode Parse(ReadOnlySpan<byte> utf8)`, `byte[] Write(GmNode node)`.
- Produces (tests, namespace `StoneshardSaveEditor.Tests`), `static class Fixtures`:
  - `string RepoRoot`
  - `string RealSavesRoot`: skips if missing.
  - `string GameDir`: skips if `data.win` is missing.
  - `string CopyRealSavesToTemp()`: returns the temp `characters_v1` path.
  - `byte[] DecompressedJson(string file)`: zlib-inflates and strips the 33-byte trailer.
  - Later test projects include this file via `<Compile Include="..\StoneshardSaveEditor.Core.Tests\TestSupport\Fixtures.cs" Link="TestSupport\Fixtures.cs" />`.

- [ ] **Step 1: Create branch `feature/save-editor`.** Scaffold the solution with `dotnet new sln`, the classlib, and an xunit.v3 test project referencing Core. Add `global.json` and `Directory.Build.props` (Nullable, TreatWarningsAsErrors, LangVersion latest).

- [ ] **Step 2: Write the failing tests**

```csharp
[Fact] public void Parse_then_Write_reproduces_gamemaker_text_exactly() {
    const string text = "{ \"a\": 1.0, \"b\": [ ], \"c\": { }, \"d\": [ \"x\\/y\", -4.0, true, false, null ], \"e\": \"q\\\"\\\\\" }";
    Assert.Equal(text, Encoding.UTF8.GetString(GmJson.Write(GmJson.Parse(Encoding.UTF8.GetBytes(text)))));
}
[Fact] public void Every_real_save_file_round_trips_byte_for_byte() {
    // every data.sav, save.map, character.map, characters.map under Fixtures.RealSavesRoot
    foreach (var f in files) { var json = Fixtures.DecompressedJson(f); Assert.Equal(json, GmJson.Write(GmJson.Parse(json))); }
}
[Fact] public void Long_number_text_is_preserved() {
    var root = (GmObject)GmJson.Parse("{ \"t\": 46316.836047453703940846025943756 }"u8);
    var n = (GmNumber)root["t"]!;
    Assert.Equal("46316.836047453703940846025943756", n.Raw); Assert.Equal(46316.836, n.Value, 3);
}
[Theory, InlineData(2864, "2864.0"), InlineData(-4, "-4.0"), InlineData(0.5, "0.5"), InlineData(0, "0.0")]
public void GmNumber_From_formats_like_gamemaker(double v, string raw) => Assert.Equal(raw, GmNumber.From(v).Raw);
[Fact] public void GmString_From_escapes_quote_backslash_slash() => Assert.Equal("a\\/\\\"\\\\", GmString.From("a/\"\\").Raw);
[Fact] public void GmObject_Set_replaces_in_place_and_appends_new_keys() { /* {a,b} Set(a)→ order a,b; Set(c) → a,b,c */ }
[Fact] public void IsTruthy_handles_bool_and_number() { /* true,1.0 → true; false,0.0 → false */ }
```

Keys are looked up by raw key text without quotes (`obj["t"]`). These saves have no escaped keys.

- [ ] **Step 3: Run** `dotnet test tests/StoneshardSaveEditor.Core.Tests`. Expected: FAIL to compile (GmJson missing).

- [ ] **Step 4: Implement `GmNode` types and `GmJson`.**
  - **Reader:** a hand-written UTF-8 scanner. Strings keep their raw escaped bytes and decode lazily. Numbers keep their raw text.
  - **Writer:** emits the exact GameMaker layout (§6.2, verified):
    - object: `{ ` + `"k": v` joined by `, ` + ` }`, and `{ }` when empty;
    - array: `[ ` + items joined by `, ` + ` ]`, and `[ ]` when empty;
    - no newlines.

- [ ] **Step 5: Run tests.** Expected: all PASS, including the real round trip (about 30 files).

- [ ] **Step 6: Commit** `feat(core): GameMaker JSON DOM with byte-exact round trip`.

### Task 2: Save codec and checksum salts

**Files:**
- Create: `src/StoneshardSaveEditor.Core/Files/SaveCodec.cs`, `src/StoneshardSaveEditor.Core/Files/Salt.cs`
- Test: `tests/StoneshardSaveEditor.Core.Tests/Files/SaveCodecTests.cs`

**Interfaces** (namespace `StoneshardSaveEditor.Core.Files`):
- Consumes: none.
- Produces:
  - `enum SaveFileKind { CharactersMap, CharacterMap, SlotFile }`
  - `static class Salt`: `string For(SaveFileKind kind, string? characterFolder = null, string? slotFolder = null)`
  - `sealed record DecodedSave(byte[] Json, string StoredHash)`
  - `static class SaveCodec`:
    - `DecodedSave Decode(byte[] file)`: throws `SaveFormatException`.
    - `string ComputeHash(byte[] json, string salt)`
    - `bool HashMatches(DecodedSave d, string salt)`
    - `byte[] Encode(byte[] json, string salt)`
  - `sealed class SaveFormatException : Exception`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Salt_strings_match_game() {
    Assert.Equal("stOne!characters_v1!shArd", Salt.For(SaveFileKind.CharactersMap));
    Assert.Equal("stOne!characters_v1!character_2!shArd", Salt.For(SaveFileKind.CharacterMap, "character_2"));
    Assert.Equal("stOne!characters_v1!character_2!exitsave_1!shArd", Salt.For(SaveFileKind.SlotFile, "character_2", "exitsave_1"));
}
[Fact] public void Real_files_have_valid_hashes() { /* characters.map, character_2/character.map, exitsave_1/save.map + data.sav → HashMatches true;
    Decode(data.sav).StoredHash == "1286653c2cd1cbdeee110fd96dff7970" */ }
[Fact] public void Reencoding_real_data_sav_yields_same_payload() { /* Decode → Encode(json, salt) → Decode: Json and StoredHash equal */ }
[Fact] public void Decode_rejects_garbage() { /* random bytes; truncated zlib; payload without trailing NUL or with non-hex trailer → SaveFormatException */ }
```

- [ ] **Step 2: Run** `dotnet test --filter SaveCodecTests`. Expected: FAIL (types missing).

- [ ] **Step 3: Implement.**
  - Payload = `json + 32 lowercase hex + 0x00`.
  - Hash = MD5 of `json` bytes followed by UTF-8 `salt` bytes.
  - Compress and decompress with `System.IO.Compression.ZLibStream` (`CompressionLevel.Optimal`).

- [ ] **Step 4: Run tests.** Expected: PASS.

- [ ] **Step 5: Commit** `feat(core): zlib+salted-MD5 save codec`.

### Task 3: Find characters and slots

**Files:**
- Create: `src/StoneshardSaveEditor.Core/Files/SaveLocator.cs`, `src/StoneshardSaveEditor.Core/Files/GameDate.cs`
- Test: `tests/StoneshardSaveEditor.Core.Tests/Files/SaveLocatorTests.cs`

**Interfaces:**
- Consumes: `SaveCodec`, `Salt`, `GmJson` (Tasks 1–2).
- Produces:
  - `enum SlotStatus { Ok, Damaged, UnsupportedVersion }`
  - `sealed record SlotInfo(string CharacterFolder, string SlotFolder, string FullPath, DateTime SavedAtUtc, string LocationKey, string AvatarSprite, string NameKey, SlotStatus Status, string? StatusMessage)`
  - `sealed record CharacterInfo(string Folder, string NameKey, string? ClassKey, int? Level, string AvatarSprite, IReadOnlyList<SlotInfo> Slots)`: slots are newest first.
  - `sealed class SaveLocator(string root)`:
    - `static string DefaultRoot` = `%LOCALAPPDATA%\StoneShard\characters_v1`
    - `IReadOnlyList<CharacterInfo> Scan()`
  - `static class GameDate`: `DateTime ToUtc(double days)` (epoch 1899-12-30 UTC).
  - `static class SupportedVersions`: `const string WipeVersion = "0.9"`.

- [ ] **Step 1: Write failing tests** (on `Fixtures.CopyRealSavesToTemp()`)

```csharp
[Fact] public void Scan_lists_both_characters_newest_slot_first() {
    // 2 characters. character_2: NameKey "Velmir", ClassKey "Revenger", Level 5, AvatarSprite "s_Velmir",
    // 6 slots, Slots[0].SlotFolder == "exitsave_1", LocationKey "CaravanCamp". character_1: NameKey "Jorgrim", Level 1.
}
[Fact] public void Scan_ignores_non_slot_folders() => /* no slot "exitsave_1_backup_20261021_215927" */;
[Fact] public void Damaged_slot_is_reported_not_thrown() { /* write junk to character_2/save_1/data.sav → that slot Damaged, others Ok */ }
[Fact] public void Hash_mismatch_is_damaged() { /* re-encode save_2/data.sav with salt for "save_9" → Damaged, StatusMessage mentions checksum */ }
[Fact] public void Unsupported_version_is_flagged() { /* rewrite save_1/save.map with wipeVersion "0.10" (re-signed) → UnsupportedVersion */ }
[Fact] public void GameDate_converts_days_to_utc() =>
    Assert.InRange(GameDate.ToUtc(46316.836047453703940846025943756), new DateTime(2026,10,21,20,3,54,DateTimeKind.Utc), new DateTime(2026,10,21,20,3,55,DateTimeKind.Utc));
[Fact] public void Missing_root_returns_empty_list() => Assert.Empty(new SaveLocator(@"C:\does\not\exist").Scan());
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement `SaveLocator.Scan`.**
  - Character folders match `^character_\d+$`; slot folders match `^(exitsave|save|autosave)_\d+$`.
  - Read `save.map` for each slot.
  - A slot is `Damaged` when its `save.map` or `data.sav` fails to decode or its hash doesn't match. `data.sav` is hash-checked at scan time (about 50 ms each).
  - `ClassKey` and `Level` (`characterDataMap.classKey`, `LVL`) come from the newest `Ok` slot's `data.sav`.

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Commit** `feat(core): locate characters and save slots`.

### Task 4: Save session: open, save with backup, guards

**Files:**
- Create:
  - `src/StoneshardSaveEditor.Core/Files/SaveSession.cs`
  - `src/StoneshardSaveEditor.Core/Files/BackupService.cs`
  - `src/StoneshardSaveEditor.Core/Files/GameProcessProbe.cs`
- Test: `tests/StoneshardSaveEditor.Core.Tests/Files/SaveSessionTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:
  - `interface IGameProcessProbe { bool IsGameRunning(); }`
  - `sealed class GameProcessProbe : IGameProcessProbe`: true when any process is named `StoneShard`.
  - `sealed record BackupInfo(string Path, DateTime CreatedUtc)`
  - `sealed class BackupService(string backupRoot)`:
    - `static string DefaultRoot` = `%LOCALAPPDATA%\StoneShardSaveEditor\backups`
    - `string Backup(SlotInfo slot)`: zips the slot folder to `<root>\<character>\<slot>\yyyyMMdd-HHmmss-fff.zip`.
    - `IReadOnlyList<BackupInfo> List(string characterFolder, string slotFolder)`: newest first.
    - `void Restore(BackupInfo backup, SlotInfo slot)`: backs up the current slot first, then overwrites the slot files from the zip.
  - `enum SaveResult { Saved, GameRunning, ChangedOnDisk, ReadOnly }`
  - `sealed class SaveSession`:
    - `static SaveSession Open(SlotInfo slot)`
    - `SlotInfo Slot`, `GmObject Root`
    - `bool ReadOnly`, `string? ReadOnlyReason`
    - `int ChangeCount`, `void MarkChanged()`
    - `SaveResult Save(BackupService backups, IGameProcessProbe probe)`: on success `ChangeCount` resets to 0, and the new file hash becomes the baseline for change detection.

- [ ] **Step 1: Write failing tests** (temp copy, `FakeProbe(bool)` in the test file)

```csharp
[Fact] public void Open_reads_root_and_starts_clean() { /* ChangeCount 0, ReadOnly false, Root["characterDataMap"] not null */ }
[Fact] public void Save_writes_valid_file_and_backup() {
    // set characterDataMap.STR = GmNumber.From(14); MarkChanged(); Save → Saved
    // re-decode data.sav: HashMatches(slot salt) && STR raw "14.0"; one zip under backupRoot/character_2/exitsave_1 containing data.sav, save.map, preview.png
}
[Fact] public void Save_refused_while_game_running() { /* FakeProbe(true) → GameRunning; data.sav bytes unchanged; no backup */ }
[Fact] public void Save_refused_when_file_changed_on_disk() { /* after Open, rewrite data.sav externally (re-encoded with STR 99) → ChangedOnDisk; file still has 99 */ }
[Fact] public void ReadOnly_session_never_writes() { /* UnsupportedVersion slot → ReadOnly true; Save → ReadOnly; bytes unchanged */ }
[Fact] public void Unchanged_save_rewrites_identical_payload() { /* MarkChanged without edits; Save; decompressed payload == original */ }
[Fact] public void Restore_brings_back_backup_and_backs_up_current_first() { /* Save twice with different STR; Restore oldest → STR original; List count +1 */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement.**
  - Change detection: SHA-256 of the `data.sav` bytes at open, compared just before writing.
  - Atomic write: write `data.sav.tmp` in the slot folder, then `File.Replace(tmp, dataSav, null)`.
  - Only `data.sav` is rewritten. `save.map` and `preview.png` are untouched.

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Commit** `feat(core): save session with backups, change and game-running guards`.

### Task 5: Lab tool: test characters, dump, diff

**Files:**
- Create:
  - `src/StoneshardSaveEditor.Core/Files/TestCharacterCloner.cs`
  - `src/StoneshardSaveEditor.Core/Json/GmDiff.cs`
  - `tools/StoneshardSaveEditor.Lab/StoneshardSaveEditor.Lab.csproj`
  - `tools/StoneshardSaveEditor.Lab/Program.cs`
- Test: `tests/StoneshardSaveEditor.Core.Tests/Files/TestCharacterClonerTests.cs`, `tests/StoneshardSaveEditor.Core.Tests/Json/GmDiffTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `static class TestCharacterCloner`: `string Clone(string root, string fromCharacter, string fromSlot, string? locationLabel = null)`.
    - Creates `character_{max+1}` containing a re-signed `character.map` and a slot `exitsave_1` (re-signed `save.map` and `data.sav`, copied `preview.png`).
    - When `locationLabel` is set, it replaces `save.map.locationTitleKey`, so the game's load menu tells test characters apart.
    - Returns the new folder name.
  - `static class GmDiff`: `IReadOnlyList<string> Diff(GmNode a, GmNode b)`. Lines look like `skillsDataMap.skillsAllDataList[18]: 1.0 -> false`, and paths use `.key` and `[i]`.
  - Lab CLI commands (plain `args` parsing, no packages):
    - `clone-test-character --root <dir> --from character_2 --slot exitsave_1 [--label Osbrook]`
    - `point-continue --root <dir> --character <c> --slot <s>`: backs up `characters.map` to the backups root first.
    - `dump --file <path> [--path a.b[3]]`
    - `diff --a <data.sav> --b <data.sav>`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Clone_creates_next_character_with_resigned_files() {
    // temp copy; Clone(root,"character_2","exitsave_1","Osbrook") == "character_3"
    // character_3/character.map hash ok with Salt(CharacterMap,"character_3"); exitsave_1/save.map + data.sav hash ok with Salt(SlotFile,"character_3","exitsave_1")
    // save.map locationTitleKey == "Osbrook"; data.sav JSON == source JSON; source files byte-identical to before
}
[Fact] public void Diff_reports_changed_paths() { /* two parsed docs differing in a.b[1] and c → exactly those two lines */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement** the cloner, the diff and the CLI. The CLI prints the new folder name and the diff lines.

- [ ] **Step 4: Run.** Expected: PASS. Then run `dotnet run --project tools/StoneshardSaveEditor.Lab -- diff --a <fixture exitsave_1/data.sav> --b <fixture save_2/data.sav>`. Expected: a non-empty list of paths.

- [ ] **Step 5: Commit** `feat(lab): test-character cloner, dump and diff`.

---

## Phase 2: Game data

### Task 6: data.win reader, icons, install discovery

**Files:**
- Create:
  - `src/StoneshardSaveEditor.GameData/StoneshardSaveEditor.GameData.csproj` (references `SharpZipLib`)
  - `GameInstall.cs`
  - `DataWin/DataWinFile.cs`, `SpriteTable.cs`, `ObjectTable.cs`, `TexturePages.cs`, `GmQoi.cs`
  - `Imaging/Rgba32Image.cs`, `Png.cs`
  - `Icons/IconService.cs`
- Test: `tests/StoneshardSaveEditor.GameData.Tests/` (`DataWinTests.cs`, `GmQoiTests.cs`, `IconServiceTests.cs`)

**Interfaces:**
- Consumes: none from Core.
- Produces (namespace `StoneshardSaveEditor.GameData`):
  - `static class GameInstall`: `string? Find(string? configuredDir)`.
    - Order: `configuredDir`, then every library in Steam's `steamapps\libraryfolders.vdf` + `\steamapps\common\Stoneshard`.
    - The Steam path comes from registry `HKCU\Software\Valve\Steam\SteamPath`, falling back to `C:\Program Files (x86)\Steam`.
    - Valid when both `data.win` and `StoneShard.exe` exist.
  - `sealed class DataWinFile`:
    - `static DataWinFile Load(string path)`
    - `(int Offset, int Length) Chunk(string name)`
    - `uint U32(int offset)`, `ushort U16(int offset)`
    - `string GmString(int offset)`: a length-prefixed string at `offset - 4`.
    - `ReadOnlySpan<byte> Span(int offset, int length)`
  - `sealed record SpriteFrame(int SourceX, int SourceY, int SourceW, int SourceH, int TargetX, int TargetY, int TargetW, int TargetH, int BoundW, int BoundH, int Texture)`
  - `sealed record SpriteInfo(string Name, int Width, int Height, SpriteFrame? FirstFrame)`
  - `sealed class SpriteTable(DataWinFile f)`: `int Count`, `SpriteInfo? Get(string name)`, `SpriteInfo ByIndex(int i)`, `IEnumerable<string> Names`.
  - `sealed class ObjectTable(DataWinFile f, SpriteTable sprites)`: `bool Exists(string obj)`, `string? SpriteOf(string obj)`, `IEnumerable<string> Names`.
  - `sealed record Rgba32Image(int Width, int Height, byte[] Pixels)`
  - `static class GmQoi`: `Rgba32Image Decode(ReadOnlySpan<byte> qoi)`
  - `static class Png`: `byte[] Encode(Rgba32Image img)`
  - `sealed class TexturePages(DataWinFile f)`: `Rgba32Image Page(int index)`, keeping the last 8 pages in an LRU; `int DecodeCount` (for tests).
  - `sealed class IconService(DataWinFile f, SpriteTable sprites, TexturePages pages, string cacheDir)`:
    - `byte[]? GetPng(string sprite)`
    - `(int W, int H)? Size(string sprite)`
    - `static string CacheDirFor(string cacheRoot, string dataWinPath)` = `<cacheRoot>\<length>-<lastWriteTicks>\icons`

- [ ] **Step 1: Write failing tests** (skip without `Fixtures.GameDir`, except the QOI test)

```csharp
[Fact] public void Sprite_table_has_expected_counts() { /* Count == 17248; Names.Count(n => n.StartsWith("s_inv_")) == 1768 */ }
[Fact] public void Object_table_maps_objects_to_sprites() {
    // SpriteOf("o_inv_bandage")=="s_inv_bondage"; ("o_inv_aqua_vitae")=="s_inv_blessed_water";
    // ("o_skill_incineration_ico")=="s_skills_inceneration"; ("o_pass_skill_excess_heat")=="s_passive_ecess_heat";
    // Names.Count(n => n.StartsWith("o_inv_")) == 1005
}
[Fact] public void Sprite_sizes_match_footprints() { /* Size("s_inv_torch")==(27,81); ("s_inv_roadguardbrigandine")==(54,81); ("s_Velmir")==(37,50); ("s_skills_wide_cut")==(25,24) */ }
[Fact] public void GmQoi_decodes_color_then_run() {
    // "fioq", w=2, h=1, len=6, bytes [0xFF,10,20,30,255, 0x40] → both pixels (10,20,30,255)
}
[Fact] public void Icon_png_has_right_size_and_visible_pixels() { /* GetPng("s_inv_moneybag"): PNG signature, IHDR 27×54; internal GetRgba has ≥100 pixels with alpha>0 */ }
[Fact] public void Second_service_serves_from_disk_cache() { /* new IconService on same cacheDir returns identical bytes with pages.DecodeCount == 0 */ }
[Fact] public void GameInstall_accepts_configured_dir() => Assert.Equal(Fixtures.GameDir, GameInstall.Find(Fixtures.GameDir));
```

- [ ] **Step 2: Run** `dotnet test tests/StoneshardSaveEditor.GameData.Tests`. Expected: FAIL.

- [ ] **Step 3: Implement.** Port the format knowledge from `docs/research/extract_icons_spike.py`:
  - **Chunks:** `FORM` at 0. Each chunk is a 4-byte name, a u32 size, then data.
  - **`SPRT`:** u32 count, then pointers.
    - Sprite record: name pointer at +0, width +4, height +8, version +60.
    - The frame count sits at `+76 + 4·(version≥2) + 4·(version≥3)`, followed by `TPAG` pointers.
  - **`TPAG`:** 11 × u16, in `SpriteFrame` field order.
  - **`TXTR`:** the entry's blob pointer is at +24.
    - A blob starting `2zoq` is BZip2 from the first `BZh`, inflated to `fioq`.
    - A blob starting `fioq` is QOI directly; anything else is PNG.
  - **`OBJT`:** the record name pointer is at +0 and the sprite index (i32, −1 = none) at +4.
  - **QOI ops:** the bit layout in the spike's `decode_qoi`.
  - **Frame composition:** frame 0 goes into a `BoundW×BoundH` transparent canvas at (TargetX, TargetY). Use nearest-neighbour if the target size differs from the source size.
  - **`Png.Encode`:** RGBA8, filter 0, `ZLibStream`, and CRC32 from a 256-entry table inside `Png.cs` (no package).

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Commit** `feat(gamedata): data.win sprites, objects and cached icon PNGs`.

### Task 7: Data tables and English names from StoneShard.exe

**Files:**
- Create: `src/StoneshardSaveEditor.GameData/Tables/GameTable.cs`, `ExeTables.cs`, `Localization.cs`
- Test: `tests/StoneshardSaveEditor.GameData.Tests/ExeTablesTests.cs`

**Interfaces:**
- Produces:
  - `sealed class TableRow`: `string Key` (first cell), `string this[string column]` (empty string if absent), `string[] Raw`.
  - `sealed class GameTable`: `string[] Header`, `IReadOnlyList<TableRow> Rows`, `TableRow? Find(string key)`.
  - `sealed class ExeTables`: `static ExeTables Load(string exePath)`; properties `GameTable Weapons, Armor, ItemsStats, SkillsStats`.
  - `sealed class Localization`: `static Localization Load(string exePath)`; `string? English(string key)`.

- [ ] **Step 1: Write failing tests** (skip without game)

```csharp
[Fact] public void Weapons_table_has_guard_crossbow() {
    var r = tables.Weapons.Find("Guard Crossbow")!;
    Assert.Equal("2", r["Tier"]); Assert.Equal("crossbow14", r["id"]); Assert.Equal("crossbow", r["Slot"]);
    Assert.Equal("40", r["MaxDuration"]); Assert.Equal("-55", r["Hit_Chance"]); Assert.Equal("25", r["Armor_Piercing"]);
}
[Fact] public void Armor_table_has_road_guard_brigandine() => Assert.NotNull(tables.Armor.Find("Road Guard Brigandine"));
[Fact] public void Items_stats_has_consumables_and_treatises() {
    // salve: Material "metal", Cat "medicine"; wineskin: Stacks "2"; treatise_pyro4: tier "4", Subcat "treatise"; moneybag: Cat "bag"
}
[Fact] public void Skills_stats_has_branches() { /* Wide_Cut: Branch "2haxe", Object "o_wide_cut"; Execution: Branch "axe" */ }
[Theory]
[InlineData("Wide_Cut","Hooking Chop")] [InlineData("salve","Healing Salve")] [InlineData("Road Guard Brigandine","Road Guard Armor")]
[InlineData("treatise_pyro1","Pyromancy Treatise I")] [InlineData("Antique Copper Ring","Ancient Copper Ring")] [InlineData("Splinted Boots","Splint Boots")]
public void English_names_resolve(string key, string english) => Assert.Equal(english, loc.English(key));
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement.** Tables are runs of consecutive NUL-terminated strings in the exe with the same `;` count.
  - **Locating a table:** find its header row by prefix.

    | Table | Header prefix |
    |---|---|
    | Weapons | `name;Tier;id;Slot;Subtype;altGrip;rarity;Mat;Price;Markup;MaxDuration;Rng;` |
    | Armor | `name;Tier;id;Slot;class;rarity;Mat;Price;Markup;MaxDuration;DEF;` |
    | ItemsStats | `id;;Price;EffPrice;tier;Cat;Subcat;Material;Weight;` |
    | SkillsStats | `;Object;Target;Range;KD;MP;` |

  - **Reading rows:** read following rows while the `;` count matches. Skip rows starting with `//` or `[` and rows with an empty first cell. Name duplicate or empty header columns `#<index>`.
  - **Localization:** every 14-cell row with a non-empty first cell; English is cell index 2; the first occurrence of a key wins.
  - Load the exe with a single `File.ReadAllBytes`. Both loaders share one scan helper.

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Commit** `feat(gamedata): item/skill tables and English names from the game exe`.

---

## Phase 3: Editing

### Task 8: Character stats and reputation

**Files:**
- Create:
  - `src/StoneshardSaveEditor.Core/Editing/EditRejectedException.cs`
  - `src/StoneshardSaveEditor.Core/Editing/CharacterEditor.cs`
  - `src/StoneshardSaveEditor.Core/Editing/ReputationEditor.cs`
- Test: `tests/StoneshardSaveEditor.Core.Tests/Editing/CharacterEditorTests.cs`, `ReputationEditorTests.cs`

**Interfaces:**
- Consumes: `SaveSession` (Task 4).
- Produces:
  - `sealed class EditRejectedException(string message) : Exception`
  - `enum CharacterField { Strength, Agility, Perception, Vitality, Willpower, Level, Experience, StatPoints, AbilityPoints, Health, Energy, Sanity, Morale, Hunger, Thirst, Fatigue, Intoxication, Pain }`
  - `sealed class CharacterEditor(SaveSession s)`: `IReadOnlyDictionary<CharacterField, double> Read()`; `void Set(CharacterField f, double value)` (validates, writes `GmNumber.From`, `MarkChanged`).
  - `sealed record SettlementReputation(string VillageKey, string LocationKey, double Value)`
  - `sealed class ReputationEditor(SaveSession s)`: `IReadOnlyList<SettlementReputation> Read()`; `void Set(string locationKey, double value)`.

Field → key → allowed values *(limits are my call and may be revisited after Task 12)*:

| Fields | Keys | Allowed |
|---|---|---|
| Strength, Agility, Perception, Vitality, Willpower | `STR`, `AGL`, `PRC`, `Vitality`, `WIL` | whole numbers 1–100 |
| Level | `LVL` | whole 1–99 |
| Experience | `XP` | whole 0–10,000,000 |
| StatPoints, AbilityPoints | `AP`, `SP` | whole 0–999 |
| Health, Energy | `HP`, `MP` | 0–9,999 |
| Sanity, Morale, Hunger, Thirst, Fatigue, Intoxication, Pain | `Sanity`, `Morale`, `Hunger`, `Thirsty`, `Fatigue`, `Intoxication`, `Pain` | 0–100 |
| Reputation | `globalmapDataMap.locationsDataMap["x_y"].reputation` | whole 0–100,000 |

Settlements come from `factionsDataMap.*.villages{name:{x,y}}`, giving location key `$"{x}_{y}"`.

- [ ] **Step 1: Write failing tests** (Velmir, temp copy)

```csharp
[Fact] public void Read_returns_velmir_values() { /* Strength 13, Agility 11, Perception 11, Vitality 12, Willpower 10, Level 5, Experience 1237,
                                                    StatPoints 0, AbilityPoints 0, Health 100, Energy 104, Sanity 51, Morale 78 */ }
[Fact] public void Set_writes_gamemaker_real() { /* Set(Strength,18) → characterDataMap.STR Raw "18.0"; ChangeCount 1 */ }
[Theory]
[InlineData(CharacterField.Strength, 12.5)] [InlineData(CharacterField.Strength, -1)] [InlineData(CharacterField.Sanity, 101)]
[InlineData(CharacterField.Level, 0)] [InlineData(CharacterField.Experience, double.NaN)] [InlineData(CharacterField.AbilityPoints, double.PositiveInfinity)]
public void Set_rejects_invalid(CharacterField f, double v) { /* throws EditRejectedException; value unchanged; ChangeCount 0 */ }
[Fact] public void Reputation_reads_five_settlements() { /* set of (VillageKey,LocationKey,Value): (Osbrook,32_10,4258) (Brynn,26_33,2648)
                                                          (Mannshire,26_23,2552) (Denbrie,14_37,2552) (RottenWillow,13_9,2500) */ }
[Fact] public void Reputation_set_updates_only_value() { /* Set("32_10",9000) → reputation Raw "9000.0"; reputationLogList still 7 entries */ }
[Fact] public void Reputation_rejects_unknown_location_and_out_of_range() { /* "99_99" and 100001 → EditRejectedException */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement** the two editors per the tables above. The rejection message names the field and the allowed range, for example "Sanity must be between 0 and 100".

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Commit** `feat(core): character stats and reputation editing`.

### Task 9: Inventory: read, place, edit, gold

**Files:**
- Create: `src/StoneshardSaveEditor.Core/Editing/InventoryEditor.cs`, `src/StoneshardSaveEditor.Core/Editing/BagGrid.cs`
- Test: `tests/StoneshardSaveEditor.Core.Tests/Editing/InventoryEditorTests.cs` (with `FakeShapes`)

**Interfaces:**
- Consumes: `SaveSession`, `EditRejectedException`.
- Produces:
  - `interface IItemShapeProvider { (int W, int H) Footprint(string itemName); string SpriteFor(string itemName); }`
  - `sealed record ItemView(int Index, string Name, bool Equipped, string Slot, int? X, int? Y, int W, int H, int? WeaponSet, double? Charges, double? Stack, double? Durability, double? MaxDurability, bool IsGear, string Sprite)`
  - `sealed class InventoryEditor(SaveSession s, IItemShapeProvider shapes)`:
    - `const int Columns = 10`; `static int BagRows = 5` (raised in Task 12 once confirmed).
    - `IReadOnlyList<ItemView> Read()`
    - `int Gold()`
    - `void SetCharges(int index, int value)`, `void SetStack(int index, int value)`
    - `void SetDurability(int index, double current, double max)`
    - `void Delete(int index)`, `int Duplicate(int index)`
    - `int Add(GmArray entry)`
    - `void SetGold(int amount, Func<GmArray> newMoneybag)`
    - `(int X, int Y)? FirstFit(int w, int h)`

**Entry layout** (`inventoryDataList[i]`, §6.3):

| Index | Meaning |
|---|---|
| 0 | name |
| 1 | props |
| 2 | f2 |
| 3 | cell |
| 4 | f4 |
| 5 | charges mirror |
| 6 | f6 |
| 7 | f7 |
| 8 | weapon set |
| 9 | slot |

**Rules:**
- **Bag item:** `slot == "N/A"`, `x = cell % 10`, `y = cell / 10`.
- **Gear:** `props` has `Metatype`.
- **Charges** are editable when the item is not gear and `props.charge >= 1`; allowed values are whole numbers 1–99. Write `props.charge` and `[5]`.
- **Stack** is editable when `props` has `Stack`: whole 0–10,000,000.
- **Durability** is editable when `MaxDuration > 0`: max is a whole number 1–9,999, and current is 0 to max.
- **A new bag entry** is `[name, props, 0.0, cell, 0.0, charge, -4.0, 0.0, 0.0, "N/A"]`. Duplicating an equipped item converts it to that bag form.
- **Gold:**
  - `Gold()` is the sum of `Stack` over `o_inv_moneybag` items in the bag.
  - `SetGold` sets the first moneybag's `Stack` and deletes any other moneybags.
  - If there is no moneybag, `SetGold` adds `newMoneybag()`; at 0 it deletes the moneybag.
- **Placement:** first fit scans rows top to bottom, then columns left to right, within `Columns × BagRows`. No room → `EditRejectedException("No room in the bag")`.

- [ ] **Step 1: Write failing tests.** `FakeShapes` gives Velmir's footprints: torch, tinker 1×3; wineskin, moneybag, mead, potion04_empty, brandy, aqua_vitae 1×2; splint 2×1; everything else 1×1.

```csharp
[Fact] public void Read_lists_33_items_12_equipped() { /* 33 total, 12 Equipped, 21 in bag */ }
[Fact] public void Bag_positions_come_from_cell() { /* o_inv_moneybag X3 Y0 Stack 2864; o_inv_splint X2 Y4 W2 H1 */ }
[Fact] public void Gold_reads_2864() => Assert.Equal(2864, inv.Gold());
[Fact] public void SetCharges_updates_props_and_mirror() { /* salve at cell 15 → SetCharges(i,1): props.charge Raw "1.0" and entry[5] Raw "1.0" */ }
[Fact] public void SetDurability_on_gear_only() { /* Flat Kettle Helmet (50,105) → Duration "50.0"; (106,105) rejected; on o_inv_salve rejected */ }
[Fact] public void FirstFit_skips_occupied_cells() { /* FirstFit(1,1)==(7,0); FirstFit(2,2)==(7,0); FirstFit(1,3)==(7,0) */ }
[Fact] public void Duplicate_places_copy_in_first_gap() { /* Duplicate(salve) → new item X7 Y0, same charge, original unchanged */ }
[Fact] public void Bag_full_rejects_duplicate_and_add() { /* fill all free cells with 1×1 duplicates, then Duplicate → EditRejectedException("No room in the bag"); count unchanged */ }
[Fact] public void Delete_removes_entry() { /* Delete(cheese) → 32 items, no o_inv_cheese */ }
[Fact] public void SetGold_updates_creates_and_deletes() { /* 99999 → Stack "99999.0"; remove bag then SetGold(50, factory) → new bag at first fit; SetGold(0) → no moneybag */ }
[Fact] public void Inventory_rejects_invalid_numbers() { /* SetCharges 0, -1; SetGold -5; SetDurability(NaN, 105) → EditRejectedException; ChangeCount 0 */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement** `BagGrid` (occupancy from bag items' cells and footprints) and `InventoryEditor`.

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Commit** `feat(core): inventory read, placement and edits`.

### Task 10: Item catalog and props for new items

**Files:**
- Create: `src/StoneshardSaveEditor.GameData/Catalog/GameItemShapes.cs`, `ItemPropsBuilder.cs`, `ItemCatalog.cs`
- Test: `tests/StoneshardSaveEditor.GameData.Tests/CatalogTests.cs` (references Core)

**Interfaces:**
- Consumes: `IItemShapeProvider`, `InventoryEditor.Add` (Task 9), `ExeTables`, `Localization` (Task 7), `SpriteTable`, `ObjectTable` (Task 6), `GmNode` (Task 1).
- Produces:
  - `sealed class GameItemShapes(SpriteTable sprites, ObjectTable objects) : IItemShapeProvider`
    - Sprite: names starting `o_` → `objects.SpriteOf(name)`; otherwise `"s_inv_" + lowercase(letters and digits of name)`.
    - Footprint = ceil(size / 27); a missing sprite gives 1×1 and the sprite `"missing"`.
  - `enum CatalogAvailability { Ready, CloneFromSave, Unavailable }`
  - `sealed record CatalogItem(string Key, string DisplayName, string Category, string Sprite, int W, int H, CatalogAvailability Availability, string? Reason)`
  - `static class ItemPropsBuilder`: `GmObject? FromGearRow(TableRow row, bool isWeapon)`, `GmObject? FromItemsStatsRow(TableRow row)`, `GmObject? CloneFromSave(string key, GmObject saveRoot)`.
  - `sealed class ItemCatalog(ExeTables t, Localization loc, SpriteTable s, ObjectTable o)`: `IReadOnlyList<CatalogItem> All(GmObject saveRoot)`, `GmArray CreateEntry(string key, GmObject saveRoot)`.
- **Catalog contents:**
  - **Gear:** Weapons and Armor rows. Key = row name; Category = the `Slot` column.
  - **Other items:** ItemsStats rows whose `o_inv_<id>` object exists. Key = `o_inv_<id>`; Category = `Cat`.
  - DisplayName = `loc.English(name or id) ?? key`.
- **Availability:**
  - `Ready`: the builder for that category passes its golden test below.
  - Else `CloneFromSave`: an instance exists anywhere in `saveRoot`, including merchant `trade_list`s.
  - Else `Unavailable`, with reason "Item data not readable from game files yet".
  - A sprite missing from the game → `Unavailable` ("No icon in game files").
- **New items:** `Duration = MaxDuration`, `identified = 1.0`. Consumables get `charge` = `Stacks` if set, else 1.

- [ ] **Step 1: Write failing golden tests.** Compare built props against Velmir's real props key by key: numbers by value, strings and arrays by raw text. Ignore the per-instance keys `Duration`, `bolt`, `Colour`, `key`, `is_trade_item`, `i_index`, `lootList`, `Stack`, `charge`, `Effects_Duration`.

```csharp
[Theory] [InlineData("Guard Crossbow")] [InlineData("Voulge")] [InlineData("Road Guard Brigandine")] [InlineData("Flat Kettle Helmet")]
[InlineData("Splinted Boots")] [InlineData("Splint Vambraces")] [InlineData("Chain Belt")] [InlineData("Azure Cape")]
[InlineData("Antique Copper Ring")] [InlineData("Fang Pendant")]
public void Gear_props_match_real_items(string name) { /* FromGearRow(row) vs Velmir's props → no differences outside ignore set */ }
[Theory] [InlineData("o_inv_salve")] [InlineData("o_inv_bandage")] [InlineData("o_inv_wineskin")] [InlineData("o_inv_torch")]
[InlineData("o_inv_tinker")] [InlineData("o_inv_cheese")] [InlineData("o_inv_mead")] [InlineData("o_inv_moneybag")]
public void Consumable_props_match_real_items(string obj) { /* FromItemsStatsRow vs Velmir's props, same rule */ }
[Fact] public void Catalog_lists_game_items_with_availability() { /* All(root).Count > 1000; "o_inv_treatise_pyro1" not Unavailable; every Unavailable has Reason */ }
[Fact] public void Added_item_lands_in_bag() { /* inv.Add(catalog.CreateEntry("o_inv_salve", root)) → item at (7,0), Charges ≥1 */ }
[Fact] public void Shapes_resolve_gear_and_objects() { /* SpriteFor("Guard Crossbow")=="s_inv_guardcrossbow"; Footprint("Road Guard Brigandine")==(2,3); SpriteFor("o_inv_bandage")=="s_inv_bondage" */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement the builders by working the golden diffs down to zero.** Table columns whose names equal props keys copy straight across as reals. Derived keys come from the diffs: `Material` ← `Mat`/`Material`, `Metatype`, `DMG`/`DamageType`/`Main`, `Suffix`, `quality`, `cursedQuality`, `Curse`, `is_cursed`, `Armor_Type`, `idName`, `tags`, `rarity`.

  **Gate:** if a golden theory can't be made fully green for some rows, those rows' category is `CloneFromSave`/`Unavailable` (§10.1). Never ship guessed props. Mark the failing cases `Skip = "props not derivable: <reason>"` and list them in `docs/research/in-game-findings.md`.

- [ ] **Step 4: Run.** Expected: PASS, apart from documented skips.

- [ ] **Step 5: Commit** `feat(gamedata): item catalog with game-accurate props`.

### Task 11: Skills: state, learn, unlearn, respec, unlock; skill catalog

**Files:**
- Create:
  - `src/StoneshardSaveEditor.Core/Editing/SkillsEditor.cs`
  - `src/StoneshardSaveEditor.GameData/Skills/SkillCatalog.cs`
  - `src/StoneshardSaveEditor.GameData/Skills/tree-unlocks.json` (content `{}`, embedded)
  - `src/StoneshardSaveEditor.GameData/Skills/skill-trees.json` (content `{}`, embedded)
- Test: `tests/StoneshardSaveEditor.Core.Tests/Editing/SkillsEditorTests.cs`, `tests/StoneshardSaveEditor.GameData.Tests/SkillCatalogTests.cs`

**Interfaces:**
- Consumes: `SaveSession`, `EditRejectedException`, `ExeTables.SkillsStats`, `Localization`, `ObjectTable`.
- Produces (Core):
  - `sealed record SkillState(string Object, bool Learned, bool Locked)`
  - `sealed class SkillsEditor(SaveSession s)`:
    - `static IReadOnlySet<string> Innate` = { `o_skill_butchering_ico`, `o_skill_torch_strike_ico`, `o_skill_craft_ico`, `o_pass_skill_Sudden_Attacks`, `o_skill_trap_search_ico` }
    - `IReadOnlyList<SkillState> Read()`, `int AbilityPoints`
    - `void Learn(string obj, bool spendPoint)`: `learned = 1.0`, `locked = 0.0`; when `spendPoint`, `SP − 1` (reject if `SP < 1`).
    - `void Unlearn(string obj, bool refundPoint)`: rejects innate skills. Sets `learned = 0.0`, `SP + 1` when refunding, and replaces the hotbar entry `o_skill_<stem>` with `-4.0`.
    - `int RespecAll()`, `int RefundTree(IEnumerable<string> objs)`
    - `void Unlock(IEnumerable<string> objs)`, `void Lock(IEnumerable<string> objs)`
  - Entry stride is 5: `[Object, Learned, F2, Locked, F4]`. Hotbar: `skillsDataMap.skillsPanelDataList`, 5 rows × 10.
- Produces (GameData):
  - `sealed record SkillInfo(string Object, string DisplayName, string Sprite, bool Passive, string TreeId)`
  - `sealed record SkillTree(string Id, string DisplayName, string? TreatisePrefix, IReadOnlyList<SkillInfo> Skills)`
  - `sealed class SkillCatalog`:
    - `static SkillCatalog Build(GmObject saveRoot, ExeTables t, Localization loc, ObjectTable o)`
    - `IReadOnlyList<SkillTree> Trees`, `SkillTree TreeOf(string obj)`
    - `IReadOnlyList<string> UnlockSet(string treeId)`: the union of `tree-unlocks.json` volumes for that tree, empty if unknown.
    - `TreeLayout? Layout(string treeId)`: from `skill-trees.json`, null if absent.
  - `sealed record SkillPosition(int Col, int Row, string[] Requires)`
  - `sealed record TreeLayout(IReadOnlyDictionary<string, SkillPosition> Skills)`. Its data is filled in Task 13.

**Tree assignment:** the save list is grouped in blocks of passives followed by actives. A block takes its actives' `Branch` (`o_skill_<x>_ico` ↔ `SkillsStats` row whose `Object` is `o_<x>`, case-insensitive). The 'none'-branch blocks are resolved by the tests below.

Tree ids, display names and treatise prefixes *(my call)*:

| Id | Display | Treatise | Id | Display | Treatise |
|---|---|---|---|---|---|
| athletic | Athletics | athletics | sword | Swords | sword |
| combat | Warfare | combat | axe | Axes | axe |
| magic_mastery | Magic Mastery | magic | mace | Maces | mace |
| arcanistics | Arcanistics | arcane | dagger | Daggers | dagger |
| pyromancy | Pyromancy | pyro | 2hsword | Greatswords | gsword |
| geomancy | Geomancy | geo | 2haxe | Greataxes | greataxes |
| electromancy | Electromancy | electro | 2hmace | Greatmaces | greatmaces |
| ranged | Archery | ranged | dual | Dual Wielding | dualwield |
| spear | Polearms | polearms | shield | Shields | shield |
| staff | Staves | staff | armor | Armored Combat | armor |
| survival | Survival | survival | basic | Basic | — |

- [ ] **Step 1: Write failing tests**

```csharp
// Core (Velmir temp copy)
[Fact] public void Read_lists_232_skills_11_learned() { }
[Fact] public void Innate_skills_cannot_be_unlearned() { /* Unlearn("o_skill_craft_ico", true) → EditRejectedException */ }
[Fact] public void Unlearn_refunds_and_clears_hotbar() { /* wide_cut: learned Raw "0.0"; SP Raw "1.0"; skillsPanelDataList[0][0] Raw "-4.0" */ }
[Fact] public void RespecAll_refunds_six() { /* returns 6; SP 6; learned set == Innate */ }
[Fact] public void Learn_spends_point() { /* after RespecAll: Learn(wide_cut, true) → SP 5, Learned, !Locked */ }
[Fact] public void Learn_without_points_is_rejected() { /* SP 0, spendPoint → EditRejectedException */ }
[Fact] public void Unlock_clears_locked_flag() { /* Unlock(["o_skill_flame_ring_ico"]) → Locked false, Raw "0.0" */ }
[Fact] public void RefundTree_unlearns_only_learned_listed() { /* [wide_cut, dismember, shieldbreaker, killing_swing] → 3 */ }
// GameData
[Fact] public void Trees_cover_all_232_skills() { /* 22 trees (21 with treatise + basic); every save skill in exactly one tree */ }
[Theory] [InlineData("o_skill_wide_cut_ico","2haxe")] [InlineData("o_pass_skill_shieldbreaker","2haxe")] [InlineData("o_skill_first_aid_ico","survival")]
[InlineData("o_skill_torch_strike_ico","basic")] [InlineData("o_skill_flame_ring_ico","pyromancy")] [InlineData("o_skill_execution_ico","axe")]
public void Skills_land_in_their_tree(string obj, string tree) { }
[Fact] public void Names_and_sprites_resolve() { /* wide_cut → "Hooking Chop", "s_skills_wide_cut"; ≥ 200 of 232 names come from Localization */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement.**
  - For passive names, find the localization key form (likely the stem in title case, such as `Shieldbreaker`) by probing `Localization`. Fall back to title-casing the stem.

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Commit** `feat: skills editing and skill catalog`.

---

## Phase 4: In-game checks

### Task 12: Experiment session A, run with the owner

**Files:**
- Create: `tools/StoneshardSaveEditor.Lab/Experiments.cs`, `docs/research/in-game-findings.md`
- Modify (with findings):
  - `InventoryEditor.BagRows`
  - `CharacterEditor` limits
  - `ItemPropsBuilder` (charges for new items)
  - `src/StoneshardSaveEditor.GameData/Skills/tree-unlocks.json`
  - The tests that pin those values

**Interfaces:**
- Consumes: everything in Phases 1–3.
- Produces:
  - Lab command `make-experiments --root <live saves root>`: one test character per experiment, each cloned from `character_2/exitsave_1` with a location label. It prints a table of folder ↔ label ↔ purpose.
  - Lab command `extract-unlocks --before <data.sav> --after <data.sav> --treatise <id>`: merges the skills whose `locked` went truthy → falsy into `tree-unlocks.json` under that treatise id.
  - Lab command `watch --root <live saves root> --out tests/fixtures/experiments`:
    - copies every changed `data.sav` of a test character to `<out>/<character>/<yyyyMMdd-HHmmss>-data.sav`;
    - never touches `character_1` or `character_2`;
    - runs in the background during the session.
    
    This way the owner never copies files by hand. Append `tests/fixtures/experiments/` to `.gitignore`.
  - A final `tree-unlocks.json`: `{ "pyro1": ["o_skill_…", …], … }`.

**Experiments** (label shown in the game's load menu):

| Label | Setup | Owner reports |
|---|---|---|
| **Osbrook** | untouched clone | loads normally, same as Velmir |
| **Brynn** | STR 18, AP 3, SP 4, LVL 6, XP 2000, HP 999, Sanity 150 (raw, bypasses validation), Hunger 0, Osbrook rep 9000, gold 99,999 | each value as shown in game; stat/ability points available to spend; any clamping |
| **Mannshire** | salve 3→1 charges, wineskin 6→2, helmet durability 105→50, delete cheese, duplicate a salve, add Noble Longsword + an armor piece + `o_inv_antitoxin` + `o_inv_treatise_pyro1` (catalog), 1×1 salves forced at cells 50, 60, 70, 80, 90 | all visible and usable; which of the probe salves show (bag rows); new gear stats match the game's tooltip |
| **Denbrie** | SP 2; unlearn Hooking Chop (Wide_Cut) without relocking; learn Killing Swing directly (`locked`→0, `learned`→1); set Fatal Hit `locked`→0 only | skill menu state of Greataxes; whether Hooking Chop's passive bonuses still show on the character sheet (§10.5, compare `characterDataMap.buffs` in the snapshot). Then learn one available skill in-game and quit |
| **RottenWillow**, then **Mill**, **OsbrookBrewery**, **CaravanCamp** as needed | bag cleared, then the vol I treatise of every tree that has one, split across as many of these clones as the bag (rows 0–4) requires | read every book in each, quit. Then on the last one, screenshot each skill-tree tab into a folder they name |
| **Osbrook II** (a second Osbrook-labelled clone) | Pyromancy vol I–IV in the bag | read vol I, quit; reload and read vol II, quit; repeat for III and IV. `watch` captures each step |
| **Steam Cloud** | after Brynn is confirmed | fully exit the game and Steam, restart, reload Brynn: the edits are still there |

- [ ] **Step 1: Implement `make-experiments` and `extract-unlocks`.** Run `make-experiments` against a temp copy first. Expected: `Clone_creates_next_character_with_resigned_files`-style checks pass for each new folder (add `Experiments_build_expected_characters` to the Lab test file), and the bag-probe salves occupy cells 50–90.

- [ ] **Step 2: Commit** `feat(lab): experiment builder and unlock extractor`.

- [ ] **Step 3: Run `make-experiments` against the live saves root** (adds characters only; `character_1` and `character_2` untouched). Then `point-continue` to the Osbrook test character, and start `watch` in the background.

- [ ] **Step 4: Send the owner the checklist:** each label, what to do and what to report. **Stop and wait** for their report.

- [ ] **Step 5: Record every observation** in `docs/research/in-game-findings.md` as a table: experiment → observation → decision.

- [ ] **Step 6: Apply the decisions:**
  - set `BagRows`;
  - adjust limits;
  - run `extract-unlocks` per treatise to fill `tree-unlocks.json`;
  - decide whether higher volumes add skills. If they do, "Unlock tree" uses the union of all volumes (my call), and a follow-up batch covers the other trees' higher volumes;
  - note whether the game recomputes locks (Denbrie).

  Update the affected tests to the confirmed values and run `dotnet test`. Expected: PASS.

- [ ] **Step 7:** `point-continue` back to `character_2/exitsave_1`. **Commit** `docs: in-game findings session A; apply confirmed values`.

### Task 13: Skill tree layouts and "Follow game rules"

**Files:**
- Create: `src/StoneshardSaveEditor.Core/Editing/SkillRules.cs`
- Modify: `src/StoneshardSaveEditor.GameData/Skills/skill-trees.json`, `SkillCatalog.cs`
- Test: `tests/StoneshardSaveEditor.Core.Tests/Editing/SkillRulesTests.cs`, `tests/StoneshardSaveEditor.GameData.Tests/SkillTreesDataTests.cs`

**Interfaces:**
- Consumes: the owner's tree screenshots and the Denbrie diff (Task 12); `SkillsEditor`; `SkillCatalog`.
- Produces:
  - `skill-trees.json` schema: `{ "<treeId>": { "<skillObject>": { "col": int, "row": int, "requires": ["<skillObject>", …] } } }`, transcribed from the screenshots. Lines between icons give `requires`.
  - `TreeLayout` and `SkillPosition` are as defined in Task 11. `SkillRules` lives in Core and receives only the `requires` map, so Core never depends on GameData.
  - `sealed class SkillRules(IReadOnlyDictionary<string, string[]> requires, Func<string, IReadOnlyList<string>> firstTierOfTreeOf)`:
    - `bool CanLearn(string obj, IReadOnlyList<SkillState> state)`
    - `IReadOnlyList<string> ToUnlockAfterLearn(string obj, IReadOnlyList<SkillState> state)`
    - `IReadOnlyList<string> ToRelockAfterUnlearn(string obj, IReadOnlyList<SkillState> state)`
    - `string? BlockingDependent(string obj, IReadOnlyList<SkillState> state)`
  - Unlock semantics: "any" or "all" of `requires`, whichever the Denbrie diff shows. Write the conclusion at the top of `SkillRules.cs`.
  - **Rules ON:**
    - Learn needs `!Locked` and `SP ≥ 1`, then unlocks dependents.
    - Unlearn is rejected while a learned skill depends on it ("Unlearn {name} first", *my call*); otherwise it re-locks unlearned dependents that are no longer satisfied.
    - Respec all leaves only first-tier skills of open trees unlocked.
  - **Rules OFF:** Learn and Unlearn are pure toggles. They touch only that skill and the hotbar: learning costs no point and unlearning refunds none, so points don't inflate. Points can be set directly in the top bar. **Respec all** and **Refund this tree** always refund, in both modes. *(my call)*

- [ ] **Step 1: Ask the owner for anything missing.** The Task 12 screenshots should cover every tree; ask only for missing tabs.

- [ ] **Step 2: Write failing tests**

```csharp
// synthetic tree: A (tier 1) → B, A → C, B → D
[Fact] public void Learning_A_unlocks_B_and_C() { }
[Fact] public void Locked_D_cannot_be_learned() { }
[Fact] public void Unlearning_A_is_blocked_while_B_is_learned() { /* BlockingDependent == "B" */ }
[Fact] public void Unlearning_A_relocks_unlearned_B_and_C() { }
// data file
[Fact] public void Every_tree_has_positions_for_all_its_skills() { /* for 21 trees: layout keys == tree skills; requires reference skills of the same tree */ }
[Fact] public void First_tier_matches_tree_unlocks() { /* skills with no requires == UnlockSet(tree) where known */ }
```

- [ ] **Step 3: Run.** Expected: FAIL.

- [ ] **Step 4: Transcribe `skill-trees.json`** and implement `SkillRules`. Wire it into `SkillsEditor` through new overloads `Learn(obj, SkillRules? rules)` and `Unlearn(obj, SkillRules? rules)`, where `null` means rules off.

- [ ] **Step 5: Run.** Expected: PASS.

- [ ] **Step 6: Commit** `feat: skill tree layouts and game-rules mode`.

---

## Phase 5: App (Blazor Hybrid)

The owner switched the UI from React to Blazor Hybrid on 2026-10-22 (spec §5, §9).

- The screens are Razor components in a Razor class library (`UI`). They call an in-process `EditorService` that wraps the Core editors and the game data.
- A WinForms window (`App`) hosts them in `BlazorWebView`.
- There is no HTTP API, no token and no npm.
- **Icons and save screenshots** are plain files served to the WebView through virtual host names:
  - `https://icons.sse/…` maps to the icon cache root;
  - `https://saves.sse/…` maps to the saves root.
  - Components get URLs from `IAssetUrls`, so tests don't need a WebView.

### Task 14: Editor service

**Files:**
- Create:
  - `src/StoneshardSaveEditor.UI/StoneshardSaveEditor.UI.csproj` (`Microsoft.NET.Sdk.Razor`, `net10.0`; references Core and GameData)
  - `src/StoneshardSaveEditor.UI/Services/EditorService.cs`, `GameDataProvider.cs`, `Settings.cs`, `Views.cs`, `Names.cs`
- Modify: `src/StoneshardSaveEditor.Core/Editing/SkillsEditor.cs` (add `UnlockTree`)
- Test: `tests/StoneshardSaveEditor.UI.Tests/EditorServiceTests.cs`, `tests/StoneshardSaveEditor.Core.Tests/Editing/SkillsEditorTests.cs`

**Interfaces:**
- Consumes: all Core and GameData types.
- Produces (namespace `StoneshardSaveEditor.UI.Services`):
  - `SkillsEditor.UnlockTree(IEnumerable<string> firstTier, string treatiseBook)` (Core): clears `locked` and appends the book to `characterDataMap.Books_Read` if missing, as reading it does (in-game finding).
  - `sealed record EditorOptions(string SavesRoot, string BackupRoot, string CacheRoot, string SettingsPath, IGameProcessProbe Probe, Func<string?>? PickFolder)`
  - `sealed class Settings { string? GameDir; static Settings Load(string path); void Save(string path); }`: JSON, camelCase.
  - `sealed record GameFiles(string GameDir, ObjectTable Objects, ExeTables Tables, Localization Localization, IconService Icons, GameItemShapes Shapes, ItemCatalog Catalog)`
  - `sealed class GameDataProvider(Func<string?> configuredDir, string cacheRoot)`: `GameFiles? Files`, `string? Error`, `string? GameDir`, `void Reset()`.
    - It loads lazily, once per game folder and process.
    - Any exception becomes `Error`; it never throws.
  - **View records:**
    - `SetupView(string SavesRoot, bool SavesFound, string? GameDir, bool GameFound, string? GameError)`
    - `SlotView(string Folder, string DisplayName, DateTime SavedAtLocal, string Location, SlotStatus Status, string? StatusMessage)`
    - `CharacterView(string Folder, string Name, string? ClassName, int? Level, string AvatarSprite, IReadOnlyList<SlotView> Slots)`
    - `SkillView(string Object, string DisplayName, string Sprite, bool Passive, bool Learned, bool Locked, int? Col, int? Row)`
    - `SkillTreeView(string Id, string DisplayName, string? TreatisePrefix, bool Open, int LearnedCount, bool CanUnlock, IReadOnlyList<SkillView> Skills)`
    - `ReputationView(string Name, string LocationKey, double Value)`
    - `SessionView(string CharacterFolder, string SlotFolder, string Name, string Location, bool ReadOnly, string? ReadOnlyReason, int ChangeCount, bool GameFound, IReadOnlyDictionary<CharacterField, double> Character, int Gold, IReadOnlyList<ItemView> Items, int BagRows, IReadOnlyList<SkillTreeView> Trees, bool RulesAvailable, IReadOnlyList<ReputationView> Reputation)`
  - `static class Names`:
    - `SlotDisplayName(string folder)`: `exitsave_1` → "Exit save", `save_N` → "Save N", `autosave_N` → "Autosave N".
    - `Pretty(string key, Localization? loc)`: `loc.English(key)`, or else the key split into words ("CaravanCamp" → "Caravan Camp", "RottenWillow" → "Rotten Willow", "Brynn_NW" → "Brynn NW").
  - `sealed class EditorService(EditorOptions options, GameDataProvider game)`:
    - `event Action? Changed`
    - **Setup:**
      - `SetupView Setup()`
      - `void SetGameDir(string path)`: `EditRejectedException` if not a game folder; saves settings and resets the provider.
      - `string? BrowseForGameDir()`
    - **Saves:** `IReadOnlyList<CharacterView> Characters()`
    - **Session:**
      - `bool IsOpen`, `SessionView Current`
      - `SessionView Open(string character, string slot)`, `SessionView Reload()`
      - `SaveResult Save()`
      - `IReadOnlyList<BackupInfo> Backups()`
      - `SessionView Restore(string backupPath)`: only listed backups.
      - `bool GameRunning()`
    - **Edits** (each returns `SessionView` and raises `Changed`; a read-only session rejects them with "This save is read-only: <reason>"):
      - `SetCharacter(CharacterField f, double v)`, `SetReputation(string locationKey, double v)`
      - `SetCharges(int i, int v)`, `SetStack(int i, int v)`, `SetDurability(int i, double current, double max)`
      - `DeleteItem(int i)`, `DuplicateItem(int i)`, `AddItem(string key)`, `SetGold(int v)`
      - `LearnSkill(string obj, bool rules)`, `UnlearnSkill(string obj, bool rules)`
      - `RespecAll()`, `RefundTree(string treeId)`, `UnlockTree(string treeId)`
    - `IReadOnlyList<CatalogItem> Catalog()`
  - **Rules:**
    - Placing items (`DuplicateItem`, `AddItem`, `SetGold` when it must create a purse) and `Catalog()` need the game files. Without them they throw `EditRejectedException("Game files are needed for this")`.
    - With `rules: true`: learning needs the skill to be available (`!Locked`) and costs a point; unlearning refunds one. Task 13 adds prerequisites.
    - With `rules: false`: pure toggles.
    - `RulesAvailable` = every treatise tree has a layout in `skill-trees.json` (false until Task 13).
    - `UnlockTree`: `UnlockSet(treeId)` plus book `treatise_<prefix>1`. It throws `EditRejectedException` when there is no data for that tree yet.

- [ ] **Step 1: Write failing tests**

```csharp
// Core: SkillsEditorTests
[Fact] public void UnlockTree_unlocks_first_tier_and_records_the_book_like_reading_it() { /* flags 0.0; Books_Read ends with "treatise_pyro1"; once only; 1 change */ }
// UI: EditorServiceTests (temp copy of real saves, FakeProbe, temp settings/backups/cache)
[Fact] public void Characters_list_velmir_with_named_slots() { /* 2 characters; Velmir: Revenger, level 5, s_Velmir, 6 slots; first "Exit save", "Caravan Camp", Ok; contains "Autosave 3", "Save 2" */ }
[Fact] public void Open_edit_save_round_trip() { /* Strength 13 → 18, ChangeCount 1, Save → Saved, reopen → 18 & ChangeCount 0; Gold 2864; BagRows 5; 33 items; 5 reputation rows */ }
[Fact] public void Reload_discards_unsaved_edits() { }
[Fact] public void Edit_rejections_carry_the_message() { /* Sanity 150 → EditRejectedException containing "Sanity"; SetCharges(999, 2) rejected */ }
[Fact] public void Using_the_session_before_opening_fails() { /* Current → InvalidOperationException */ }
[Fact] public void Save_reports_gameRunning() { }
[Fact] public void Save_reports_changedOnDisk() { }
[Fact] public void Backups_are_listed_and_restorable() { /* restore → Strength 13; unlisted path → EditRejectedException */ }
[Fact] public void Game_data_powers_trees_catalog_and_unlock() { /* skip without game: 22 trees; Greataxes open, 3 learned; Pyromancy closed + CanUnlock; "Hooking Chop";
     Location "Caravan Camp"; reputation contains "Rotten Willow"; Catalog > 1000; AddItem treatise; UnlockTree pyromancy → open and Books_Read on disk after Save; RespecAll → 6 points */ }
[Fact] public void Missing_saves_root_is_reported() { /* SavesFound false; Characters empty; BrowseForGameDir without PickFolder → null */ }
[Fact] public void Unparseable_game_files_degrade_gracefully() { /* junk data.win + exe → GameFound false, GameError set; Open + SetCharacter work; Trees empty; Catalog → EditRejectedException */ }
[Fact] public void Game_dir_is_validated_and_saved() { /* empty folder → EditRejectedException; real game dir → settings.json written */ }
[Fact] public void Names_are_pretty() { /* SlotDisplayName cases; Pretty("RottenWillow", null) == "Rotten Willow"; Pretty("Brynn_NW", null) == "Brynn NW" */ }
```

- [ ] **Step 2: Run** `dotnet test --project tests/StoneshardSaveEditor.UI.Tests`. Expected: FAIL (compile).

- [ ] **Step 3: Implement.**
  - `GameDataProvider` keeps a static `ConcurrentDictionary<gameDir|cacheRoot, Lazy<GameFiles>>` and drops entries that failed.
  - Without game files, item views use 1×1 footprints and the sprite `"missing"`.
  - `SkillCatalog` is built once per opened session.

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Commit** `feat(ui): editor service over the save editors`.

### Task 15: Desktop window, save picker and editor frame

**Files:**
- Create:
  - `src/StoneshardSaveEditor.UI/_Imports.razor`, `App.razor`
  - `Screens/SavePicker.razor`, `Screens/Editor.razor`
  - `Components/NumberField.razor`, `Components/Icon.razor`, `Components/Banner.razor`, `Components/BackupsDialog.razor`
  - `Services/IAssetUrls.cs`, `wwwroot/app.css`
  - `src/StoneshardSaveEditor.App/StoneshardSaveEditor.App.csproj` (`Microsoft.NET.Sdk.Razor`, `net10.0-windows`, `WinExe`, `UseWindowsForms`, package `Microsoft.AspNetCore.Components.WebView.WindowsForms`)
  - `Program.cs`, `MainForm.cs`, `WebViewAssets.cs`, `wwwroot/index.html`
- Test: `tests/StoneshardSaveEditor.UI.Tests/ShellTests.cs` (bUnit)

**Interfaces:**
- Consumes: `EditorService` and views (Task 14).
- Produces:
  - `interface IAssetUrls { string Icon(string sprite); string Preview(string character, string slot); }`
    - The App implementation ensures the icon PNG is in the cache (`IconService.GetPng`) and returns `https://icons.sse/<path under cache root>`.
    - Previews return `https://saves.sse/<character>/<slot>/preview.png`.
  - `<NumberField Value Whole Min Max OnCommit>`: commits on Enter or blur; non-numbers, decimals in whole fields, and out-of-range values show an inline error and don't commit.
  - `<Icon Sprite Scale=2>`: `<img>` at 2× with `image-rendering: pixelated`.
  - `App.razor` shows `SavePicker` until a slot is opened, then `Editor`.
  - `Editor` header has the portrait, name, slot and location; tabs Character · Inventory · Skills · Reputation (bodies placeholders until Tasks 17–19); the unsaved-changes count; "Undo all", "Backups", "Save to game".
  - Every 3 s it polls `GameRunning()`. When the game runs it shows the banner "Stoneshard is running. Close the game before saving." and disables "Save to game".
  - Save results:
    - `ChangedOnDisk` → "The game changed this save after you opened it." plus a "Reload" button;
    - `ReadOnly` → the read-only reason.
- **Window:** title "Stoneshard Save Editor", 1280×820, minimum 1100×720, background `#1b1815`.
- **WebView2:**
  - user-data folder `%LOCALAPPDATA%\StoneShardSaveEditor\webview2`;
  - virtual hosts `icons.sse` → cache root and `saves.sse` → saves root (`SetVirtualHostNameToFolderMapping`, allow);
  - missing runtime → message box with `https://go.microsoft.com/fwlink/p/?LinkId=2124703`, then exit.
- **PickFolder:** `FolderBrowserDialog` on the UI thread.

- [ ] **Step 1: Write failing bUnit tests**

```csharp
[Fact] public void SavePicker_shows_characters_and_slots() { /* "Velmir", "Revenger · Level 5", "Exit save", "Caravan Camp"; damaged slot shows its warning */ }
[Fact] public void Picking_a_slot_opens_the_editor() { }
[Fact] public void Editor_shows_unsaved_change_count() { /* after an edit → "1 unsaved change" */ }
[Fact] public void Editor_shows_game_running_banner_and_disables_save() { }
[Fact] public void ChangedOnDisk_offers_reload() { }
[Fact] public void NumberField_rejects_non_numeric_input() { /* "abc" → error, OnCommit not called; "12.5" with Whole → error */ }
[Fact] public void Setup_problems_are_explained() { /* no saves → "No Stoneshard saves found at <path>"; game missing → "Game files not found" + "Choose game folder…" */ }
```

- [ ] **Step 2: Run.** Expected: FAIL.

- [ ] **Step 3: Implement** to the approved mockup `docs/superpowers/specs/mockups/app-shell.html`, with the tokens in Global Constraints.

- [ ] **Step 4: Run.** Expected: PASS.

- [ ] **Step 5: Launch** with `dotnet run --project src/StoneshardSaveEditor.App`. Expected: a window titled "Stoneshard Save Editor" lists Velmir and Jorgrim with portraits and screenshots. Opening a slot shows the editor frame.

- [ ] **Step 6: Commit** `feat(app): desktop window with save picker and editor frame`.

### Task 16: Single-file packaging

**Files:**
- Modify: `src/StoneshardSaveEditor.App/StoneshardSaveEditor.App.csproj`

**Interfaces:**
- Produces `artifacts/StoneshardSaveEditor.exe`.
- Static web assets (index.html, app.css, `_content/...`) must work from a single file. Either:
  - `IncludeAllContentForSelfExtract=true`, or
  - embed them and serve from an embedded file provider.
  
  The builder's choice. Verify by running the exe from an empty folder.

- [ ] **Step 1: Publish.**

  ```
  dotnet publish src/StoneshardSaveEditor.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts
  ```

  Expected: `artifacts/StoneshardSaveEditor.exe`.
- [ ] **Step 2: Test the exe alone.** Copy only that exe to a fresh temp folder and launch it. Expected: the picker shows portraits and screenshots.
- [ ] **Step 3: Commit** `build: single-file self-contained exe`.

### Task 17: Character and Reputation tabs

**Files:**
- Create: `src/StoneshardSaveEditor.UI/Tabs/CharacterTab.razor`, `Tabs/ReputationTab.razor`; tests in `tests/StoneshardSaveEditor.UI.Tests/CharacterTabTests.cs`
- Modify: `Screens/Editor.razor`

**Interfaces:**
- Consumes: `SessionView.Character`, `.Gold`, `.Reputation`; `EditorService.SetCharacter`, `SetGold`, `SetReputation`; `NumberField`.
- **Layout** per `app-shell.html`:
  - **Attributes** (− / + steppers);
  - **Progress:** Level, Experience, Stat points, Ability points, Gold;
  - **Condition:** Health, Energy, Sanity, Morale, Hunger, Thirst, Fatigue, Intoxication, Pain;
  - **Reputation:** rows of name, bar (value / max(all values, 5000)) and a NumberField, sorted by value, descending.
- **Hints** (from in-game findings):
  - under Health/Energy: "The game caps these at your maximum";
  - under Level/Experience: "Changing these can make the game level you up when the save loads".

- [ ] **Step 1: Write failing tests:**
  - shows Strength 13 and Gold 2864;
  - the + on Strength makes it 14;
  - a rejected edit shows its message;
  - Reputation lists Osbrook 4258 first.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui): character and reputation tabs`.

### Task 18: Inventory tab, catalog, stack counts

**Files:**
- Create: `src/StoneshardSaveEditor.UI/Tabs/InventoryTab.razor`, `Components/CatalogDialog.razor`; tests `InventoryTabTests.cs`
- Modify:
  - `Core/Editing/InventoryEditor.cs` (`Count`)
  - `GameData/Catalog/GameItemShapes.cs`, `ItemCatalog.cs` (max stack)
  - `Screens/Editor.razor`

**Interfaces:**
- Consumes: `SessionView.Items`, `.BagRows`, `.GameFound`; the inventory edits; `Catalog()`.
- Produces (new scope from the owner, session A):
  - **Stack counts:** entry value `[6]` is the stack count of stackable items, such as gold coins (`o_inv_gold`, max 100) and ammo (bolts).
    - `ItemView.Count` (`double?`): set when the item's table `stack` > 0 and `[6]` ≥ 0.
    - `InventoryEditor.SetCount(int index, int value)`: whole numbers 1 to max stack.
    - `IItemShapeProvider.MaxStack(string itemName)`: 0 when not stackable.
    - `EditorService.SetCount`.
    - New stackable entries from the catalog get `[6]` = max stack.
    - Gold coins ("Gold" in the catalog) can be added like any item.
- **Layout** per `inventory-layout.html` option A:
  - **Equipment grid** (3 columns): neck/head/back; weapon/body/set 2; hands/belt/rings; empty/feet/empty. Durability bars.
  - **Bag:** `10 × BagRows` cells of 54 px; items absolutely positioned at `x*54, y*54`, size `w*54 × h*54`; badge = Stack ?? Count ?? Charges.
  - **Edit panel** for the selection:
    - Gold for the moneybag;
    - Count for stackables;
    - Charges for consumables;
    - Durability current/max when `MaxDurability > 0`;
    - "Duplicate" and "Delete".
  - **"+ Add item"** opens `CatalogDialog`:
    - case-insensitive search on the display name, plus category chips;
    - unavailable items greyed out with the reason as a tooltip;
    - clicking adds the item and closes the dialog.
  - Without game files, "+ Add item" is disabled with the tooltip "Game files not found".

- [ ] **Step 1: Write failing tests:**
  - Core `SetCount_updates_stack_slot` (Velmir's bolts: 19 → 5, max 20; 21 rejected);
  - catalog `Gold_coins_are_addable_as_a_full_stack`;
  - bUnit:
    - the moneybag renders at left 162 px, top 0, height 108 px;
    - selecting it shows Gold 2864; a salve shows Charges 3;
    - searching "treatise" filters the catalog; an unavailable item is disabled;
    - "No room in the bag" is shown.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui): inventory tab with game-style bag, catalog and stack counts`.

### Task 19: Skills tab

**Files:**
- Create: `src/StoneshardSaveEditor.UI/Tabs/SkillsTab.razor`; tests `SkillsTabTests.cs`
- Modify: `Screens/Editor.razor`

**Interfaces:**
- Consumes: `SessionView.Trees`, `.RulesAvailable`, `Character[StatPoints/AbilityPoints]`; `LearnSkill`, `UnlearnSkill`, `RespecAll`, `RefundTree`, `UnlockTree`, `SetCharacter`.
- **Layout** per `skills-layout.html`:
  - **Top bar:**
    - points fields;
    - "Respec all", which asks to confirm ("Unlearn every skill you picked and refund the points?");
    - "Refund this tree";
    - the "Follow game rules" toggle. It is disabled and off with the tooltip "Tree rules not loaded yet" until `RulesAvailable`.
  - **Tree list:** each tree with its learned count, and 🔒 when `!Open`.
  - **Tree view:** skills at `Col`/`Row`; without a layout, two rows (passives, then actives).
    - States: learned (gold border), available (green), locked (greyscale).
    - Clicking a learned skill unlearns it; clicking an available skill learns it.
  - **Closed tree:** "Unlock tree", enabled when `CanUnlock`.

- [ ] **Step 1: Write failing tests:**
  - Greataxes shows "Hooking Chop" as learned;
  - clicking it unlearns it;
  - "Respec all" asks to confirm, then refunds;
  - a closed tree with `CanUnlock` shows "Unlock tree" and opens the tree;
  - the rules toggle is disabled when `RulesAvailable` is false.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui): skills tab`.

### Task 20: Final acceptance with the owner

**Files:**
- Modify: `docs/research/in-game-findings.md`

- [ ] **Step 1: Run everything.**
  - `dotnet test` (all projects) → all pass.
  - Rebuild `artifacts/StoneshardSaveEditor.exe` (Task 16, Step 3).

- [ ] **Step 2: Make a fresh test character.** `clone-test-character --label Osbrook` on the live root, then `point-continue` to it.

- [ ] **Step 3: Owner walkthrough of §11,** done in the exe on the test character:
  1. set gold;
  2. add a weapon from the catalog;
  3. change a salve's charges and a helmet's durability;
  4. Respec all, and learn two skills;
  5. unlock Pyromancy;
  6. raise Osbrook's reputation;
  7. Save to game.

  Then the owner loads it in Stoneshard ("Continue") and confirms every change is present and nothing else changed. Also: open and save with no edits, and the game plays identically. **Stop and wait** for their report.

- [ ] **Step 4: Record the results** in the findings doc. Fix anything found, with a test per fix, and repeat Step 3 for the affected items.

- [ ] **Step 5:** `point-continue` back to `character_2/exitsave_1`. Tell the owner the test characters can now be deleted, and offer to delete them. **Commit** `docs: final acceptance results`. Then use superpowers:finishing-a-development-branch.
