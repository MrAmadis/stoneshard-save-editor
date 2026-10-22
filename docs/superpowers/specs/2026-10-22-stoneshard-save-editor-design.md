# Stoneshard Save Editor — Design

Date: 2026-10-22
Status: draft for review

## 1. Intent

A save editor for the game **Stoneshard** that lets the owner change their character's inventory, gold, skills, stats and reputation. It should be good-looking and effortless to run.

**Why it exists:** the owner wants to fix things mid-run, mainly to:
- add items and adjust what they carry,
- respec a character into a different skill tree, including opening trees that are still locked,
- tweak stats, gold and reputation.

**Prior art, and why it isn't enough:**

- **A Python GUI editor (`stoneshard_gui`)** that the owner tried on 2026-10-21. They rejected it as "clunky": poor visuals and design, and too many steps to get it running. It left a backup folder `character_2/exitsave_1_backup_20261021_215927` inside the save directory.
- **[denbkh/stoneshard-save-editor](https://github.com/denbkh/stoneshard-save-editor)** (C# WinForms). It can edit base stats and unlearn abilities, but has no inventory editing, skill learning, tree unlocking, reputation editing or item images. Its source documents the checksum salt, which we verified (see §6).

## 2. Goals

1. **Effortless to run.** One `.exe`, double-click, nothing to install. It opens in its own window, finds the saves and the Stoneshard install by itself, and backs up a save automatically before changing it.
2. **Looks like the game.** Dark, game-styled UI using the game's own item icons, skill icons and character portraits.
3. **Inventory.**
   - Add any item in the game.
   - Change stack size or charges.
   - Change durability.
   - Duplicate and delete items.
   - Edit gold.
4. **Skills.** Respec both ways:
   - unlearn skills and refund their points to spend in-game, or
   - pick skills directly in the editor.

   Also: unlock a closed skill tree, and edit ability points.
5. **Stats.**
   - Attributes, level, XP and stat points.
   - Condition values: health, energy, sanity, morale, hunger, thirst, fatigue, intoxication, pain.
6. **Reputation** per settlement.
7. **Saves the game accepts.** The editor writes a valid checksum and leaves everything it didn't edit unchanged.

## 3. Non-goals (v1)

- Dragging items around to rearrange the bag. A nice-to-have for later.
- Equipping or unequipping gear in the editor. Equipped gear can only have its durability edited, or be deleted.
- Caravan stash editing.
- Quests, journal, world map, crime status, time and weather, lifetime statistics (`characterStatsDataMap`).
- Creating characters or saves.
- Platforms other than Windows. *(my call)*

## 4. Anti-goals: failure even if it "works"

- **Corrupting a save or losing progress.** Every write must be preceded by a backup, must carry a valid checksum, and must leave untouched data identical.
- **Clunky.** If it looks like a developer tool, or needs setup steps such as installing Python, Node or .NET or running commands, it has failed in exactly the way the previous tool did.
- **Redistributing game content.** No game art or data is shipped with the editor. It reads everything from the owner's own install at runtime.

## 5. Constraints

- **Stack: .NET backend + React UI** (owner's choice).
- **Machine:**
  - Windows 11, which ships the WebView2 runtime.
  - Installed toolchain: .NET SDK 8.0.302 and 10.0.401, Node 24.20, npm 11.19.
- **Saves:** `%LOCALAPPDATA%\StoneShard\characters_v1\`. The current save format is `wipeVersion "0.9"`, and the game is a YYC (native-compiled) build.
- **Game install:** `D:\SteamLibrary\steamapps\common\Stoneshard\` on this machine.
  - Find it through Steam's `libraryfolders.vdf` (registered libraries: `C:\Program Files (x86)\Steam`, `D:\SteamLibrary`).
  - If it isn't found, let the user browse to it once and remember the choice.
- **Steam Cloud** is enabled for these saves (`steam_autocloud.vdf` is present).

## 6. What we know: save format (verified on the owner's saves)

### 6.1 Folder layout

```
characters_v1/
  characters.map                  {"lastCharacter":"character_2","lastSave":"exitsave_1"}
  steam_autocloud.vdf
  character_N/
    character.map                 {dateTime, nameKey, prologue, compiler, wipeVersion, permadeath}
    <slot>/                       slot ∈ exitsave_1, save_1…, autosave_1…3
      save.map                    {valid, dateTime, nameKey, prologue, locationTitleKey, compiler, avatar, wipeVersion, permadeath}
      data.sav                    the full game state (~2.9 MB JSON)
      preview.png                 400×192 screenshot
```

- Only folders matching `^(exitsave|save|autosave)_\d+$` are save slots. Any other folder, such as the earlier tool's `exitsave_1_backup_…`, is ignored.
- `dateTime` is a day count from 1899-12-30. For example, 46316.836 is 2026-10-21 at 20:04 UTC, matching the folder time of 22:03 local.
- `save.map` also gives the location title key (for example `CaravanCamp`) and the portrait sprite (`avatar`, for example `s_Velmir`).

### 6.2 Encoding and checksum

All four file types share one encoding:
- the file is a **zlib** stream (header `78 9C`);
- it decompresses to UTF-8: `<JSON text><32 lowercase hex chars><0x00>`;
- the hex is `MD5(utf8(JSON text) + salt)`.

| File | Salt |
|---|---|
| `characters.map` | `stOne!characters_v1!shArd` |
| `character_N/character.map` | `stOne!characters_v1!character_N!shArd` |
| `character_N/<slot>/save.map` and `data.sav` | `stOne!characters_v1!character_N!<slot>!shArd` |

- **Verified:** for all four files of `character_2/exitsave_1`, the computed hash matches the stored one.
- Because the salt contains the folder names, a save is only valid in its own slot. A backup must be restored to the same slot.
- denbkh's editor writes compact re-serialized JSON, and the game accepts it. The game therefore parses the JSON; it doesn't compare text.

**JSON value rules:**
- Numbers are usually reals written as `1.0`.
- Booleans appear as `true`/`false`. GameMaker treats them as 1 and 0, and both forms show up for the same field.
- Some numbers carry more digits than a double holds, for example `46316.836047453703940846025943756`.

**Requirements:**
- Values the editor didn't touch are written back exactly as read, with no precision loss.
- New values use the type the field already has (a real with `.0` where the field holds reals).
- Opening a save and saving it without edits must yield a save that loads identically in the game.

### 6.3 `data.sav` sections used

The top-level keys include `characterDataMap`, `characterStatsDataMap`, `skillsDataMap`, `inventoryDataList`, `caravanStashDataList1–4`, `globalmapDataMap`, `factionsDataMap`, `questsDataMap`, `journalDataMap`, `timeDataMap`, and others. Only the parts below are edited. Everything else passes through untouched.

**Character (`characterDataMap`):**

| UI label | Key | Velmir's value |
|---|---|---|
| Strength | `STR` | 13 |
| Agility | `AGL` | 11 |
| Perception | `PRC` | 11 |
| Vitality | `Vitality` | 12 |
| Willpower | `WIL` | 10 |
| Level | `LVL` | 5 |
| Experience | `XP` | 1237 |
| Ability points | `SP` | 0 |
| Stat points | `AP` | 0 (needs confirming in-game) |
| Health | `HP` | 100 |
| Energy | `MP` | 104 |
| Sanity | `Sanity` | 51 |
| Morale | `Morale` | 78 |
| Hunger | `Hunger` | 19.9 |
| Thirst | `Thirsty` | 9.04 |
| Fatigue | `Fatigue` | 4.33 |
| Intoxication | `Intoxication` | 19.18 |
| Pain | `Pain` | 0 |

`SP` being ability points is confirmed:
- Jorgrim, a fresh level-1 Reaver, has `SP = 2`.
- Velmir at level 5 has `SP = 0` and 6 bought skills.
- Both fit 2 starting points, +1 per level, and 1 point per skill.

Identity fields: `nameKey`, `classKey` (`Revenger`), `playerClass` (`o_revenger`).

**Gold:** an inventory item `o_inv_moneybag` whose props hold `Stack: 2864.0`. If the character has no moneybag, setting gold adds one to the bag.

**Inventory (`inventoryDataList`):** one entry per item, a 10-element array:
`[name, props, f2, cell, f4, f5, f6, f7, f8, slot]`

- **`name`:**
  - consumables and misc items use the object name (`o_inv_salve`);
  - generated gear uses its display name (`Guard Crossbow`).
- **`props`:** the item's property map.
  - It holds `idName`, `Duration`/`MaxDuration` (durability), `charge`, `Stack`, `identified`, `rarity`, `Metatype`, the stats, `tags`, and so on.
  - Gear carries its full stats in here, for example `DMG`, `Armor_Piercing`, `Hit_Chance`.
- **Items in the bag:**
  - `slot` is `"N/A"`, `f2 = 0`, and `f6 = -4`.
  - `cell` is the top-left grid cell, numbered `y*10 + x` on a **10-column** grid.
  - `f5` mirrors `charge`.
  - `f4` holds values from 0 to 5, meaning unknown (possibly a sprite frame).
- **Equipped items:**
  - `f2 = cell = -4`.
  - `slot` is one of `o_inv_head`, `o_inv_neck`, `o_inv_back`, `o_inv_armor`, `o_inv_gloves`, `o_inv_belt`, `o_inv_ring_1`, `o_inv_ring_2`, `o_inv_boots`, `o_inv_right_hand`, `o_inv_left_hand`.
  - `f8` looks like the weapon set (0 or 1): Velmir has a voulge in set 0 and a crossbow plus bolts in set 1. `f7` is `true` or `0` (meaning unknown).
- **Footprint:** an item's size in cells is its icon size divided by 27 px. A torch is 27×81, so 1×3; a brigandine is 54×81, so 2×3.
- **Stack versus charges versus durability:**
  - `Stack` is used by the moneybag (gold).
  - `charge` and `f5` count uses: salve 3, wineskin 6.
  - `Duration`/`MaxDuration` is durability (gear; torch 50/50; tinkerer's tools 37/75). Consumables have 0/0.
- **Bag rows:** the number of rows isn't known yet. Velmir's items reach row 4.

**Skills (`skillsDataMap`):**
- `skillsAllDataList` is a flat list of 232 five-element entries: `[skillObject, learned, ?, locked, ?]`.
  - `learned`: `1.0` or `true` means learned.
  - `locked`: `1.0` or `true` means locked; `0.0` or `false` means available.
  - Fields 2 and 4 are unknown. Field 4 was `1.0` on one skill only.
  - Skill objects: actives are `o_skill_<name>_ico`, passives are `o_pass_skill_<name>`.
- **Innate skills** are learned on every fresh character: `o_skill_butchering_ico`, `o_skill_torch_strike_ico`, `o_skill_craft_ico`, `o_pass_skill_Sudden_Attacks`, `o_skill_trap_search_ico`. They are never refunded.
  - The learned value can be `true` or `1.0` for the same skill: Butchering was `true` on Jorgrim and `1.0` on Velmir. So innate skills can't be told apart by type and must be a fixed list.
- **Tree open or closed:** there is no separate flag. A tree is open when its first-tier skills have `locked = false`.
  - Both starting trees (from class) and treatises produce exactly this state.
  - Reading a treatise is not recorded anywhere else. The owner read treatises with Velmir, but `Books_Read` lists only lore books and notes.
  - Evidence:
    - Jorgrim (Reaver, level 1) has the first tier unlocked in Warfare, Survival, Dual Wielding, Axes, and the greatsword, greataxe and greatmace trees.
    - Velmir (Revenger, level 5, has read treatises) additionally has Athletics, Shields, Archery, Spears, Maces and First Aid in Survival.
  - Inside an open tree, deeper skills stay locked until their prerequisites are learned.
- **Tree membership, tiers and prerequisites are not in the save.** The list order appears grouped by tree, but boundaries and tiers must come from game data (§7).
- `skillsPanelDataList` is the hotbar: 5 rows × 10 of skill names, or `-4` for empty.
  - Unlearning a skill should also remove it from the hotbar. *(my call)*

**Reputation:** stored at `globalmapDataMap.locationsDataMap["<x>_<y>"].reputation`.
- Settlement names come from `factionsDataMap.<faction>.villages{name:{x,y}}`.
- Velmir's values:

| Settlement | Location key | Reputation |
|---|---|---|
| Osbrook | 32_10 | 4258 |
| Brynn | 26_33 | 2648 |
| Mannshire | 26_23 | 2552 |
| Denbrie | 14_37 | 2552 |
| Rotten Willow | 13_9 | 2500 |

- `reputationLogList` (history) is left untouched. *(my call)*

## 7. What we know: game install data (verified)

**`data.win`** is a GameMaker `FORM` container. It includes the chunks:
- `SPRT`: 17,248 sprites, 1,768 of them named `s_inv_*`;
- `TPAG`: texture-page placements;
- `TXTR`: 149 pages, stored as `2zoq`, i.e. a BZip2-compressed GameMaker-variant QOI;
- `OBJT`: 9,663 objects, 1,005 of them `o_inv_*`. Each object has a sprite index.

Icon extraction is proven. `docs/research/extract_icons_spike.py` is a throwaway spike, but it documents the QOI variant, the chunk offsets and the sprite-record layout.

**Icon lookup:**
- **Inventory objects:** use the object's `OBJT` sprite, because names don't always match (`o_inv_bandage` → `s_inv_bondage`, `o_inv_aqua_vitae` → `s_inv_blessed_water`).
- **Generated gear:** the display name lowercased with spaces removed gives `s_inv_<name>` (`Guard Crossbow` → `s_inv_guardcrossbow`). Some don't match exactly; for example "Road Guard Brigandine" has no `s_inv_roadguardbrigandine`.
- **Skills:**
  - Actives: `o_skill_X_ico` → `s_skills_X`.
  - Passives: `o_pass_skill_X` → `s_passive_X`.
  - Look these up through `OBJT` too, since typos exist (`s_skills_inceneration`).
- **Portraits:** `s_<Name>` (`s_Velmir`, 37×50).
- **Treatises:** items `o_inv_treatise_<tree><n>` (Pyromancy has 4 volumes, Axes 3, and so on).

**`StoneShard.exe`** (YYC) contains the game's data tables as semicolon-delimited text:
- **Item stats**, for example:
  `Guard Crossbow;2;crossbow14;crossbow;heavycrossbow;;Common;wood;650;1;40;8;;25;;20;;;23;…;-55;…;aldor;…`
  This is the source for the full item catalog and for building correct props for new items.
- **Localized names** in about 12 languages, and descriptions.
- Column headers and the exact layout have not been mapped yet.

## 8. The product (owner-approved screens)

The approved mockups are in `docs/superpowers/specs/mockups/`. Their images came from the game and aren't committed, so the pages render without icons; the layouts still read clearly.

### 8.1 Launch: choose a save (`app-shell.html`, top)

- The editor scans the saves folder and shows each character with:
  - their game portrait, name, class and level;
  - their slots, each with the `preview.png` screenshot, slot name, location and date/time.
- One click opens a slot.
- If Stoneshard is running, a banner warns about it and **Save to game** is disabled. *(my call: block, not just warn)*

### 8.2 Editor frame (`app-shell.html`, bottom)

- **Header:**
  - the portrait, the name, and the slot with its location;
  - tabs **Character · Inventory · Skills · Reputation**;
  - an unsaved-changes counter, **Undo all**, and **Save to game**.
- **Save to game** does three things in order:
  1. zips the slot folder as a backup;
  2. writes `data.sav` with the new checksum;
  3. reports success.
- A **Backups** view lists the backups for the open slot and can restore one. A restore also takes a fresh backup of the current state first.
- **Backup location:** `%LOCALAPPDATA%\StoneShardSaveEditor\backups\character_N\<slot>\<timestamp>.zip`. That is outside the game's folder, so neither the game nor Steam Cloud sees it. *(my call)*

### 8.3 Character tab

- **Attributes** with − / + steppers: STR, AGL, PRC, VIT, WIL.
- **Progress:** Level, XP, Stat points, Ability points, and a Gold shortcut.
- **Condition:** HP, MP, Sanity, Morale, Hunger, Thirst, Fatigue, Intoxication, Pain.
- Changing the level does not grant points. The user sets points directly. *(my call)*

### 8.4 Inventory tab: layout "A", like the game (`inventory-layout.html`, option A, chosen by the owner)

- **Left: equipment slots.** Head, neck, back, body, hands, belt, two rings, feet, and both weapon sets. Each shows its icon and a durability bar.
- **Middle: the backpack** as the game's 10-column grid.
  - Every item sits at its real cell with its real footprint and icon.
  - A badge shows the stack or charges (gold 2864, salve 3).
- **Right: the edit panel** for the selected item:
  - Gold for the moneybag;
  - Charges for consumables;
  - Durability, current and max, for gear;
  - **Duplicate** and **Delete** buttons.
  - Duplicate places the copy in the first free gap.
- **+ Add item** opens a searchable catalog of every item in the game, with icons.
  - Picking an item places it in the first gap that fits, with the game's real stats for that item.
  - If nothing fits, the editor says so and adds nothing. Items never overlap. *(my call)*
- Treatises are in the catalog, so a tree can also be opened the in-game way.

### 8.5 Skills tab (`skills-layout.html`)

- **Top bar:**
  - editable Ability points and Stat points;
  - **Respec all**: unlearns every non-innate skill, refunds 1 point each, removes those skills from the hotbar, and re-locks what depended on them;
  - **Refund this tree**;
  - **Follow game rules**, on by default. *(my call)* When it's on, learning costs 1 point and needs the prerequisites. When it's off, any skill can be toggled.
- **Left: tree list** with a learned-count per tree and 🔒 on closed trees.
- **Main area:** the selected tree, arranged as in the game, with each skill's icon in one of three states:
  - **learned** (gold): click to unlearn and refund;
  - **available** (green): click to learn;
  - **locked** (dimmed).
- **Unlock tree** on a closed tree clears `locked` on that tree's first tier. That is the same end state as reading its treatise.

### 8.6 Reputation tab

- One row per settlement: the name, a bar, and an editable number.
- The game's reputation tier cutoffs aren't known. Values are shown as plain numbers.

## 9. How it's built: decided parts

- **.NET backend + React frontend** (owner).
- **A single self-contained `.exe`.** It hosts the built React app in a native window through WebView2 (Photino.NET, or WinForms plus WebView2, at the builder's choice). It needs no .NET or Node on the user's machine. *(my call on the WebView2 approach; the owner approved the "double-click, own window" experience)*
- **Game data is read from the install at runtime.**
  - Icons, the item catalog and skill data come from `data.win` and `StoneShard.exe`.
  - Nothing from the game ships in the editor or the repo.
  - Extracted icons and catalog may be cached under `%LOCALAPPDATA%\StoneShardSaveEditor\cache\`, keyed to the game files' size and timestamp, so later launches are fast. The first launch shows progress while it builds the cache.
- **Development workflow** (owner's standing preference):
  - all work happens on a feature branch;
  - the owner tests locally before anything is pushed.
- **Distribution:** for the owner's personal use. The `.exe` is built on the owner's machine. A locally built file carries no download mark, so Windows SmartScreen doesn't block it. There's no public release, release pipeline or code signing in v1. *(my call)* Publishing it later would be a new request: it would add robustness work for other machines, languages and game versions.

## 10. Risks and unknowns to settle during the build

Every item below is verified **in the real game**: edit the save, load it in Stoneshard, check the result.

**How in-game checks work** (agreed with the owner):
- The builder creates a **test character**: a copy of Velmir's `exitsave_1` in a new `character_N` folder (the next free number). Every file is re-signed with that folder's salt (§6.2), and `character.map` is included.
- The builder writes experiment saves only into that test character.
- **The owner** loads them in Stoneshard when asked and reports what they see. Give them a short numbered checklist per session.
- The owner's real characters (`character_1` Jorgrim, `character_2` Velmir) are never written to by the builder. That holds until the checks pass and the owner uses the finished editor themselves.
- Steam Cloud will sync the test character. That's accepted, and it can be deleted when the checks are done.
- Batch the experiments so the owner needs as few in-game sessions as possible. Work that doesn't depend on an answer (the reader and writer, the UI, icons) proceeds in parallel.

1. **Item props for new items.**
   - Map the `StoneShard.exe` table columns to `props` keys, using existing items as ground truth. For example, the Guard Crossbow row should map to the Guard Crossbow props in Velmir's save.
   - **Consumables and misc items** carry small, generic props (`Material`, `idName`, `Duration`, `MaxDuration`, `charge`, `identified`, `tags`, …). Build those from the object name plus whatever the tables give.
   - **Fallback:** clone props from an instance of the same item found anywhere in the save. Merchant `trade_list`s in `locationsDataMap.*.npc_data` hold many complete items.
   - **If an item's props can't be built reliably by either route,** show it in the catalog, greyed out, with a short reason. Never write a guessed item into a save, and don't ship a hand-made props file. *(my call)*
2. **Skill tree structure:** membership, first tier, prerequisites and layout positions.
   - Find the source in game data. The exe's text tables are the first place to look.
   - If the structure can only be derived by hand, a small skill-tree definition file built from game data for the current game version is acceptable. It contains names and positions only, no art.
   - **Sources for a hand-built file, in order:** game data, then screenshots of each tree that the owner supplies (one per tree).
   - **Layout:** mirror the game's arrangement when positions are available. Otherwise, tier rows with prerequisite lines are acceptable. *(my call)*
   - "Follow game rules" and re-locking dependents ship only once prerequisites are confirmed. Until then, the editor offers only refund and learn with no prerequisite logic, and **Unlock tree**. *(my call)*
3. **Does the game recompute skill `locked` flags on load?** If it does, "Follow game rules: off" may be partly overridden. Document whatever behavior is observed.
4. **Unknown fields:**
   - inventory `f4`, `f7` and `f8`;
   - skill entry fields 2 and 4;
   - the bag's row count;
   - `AP` = stat points.
5. **Side effects of passives:** whether unlearning a passive in the save leaves leftover bonuses (check `characterDataMap.buffs`).
6. **Clamping:** whether HP or MP above the maximum, or condition values outside 0–100, are clamped by the game or cause problems. If they cause problems, the UI enforces limits.
7. **Steam Cloud:** confirm an edited save survives a game launch and isn't replaced by the cloud copy.
8. **Game updates:** if `wipeVersion` isn't `0.9`, or `data.win` doesn't parse, show a clear message and open read-only. Never write in that state.

## 11. Success looks like

- Double-click the `.exe` and see your characters with portraits and screenshots. The first launch may show a short "reading game files" progress bar.
- Open a save, then:
  - set gold;
  - add a weapon from the catalog;
  - change a salve's charges and a helmet's durability;
  - respec skills and open the Pyromancy tree;
  - raise Osbrook's reputation;
  - save.
- Load that save in Stoneshard: every change is there, nothing else changed, and the game loads it without complaint.
- Opening a save and saving it with no edits gives a save that plays identically.

## 12. Left to the builder

- Project and solution layout, libraries, and the choice of WebView2 host.
- API shape between the .NET host and React, state management, and component design.
- Styling details beyond the mockups, keeping the dark, game-like look.
- Cache format.
- Test strategy.
  - Automated tests run on copies of the owner's saves stored outside the game folder. Golden round-trip tests are strongly suggested.
  - The only writes into the live saves folder go to the test character (§10).
- Whether to use a JSON library that preserves raw number text, or a custom pass-through. The requirement is in §6.2.
