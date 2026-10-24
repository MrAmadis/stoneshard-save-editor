# Inventory Drag and Drop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move items in the Inventory tab by dragging: equip, unequip, swap, move between weapon sets and rings, and rearrange the backpack, refusing what the game wouldn't allow.

**Architecture:** The rules and save writes live in Core:
- `GearRules` says which slot an item kind fits;
- `InventoryEditor.Refusal`/`Move` apply them to the save;
- the kinds come from the game tables via `IItemShapeProvider.Kind`.

A Lab test character checks in the game that editor-moved gear works before any UI is built. The UI then adds HTML5 drag and drop to the existing Inventory tab, through `EditorService`.

**Tech Stack:**
- .NET 10, C#, Blazor Hybrid (WinForms BlazorWebView, WebView2);
- tests: xunit.v3, bUnit.

**Spec:** `docs/superpowers/specs/2026-10-23-inventory-drag-and-drop-design.md`

## Global Constraints

- Work on branch `feature/inventory-drag` off `main`. Don't push until the owner has tested in the game.
- Commit with `git -c user.name=vedra -c user.email=theos.ares@gmail.com commit …`, ending each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Never write to `character_1` or `character_2` in the live saves folder. Tests use `Fixtures.CopyRealSavesToTemp()`; test characters come from the Lab only.
- Game files (`data.win`, `StoneShard.exe`) are read-only; nothing extracted from them is committed.
- Every save change goes through `InventoryEditor` and `EditorService.Edit`, so a failed move rolls back.
- A move changes only these entry fields: `f2`, `cell`, `f7`, `weaponSet`, `slot`. The item's `props`, `f4`, `chargesMirror` and `count` stay as they are.
- Backpack: `InventoryEditor.Columns` (10) × `InventoryEditor.BagRows` (5) cells; cell number = `y * 10 + x`.
- A refused drop changes nothing and shows no error banner: the target is shown faded or red while dragging, and dropping does nothing.
- Copy (owner-facing):
  - removal banner: `"{display name} had no room and was removed"`;
  - refusals: `"There's no room there"`, `"That doesn't go there"`, `"A two-handed weapon needs the other hand free"`, `"The other hand holds a two-handed weapon"`, `"Ammunition goes with a bow or crossbow in the other hand"`.

## Review Focus

- **Weapon set saved as `true`/`false`.** The game's autosaves write `weaponSet` and `f7` as booleans, and Velmir's bolts have `weaponSet: true`. They must read as set 2 and active, not as set 0. Test in Task 3.
- **Dropping an item where it already is.** This must change nothing: no change counted, and no "unsaved changes" prompt. Tests in Task 3 (Core) and Task 7 (UI).
- **A drag that ends outside any target.** Releasing over nothing must clear every highlight and outline. Test in Task 7.
- **Grabbing an item by its lower cell.** A torch grabbed at its middle cell must land with that cell under the cursor, not its top cell. Test in Task 8.
- **Moving a stack or a container.** Bolts keep their count and a purse keeps its coins and gems when moved. Test in Task 3.

---

### Task 1: Item kinds from the game tables

**Files:**
- Modify: `src/StoneshardSaveEditor.Core/Editing/InventoryEditor.cs` (`IItemShapeProvider`)
- Modify: `src/StoneshardSaveEditor.GameData/Catalog/GameItemShapes.cs`
- Modify: `src/StoneshardSaveEditor.GameData/Catalog/ItemCatalog.cs` (its `_shapes` field)
- Modify: `src/StoneshardSaveEditor.UI/Services/GameDataProvider.cs:108`
- Modify: `src/StoneshardSaveEditor.UI/Services/EditorService.cs` (`UnknownShapes`)
- Modify: `tools/StoneshardSaveEditor.Lab/GameServices.cs:14`
- Test: `tests/StoneshardSaveEditor.GameData.Tests/CatalogTests.cs`, and the `Shapes()` helper at line 22
- Test: `tests/StoneshardSaveEditor.Core.Tests/Editing/InventoryEditorTests.cs` (`FakeShapes`)

**Interfaces:**
- Produces:
  - `string IItemShapeProvider.Kind(string itemName)`: a weapon's or armor's table `Slot` (`sword`, `2hsword`, `Head`, `shield`, `Ring`, …), another item's `Cat` (`ammo`, `tool`, `food`, …), or `""` when the tables don't list it;
  - `GameItemShapes(SpriteTable sprites, ObjectTable objects, ExeTables tables)`.

- [ ] **Step 1: Create the branch.** Run `git checkout -b feature/inventory-drag` from `main`.

- [ ] **Step 2: Write the failing test** in `CatalogTests.cs`:

```csharp
[Theory]
[InlineData("Flat Kettle Helmet", "Head")]
[InlineData("Voulge", "spear")]
[InlineData("Guard Crossbow", "crossbow")]
[InlineData("Board Shield", "shield")]
[InlineData("Velmir Ring", "Ring")]
[InlineData("o_inv_leafshaped_bolts", "ammo")]
[InlineData("o_inv_torch", "tool")]
[InlineData("o_inv_cheese", "food")]
[InlineData("o_inv_no_such_item", "")]
public void Item_kinds_come_from_the_tables(string name, string kind) => Assert.Equal(kind, Shapes().Kind(name));
```

- [ ] **Step 3: Run it to see it fail.** Run `dotnet test tests/StoneshardSaveEditor.GameData.Tests --filter "FullyQualifiedName~Item_kinds"`. Expected: build error, because `Kind` doesn't exist.

- [ ] **Step 4: Implement `Kind`.**
  - Add `Kind` to `IItemShapeProvider`.
  - `GameItemShapes` takes `ExeTables`:
    - names starting `o_inv_` use `tables.ItemsStats.Find(stem)?["Cat"]`;
    - other names use `tables.Weapons.Find(name)?["Slot"] ?? tables.Armor.Find(name)?["Slot"]`;
    - anything else returns `""`.
  - `MaxStack` keeps using `ItemsStats`.
  - Update the three `GameItemShapes` constructions and the test `Shapes()` helper.
  - `UnknownShapes.Kind` returns `""`.
  - `FakeShapes.Kind` maps Velmir's gear:
    - armor: `Flat Kettle Helmet`→`Head`, `Road Guard Brigandine`→`Chest`, `Splint Vambraces`→`Arms`, `Splinted Boots`→`Legs`, `Chain Belt`→`Waist`, `Fang Pendant`→`Amulet`, `Azure Cape`→`Back`, `Velmir Ring` and `Antique Copper Ring`→`Ring`;
    - weapons and ammo: `Voulge`→`spear`, `Guard Crossbow`→`crossbow`, `o_inv_leafshaped_bolts`→`ammo`;
    - test-only names: `Peasant Hat`→`Head` and `Board Shield`→`shield`, size 1×1;
    - anything else returns `""`;
    - add `["Flat Kettle Helmet"] = (2, 2)` to its sizes.

- [ ] **Step 5: Run the tests.** Run `dotnet test`. Expected: all pass.

- [ ] **Step 6: Commit** `feat: item kinds from the game tables`.

### Task 2: Which slot each kind fits

**Files:**
- Create: `src/StoneshardSaveEditor.Core/Editing/GearRules.cs`
- Test: `tests/StoneshardSaveEditor.Core.Tests/Editing/GearRulesTests.cs`

**Interfaces:**
- Produces `public static class GearRules` with:
  - constants `Head = "o_inv_head"`, `Body = "o_inv_armor"`, `Hands = "o_inv_gloves"`, `Feet = "o_inv_boots"`, `Belt = "o_inv_belt"`, `Neck = "o_inv_neck"`, `Back = "o_inv_back"`, `Ring1 = "o_inv_ring_1"`, `Ring2 = "o_inv_ring_2"`, `RightHand = "o_inv_right_hand"`, `LeftHand = "o_inv_left_hand"`;
  - `IReadOnlyList<string> SlotsFor(string kind)`;
  - `bool IsTwoHanded(string kind)`;
  - `bool Shoots(string weaponKind, string ammoName)`;
  - `string? HandRefusal(string kind, string name, string hand, (string Kind, string Name)? other)`: null when allowed, otherwise one of the refusal strings in Global Constraints.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Theory]
[InlineData("Head", "o_inv_head")]
[InlineData("Chest", "o_inv_armor")]
[InlineData("Arms", "o_inv_gloves")]
[InlineData("Legs", "o_inv_boots")]
[InlineData("Waist", "o_inv_belt")]
[InlineData("Amulet", "o_inv_neck")]
[InlineData("Back", "o_inv_back")]
[InlineData("Ring", "o_inv_ring_1 o_inv_ring_2")]
[InlineData("shield", "o_inv_left_hand")]
[InlineData("sword", "o_inv_right_hand o_inv_left_hand")]
[InlineData("axe", "o_inv_right_hand o_inv_left_hand")]
[InlineData("mace", "o_inv_right_hand o_inv_left_hand")]
[InlineData("dagger", "o_inv_right_hand o_inv_left_hand")]
[InlineData("2hsword", "o_inv_right_hand")]
[InlineData("2haxe", "o_inv_right_hand")]
[InlineData("2hmace", "o_inv_right_hand")]
[InlineData("2hStaff", "o_inv_right_hand")]
[InlineData("spear", "o_inv_right_hand")]
[InlineData("bow", "o_inv_right_hand")]
[InlineData("crossbow", "o_inv_right_hand")]
[InlineData("ammo", "o_inv_left_hand")]
[InlineData("wand", "")]   // open until the in-game check (spec §5.1)
[InlineData("tool", "")]
[InlineData("food", "")]
[InlineData("", "")]
public void Slots_follow_the_game_tables(string kind, string slots) =>
    Assert.Equal(slots.Split(' ', StringSplitOptions.RemoveEmptyEntries), GearRules.SlotsFor(kind));

[Theory]
[InlineData("2hsword", true)]
[InlineData("2haxe", true)]
[InlineData("2hmace", true)]
[InlineData("2hStaff", true)]
[InlineData("spear", true)]
[InlineData("bow", true)]
[InlineData("crossbow", true)]
[InlineData("sword", false)]
[InlineData("shield", false)]
[InlineData("ammo", false)]
public void Two_handed_kinds(string kind, bool twoHanded) => Assert.Equal(twoHanded, GearRules.IsTwoHanded(kind));

[Theory]
[InlineData("crossbow", "o_inv_leafshaped_bolts", true)]
[InlineData("bow", "o_inv_bodkin_arrows", true)]
[InlineData("bow", "o_inv_leafshaped_bolts", false)]
[InlineData("spear", "o_inv_leafshaped_bolts", false)]
public void Bows_shoot_arrows_and_crossbows_bolts(string weapon, string ammo, bool shoots) =>
    Assert.Equal(shoots, GearRules.Shoots(weapon, ammo));

[Fact]
public void Hands_refuse_what_the_game_wouldnt_hold()
{
    Assert.Null(GearRules.HandRefusal("2hsword", "x", GearRules.RightHand, null));
    Assert.Equal("A two-handed weapon needs the other hand free", GearRules.HandRefusal("2hsword", "x", GearRules.RightHand, ("shield", "s")));
    Assert.Null(GearRules.HandRefusal("crossbow", "x", GearRules.RightHand, ("ammo", "o_inv_leafshaped_bolts")));
    Assert.Equal("The other hand holds a two-handed weapon", GearRules.HandRefusal("shield", "s", GearRules.LeftHand, ("spear", "Voulge")));
    Assert.Null(GearRules.HandRefusal("ammo", "o_inv_leafshaped_bolts", GearRules.LeftHand, ("crossbow", "Guard Crossbow")));
    Assert.Equal("Ammunition goes with a bow or crossbow in the other hand", GearRules.HandRefusal("ammo", "o_inv_arrows", GearRules.LeftHand, null));
    Assert.Null(GearRules.HandRefusal("shield", "s", GearRules.LeftHand, ("sword", "w")));
}
```

- [ ] **Step 2: Run the tests to see them fail.** Run `dotnet test tests/StoneshardSaveEditor.Core.Tests --filter "FullyQualifiedName~GearRules"`. Expected: build error.

- [ ] **Step 3: Implement `GearRules`.**
  - Slot lists, in the order the test gives them.
  - `Shoots`: `bow` with a name containing `arrows`; `crossbow` with a name containing `bolts`.
  - `HandRefusal`:
    - the right hand checks only two-handed against the other hand (ammo it `Shoots` is fine);
    - the left hand refuses next to a two-handed weapon unless it's ammo that weapon shoots;
    - ammo always needs a weapon that shoots it.
  - The doc comment cites spec §5.1, including the open categories.

- [ ] **Step 4: Run the tests.** Run `dotnet test tests/StoneshardSaveEditor.Core.Tests`. Expected: all pass.

- [ ] **Step 5: Commit** `feat(core): which slot each item kind fits`.

### Task 3: Moving items in the save

**Files:**
- Create: `src/StoneshardSaveEditor.Core/Editing/ItemPlace.cs`
- Modify: `src/StoneshardSaveEditor.Core/Editing/InventoryEditor.cs`
- Modify: `src/StoneshardSaveEditor.Core/Editing/BagGrid.cs` (expose `Fits`)
- Test: `tests/StoneshardSaveEditor.Core.Tests/Editing/InventoryMoveTests.cs` (reuse `FakeShapes`; move it to `TestSupport/FakeShapes.cs` so both test classes share it)

**Interfaces:**
- Consumes `GearRules` (Task 2) and `IItemShapeProvider.Kind` (Task 1).
- Produces:
  - `public sealed record ItemPlace(string Slot, int WeaponSet, int X, int Y)`, with `const string BagSlot = "N/A"`, `static ItemPlace InBag(int x, int y)`, `static ItemPlace Worn(string slot, int weaponSet = 0)` (X and Y −1), and `bool IsBag`;
  - `ItemView.Place` (computed: `Worn(Slot, WeaponSet ?? 0)` or `InBag(X, Y)`);
  - `public sealed record MoveResult(int Index, string? Removed)`;
  - `string? InventoryEditor.Refusal(int index, ItemPlace to)`;
  - `MoveResult InventoryEditor.Move(int index, ItemPlace to)`, which throws `EditRejectedException(Refusal)` when refused.

How a move writes the entry:
- **Bag form:** `f2` 0, `cell` `y*10+x`, `f7` 0, `weaponSet` 0, `slot` `N/A`.
- **Worn form:** `f2` −4, `cell` −4, `slot`, `weaponSet` (0 for non-hand slots), and `f7`:
  - `GmBool(true)` for non-hand slots, and for hands in the active weapon set;
  - `GmNumber(0)` for hands in the other set.
- **The active set** is the `weaponSet` of a worn hand item whose `f7` is true (`GmBool true` or a non-zero number), or 0 when none is.
- **Reading:** `weaponSet` reads as 1 when it's `GmBool(true)`.

How a move decides (spec §5): a move succeeds only if the final arrangement is allowed. Refusals:
- a bag spot not free (ignoring the moving item's own cells) or past the grid: "There's no room there";
- a slot not in `SlotsFor(kind)`: "That doesn't go there";
- a hand pair touched by the move that fails `HandRefusal`.

When the target is taken by `old`:
- **From another worn slot:** `old` trades into the mover's place if that's allowed. Otherwise it goes to the bag as below.
- **To the bag:** `old` tries the mover's freed top-left cell, then `FirstFit`. If neither fits, `old` is deleted and named in `MoveResult.Removed`. `MoveResult.Index` accounts for the shifted list.

Dropping where the item already is returns at once and changes nothing.

- [ ] **Step 1: Write the failing tests.** All open Velmir with `FakeShapes`. `EmptyBag(inv)` deletes every bag item.
  - `Moving_in_the_bag_changes_only_the_cell`: the torch moves to `FirstFit(1, 3)`. Its entry differs from the original only at `cell`.
  - `An_item_can_shift_onto_cells_it_is_leaving`: put the torch at `FirstFit(1, 4)` = (x, y), then move it to (x, y+1). Assert the new position.
  - `Moving_onto_a_taken_spot_is_refused_and_changes_nothing`: torch → the moneybag's cell. `Refusal` is "There's no room there"; `Move` throws; `session.ChangeCount` is unchanged.
  - `Moving_past_the_edge_is_refused`: torch → (0, 4) and → (10, 0).
  - `Unequipping_writes_the_bag_form`: the helmet → `FirstFit(2, 2)`. Check `f2` 0, `cell`, `f7` 0, `weaponSet` 0, `slot` `N/A`, and props equal to before.
  - `Equipping_writes_the_worn_form`: unequip the helmet, then move it to `Worn(Head)`. Check `f2` −4, `cell` −4, `f7` `GmBool(true)`, `weaponSet` 0, `slot` `o_inv_head`.
  - `Swapping_puts_the_old_item_where_the_new_one_was`: `Duplicate` the helmet (the copy goes to spot S), then move the copy to `Worn(Head)`. The original is in the bag at S. `MoveResult.Removed` is null.
  - `A_swapped_out_item_that_doesnt_fit_the_freed_spot_goes_to_the_first_free_one`: `EmptyBag`, a `Peasant Hat` at (9, 4), hat → `Worn(Head)`. The helmet is at (0, 0).
  - `With_no_room_the_swapped_out_item_is_removed`: `EmptyBag`, a `Peasant Hat` at (9, 4), the other 49 cells filled with 1×1 copies, hat → head. `Removed` is `"Flat Kettle Helmet"`, no helmet is left, and `Read()[result.Index].Name` is `"Peasant Hat"`.
  - `Worn_items_trade_places_when_each_fits_the_other`: Velmir Ring (ring 1) → `Worn(Ring2)`. The rings have swapped.
  - `A_two_handed_weapon_needs_the_other_hand_free`: Voulge → `Worn(RightHand, 1)` is refused with that message (set 1's left holds bolts).
  - `A_weapon_that_cant_trade_goes_to_the_bag`: crossbow → `Worn(RightHand, 0)`. The crossbow is in set 0; the Voulge is in the bag, because set 1's bolts don't go with a spear.
  - `Ammunition_goes_with_the_weapon_that_shoots_it`:
    - bolts → `Worn(LeftHand, 0)` is refused ("The other hand holds a two-handed weapon");
    - bolts → bag, then → `Worn(LeftHand, 1)` succeeds.
  - `The_active_weapon_set_decides_f7`: after the crossbow → set 0, the crossbow's `f7` is true; after the Voulge → set 1 right (bolts to the bag first), the Voulge's `f7` is 0.
  - `Weapon_sets_saved_as_true_read_as_set_2`: set the bolts' `weaponSet` to `GmBool(true)`. `Read()` gives `WeaponSet` 1.
  - `A_stack_keeps_its_count_and_a_purse_its_coins`: bolts → bag keeps `count` 19; moneybag → `FirstFit` keeps its `lootList` and `Stack`.
  - `Moving_to_where_it_already_is_changes_nothing`: `ChangeCount` is unchanged and the entry is equal to before.
  - `Shields_can_go_beside_a_one_handed_weapon_only`: `Board Shield` → `Worn(LeftHand, 0)` is refused next to the Voulge. Once the crossbow is in set 0 and set 0's left is free, it's still refused (two-handed).

- [ ] **Step 2: Run the tests to see them fail.** Run `dotnet test tests/StoneshardSaveEditor.Core.Tests --filter "FullyQualifiedName~InventoryMove"`. Expected: build error.

- [ ] **Step 3: Implement** `ItemPlace`, `MoveResult`, `ItemView.Place`, the bool-tolerant read of `weaponSet`/`f7`, `BagGrid.Fits` (public, false past the edges), `Refusal` and `Move`, as described above.

- [ ] **Step 4: Run the tests.** Run `dotnet test`. Expected: all pass.

- [ ] **Step 5: Commit** `feat(core): move items between the backpack and the equipment slots`.

### Task 4: Session G test characters

**Files:**
- Modify: `tools/StoneshardSaveEditor.Lab/Experiments.cs`
- Modify: `tools/StoneshardSaveEditor.Lab/Program.cs` (add `make-session-g` and its usage line)
- Test: `tests/StoneshardSaveEditor.Lab.Tests/ExperimentsTests.cs`

**Interfaces:**
- Consumes `InventoryEditor.Move`/`FirstFit` (Task 3) and `ItemCatalog.CreateEntry`.
- Produces `IReadOnlyList<ExperimentResult> Experiments.BuildSessionG(string root, GameServices game, BackupService backups, IGameProcessProbe probe)` and the Lab command `make-session-g --root <characters_v1> --game <Stoneshard folder>`.

The two test characters (spec §7):
- **`Osbrook` (G1): "gear moved by the editor".**
  1. Flat Kettle Helmet → bag.
  2. `Hood` added from the catalog → head.
  3. Velmir Ring → ring 2 (it trades with the Antique Copper Ring).
  4. Bolts → bag.
  5. Voulge → set 1 right (it trades with the crossbow).
  6. Bolts → set 0 left.
  7. Torch and moneybag moved to other free spots.
- **`Mannshire` (G2): "where the open kinds go".** Delete every bag item except the moneybag. Then add `Academy Wand`, `Makeshift Sling`, `o_inv_ammo_bullet`, `Lute`, `Pickaxe`, `Chain`, `Peasant Bow`, `o_inv_arrows` and `o_inv_torch`.

- [ ] **Step 1: Write the failing test** `Session_G_builds_the_moved_gear_characters`:
  - labels are `Osbrook` and `Mannshire`;
  - G1: `Hood` is at `o_inv_head`, with `f2` −4 and `f7` true; Flat Kettle Helmet is in the bag; ring 1 is the Antique Copper Ring and ring 2 is the Velmir Ring; the Guard Crossbow is at set 0 right; the bolts are at set 0 left with count 19; the Voulge is at set 1 right with `f7` 0;
  - G2: the bag holds each of the nine added names;
  - both saves are validly signed (`Load`).

- [ ] **Step 2: Run it to see it fail.** Run `dotnet test tests/StoneshardSaveEditor.Lab.Tests --filter "FullyQualifiedName~Session_G"`. Expected: build error.

- [ ] **Step 3: Implement** `BuildSessionG` with `Batch.Make`, plus `make-session-g` in `Program.cs`. Purpose strings:
  - G1: "check the gear shows where listed, armor and stats in the character screen match the worn gear, both weapon sets work, and nothing doubled or disappeared";
  - G2: "equip each item in the game and note which slot it goes to; does the sling take the bullets as the crossbow takes bolts?"

- [ ] **Step 4: Run the tests.** Run `dotnet test`. Expected: all pass.

- [ ] **Step 5: Commit** `feat(lab): session G tests gear moved by the editor`.

### Task 5: In-game check (gate)

**Files:**
- Modify: `docs/research/in-game-findings.md` (add Session G)
- Modify: `src/StoneshardSaveEditor.Core/Editing/GearRules.cs`, and its tests, only for kinds the owner confirms

- [ ] **Step 1: Build the characters in the live saves**, with the game closed. Run `dotnet run --project tools/StoneshardSaveEditor.Lab -- make-session-g --root "%LOCALAPPDATA%\StoneShard\characters_v1" --game "D:\SteamLibrary\steamapps\common\Stoneshard"`. It prints two `character_N` folders.

- [ ] **Step 2: Ask the owner to check both**, giving each character's purpose string. Stop until they report.

- [ ] **Step 3: Record the results** as "Session G" in `in-game-findings.md`.
  - If G1 shows the game doesn't take the moved gear (stats wrong, gear missing or doubled), **stop: the design goes back to the owner** (spec §7).
  - For each kind G2 confirms, add an `InlineData` row to `Slots_follow_the_game_tables` with the slot the owner saw, watch it fail, and add the kind to `SlotsFor`.
  - If the sling takes bullets, extend `Shoots` and its test the same way.

- [ ] **Step 4: Run the tests and commit** `docs: session G; gear kinds confirmed in-game`.

### Task 6: Moves through the editor service

**Files:**
- Modify: `src/StoneshardSaveEditor.UI/Services/EditorService.cs`
- Test: `tests/StoneshardSaveEditor.UI.Tests/EditorServiceTests.cs`

**Interfaces:**
- Consumes `InventoryEditor.Move`/`Refusal`, `ItemPlace` and `MoveResult` (Task 3).
- Produces:
  - `MoveResult EditorService.MoveItem(int index, ItemPlace to)` (through `Edit`);
  - `string? EditorService.MoveRefusal(int index, ItemPlace to)` (read-only, under the session lock, no change counted).

- [ ] **Step 1: Write the failing tests.**
  - `Moving_an_item_updates_the_view`: helmet → a free bag spot. In `Current.Items` the helmet isn't equipped and has that X and Y, and the save counts one unsaved change.
  - `Asking_whether_a_move_is_allowed_changes_nothing`: `MoveRefusal(helmet, Worn(Feet))` is "That doesn't go there", and the save has no unsaved changes.

- [ ] **Step 2: Run them to see them fail.** Run `dotnet test tests/StoneshardSaveEditor.UI.Tests --filter "FullyQualifiedName~EditorServiceTests"`. Expected: build error.

- [ ] **Step 3: Implement** both methods, following `DeleteItem` and `ItemName`.

- [ ] **Step 4: Run the tests.** Run `dotnet test`. Expected: all pass.

- [ ] **Step 5: Commit** `feat: item moves in the editor service`.

### Task 7: Dragging onto the equipment slots

**Files:**
- Modify: `src/StoneshardSaveEditor.UI/Tabs/InventoryTab.razor` (and its CSS: `wwwroot/app.css` or the tab's stylesheet, wherever `.gear-slot` is styled)
- Test: `tests/StoneshardSaveEditor.UI.Tests/InventoryTabTests.cs`

**Interfaces:**
- Consumes `EditorService.MoveItem`/`MoveRefusal` (Task 6) and `GearRules` slot constants.
- Produces markup that Task 8 and the tests rely on:
  - each drop target is `.gear-slot[data-slot='<slot>'][data-set='<0|1>']`;
  - the `data-cell` ids are `neck head back weapon-right weapon-left body set2-right set2-left hands belt ring1 ring2 feet`;
  - while dragging, each target has class `ok` (allowed) or `no`;
  - items are `draggable="true"`;
  - the removal banner is `<Banner Kind="warn">`, shown under the gold note.

- [ ] **Step 1: Write the failing tests.** Update `Moneybag_renders_at_its_cell` to `[data-cell='weapon-right']` for the Voulge and `[data-cell='set2-right']` for the crossbow, and add `[data-cell='set2-left']` for the bolts.
  - `Dragging_lights_the_slots_an_item_fits`: `h.Editor.AddItem("Hood")`, then `DragStart` on its bag button. The head target has `ok` and the feet target has `no`. After `DragEnd`, no element has `ok` or `no`.
  - `Dropping_a_helmet_on_the_head_swaps_it`: `DragStart` the Hood, `Drop` on the head target. Hood is equipped at `o_inv_head`; Flat Kettle Helmet is in the bag; the detail panel shows Hood.
  - `Dropping_where_the_game_wouldnt_allow_does_nothing`: drop the Hood on the feet target. `Current.Items` is unchanged and there's no `.banner-error`.
  - `Dropping_on_its_own_slot_does_nothing`: drag the worn helmet onto the head target. The save has no unsaved changes.
  - `A_swapped_out_item_with_no_room_is_reported`:
    - choose from `h.Editor.Catalog()` a `CatalogAvailability.Ready` item with `Category == "bow"` whose `W*H` is smaller than the Voulge's;
    - add it, then fill the bag by duplicating the torch until "No room in the bag";
    - drop the bow on the `weapon-right` target.
    - The `.banner-warn` banner reads "Voulge had no room and was removed".

- [ ] **Step 2: Run them to see them fail.** Run `dotnet test tests/StoneshardSaveEditor.UI.Tests --filter "FullyQualifiedName~InventoryTabTests"`. Expected: failures on the missing `data-cell`/`data-slot` targets.

- [ ] **Step 3: Implement.**
  - Split the weapon, set 2 and rings boxes into the per-hand and per-ring targets. Hands stay tall, with right and left side by side.
  - Make every item button `draggable="true"`. `@ondragstart` records the dragged item's index and its grab cell, `(int)(OffsetX / Cell), (int)(OffsetY / Cell)` for bag items and (0, 0) for worn ones. `@ondragend` clears it.
  - Each target uses `@ondragover:preventDefault`; `@ondrop` calls `MoveItem` only when `MoveRefusal` is null, then selects the moved item by `Place` and sets the removal notice from `MoveResult.Removed` (`EditorService.ItemName`).
  - Click-to-select stays as it is.
  - Styling (lit and faded) follows the mockup `docs/superpowers/specs/mockups/inventory-drag-swap-helmet.html`.

- [ ] **Step 4: Run the tests.** Run `dotnet test`. Expected: all pass.

- [ ] **Step 5: Commit** `feat(ui): drag items onto the equipment slots`.

### Task 8: Dragging within and into the backpack

**Files:**
- Modify: `src/StoneshardSaveEditor.UI/Tabs/InventoryTab.razor` (and the same stylesheet)
- Test: `tests/StoneshardSaveEditor.UI.Tests/InventoryTabTests.cs`

**Interfaces:**
- Consumes the drag state from Task 7 (dragged index, grab cell), `MoveItem` and `MoveRefusal`.
- Produces this markup:
  - while dragging, `.bag .cell[data-x][data-y]`: 50 drop cells, each `Cell` px;
  - `.bag .drop-outline`, positioned at (hover cell − grab cell) with the item's W × H, with class `fit` (allowed) or `clash`.

- [ ] **Step 1: Write the failing tests.** A test helper finds a free W×H spot from `Current.Items`' bag rectangles.
  - `The_outline_shows_where_an_item_would_land`: `DragStart` the moneybag, then `DragEnter` a free cell: the outline has `fit` and is 54 × 108 px at that cell. `DragEnter` the torch's cell: the outline has `clash`.
  - `Dropping_in_the_backpack_moves_an_item_to_a_free_spot`: the moneybag dropped on a free cell has that X and Y.
  - `Dropping_on_a_taken_spot_leaves_the_item_where_it_was`: the moneybag dropped on the torch's cell keeps its X and Y, and the save has no unsaved changes.
  - `Unequipping_by_dropping_into_the_backpack`: the worn helmet dropped on a free 2×2 spot is unequipped at that spot.
  - `Grabbing_an_item_by_its_lower_cell_keeps_that_offset`: `DragStart` the torch with `OffsetY = 1.5 * InventoryLayout.Cell`, then `DragEnter` the cell (x, y+1) of a free 1×3 spot at (x, y). The outline's top is at y and it has `fit`.

- [ ] **Step 2: Run them to see them fail.** Run `dotnet test tests/StoneshardSaveEditor.UI.Tests --filter "FullyQualifiedName~InventoryTabTests"`. Expected: the new tests fail.

- [ ] **Step 3: Implement** the drop cells and the outline.
  - `@ondragenter` sets the hover cell and `@ondrop` moves to `InBag(hover − grab)`; both use `@ondragover:preventDefault`. After a drop, select the moved item and show any removal notice, as in Task 7.
  - `@ondragend` (Task 7) clears the hover cell too.
  - Styling follows `docs/superpowers/specs/mockups/inventory-drag-backpack-move.html`.

- [ ] **Step 4: Run the tests.** Run `dotnet test`. Expected: all pass.

- [ ] **Step 5: Publish and check the exe.**
  - Run `dotnet publish src/StoneshardSaveEditor.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts`, with no editor running.
  - Launch a copy from an empty folder and confirm the save list shows.

- [ ] **Step 6: Commit** `feat(ui): drag items within and into the backpack`. Then ask the owner to try the exe on a test character.
