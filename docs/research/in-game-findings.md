# In-game findings

Results of loading editor-made saves in Stoneshard (game version with save format `wipeVersion 0.9`).
Test characters were copies of Velmir (`character_2/exitsave_1`); the owner's real characters were never written.

## Session A — 2026-10-22

| Experiment | Observation (owner + save snapshots) | Decision |
|---|---|---|
| A0 Osbrook: untouched copy | Loads and plays exactly like Velmir. | The re-signed, byte-identical copy is accepted. |
| A1 Brynn: STR 18 | Shows 18. | Attribute edits work. |
| A1 Brynn: stat points `AP` 3, ability points `SP` 4, level 6, XP 2000 | Game showed **4** stat and **5** ability points, both spendable. | Mapping confirmed: `AP` = stat points, `SP` = ability points. The extra +1 each most likely comes from the game levelling up on load from the XP set; the save was consumed (see below), so not verified. UI note: "changing XP or level can trigger level-ups in the game". |
| A1 Brynn: HP 999 | Shows 100. | The game caps health at the character's maximum. Keep 0–9,999 limit; UI hint that the game caps it. |
| A1 Brynn: Sanity 150 (outside limits on purpose) | Shows **150**, not clamped. | Keep the editor's 0–100 limit so players can't create states the game never produces. |
| A1 Brynn: Hunger 0, Osbrook reputation 9,000, gold 99,999 | All shown as set. | Edits work. |
| A1 Brynn: gold | Owner would prefer gold as coin items ("stacks of 50") rather than one purse total; adding it as an item is fine. | Gold coins are `o_inv_gold` items whose entry value [6] is the coin count (game max 100 per stack, `stack` column). Add "Gold coins" to the catalog with an editable count; the same entry value [6] is the stack size of ammo (Velmir's bolts: 19), so ammo stacks become editable too. |
| A2 Mannshire: charges, waterskin sips, helmet durability, deletions, duplicate | All as set. | Inventory edits work. |
| A2 Mannshire: added Noble Longsword, Copper Ring, Antitoxin, Pyromancy Treatise I | Present in the bag; looked normal. The game kept them in their cells after saving. | Table-built gear and items and the in-bag entry form are accepted. |
| A2 Mannshire: probe salves at cells 50–90 (rows 5–9) | All five were pushed out of rows 5–9 ("salves dropped") and ended up in normal cells; nothing survived at row ≥ 5. | **Bag is 10 × 5.** `InventoryEditor.BagRows = 5` confirmed. |
| A3 Denbrie: Hooking Chop unlearned, Fatal Strike unlocked only, Killing Swing learned directly | Hooking Chop unlearned. Fatal Strike was **not** available until Hooking Chop was learned again: "all three need to be learned". Killing Swing ("Make Space" in English, last tier) showed as learned. | The game recomputes availability (locks) inside open trees from prerequisites when loading; learning a deep skill directly is accepted. In the Greataxes tree, the second tier needs all three first-tier skills learned. "Follow game rules" models this from `skill-trees.json`. |
| A4 RottenWillow: read Volume I of the 11 trees closed on Velmir | 39 skills unlocked; the books were added to `Books_Read` (`treatise_pyro1`, …). Three Survival skills also unlocked without a book: the game recalculated that open tree on load. | `tree-unlocks.json` holds the 36 first-tier skills of the 11 books read. **Unlock tree** = clear `locked` on those skills **and** add `treatise_<tree>1` to `Books_Read`, exactly as reading the book does. Extractor credits only books that newly appear in `Books_Read`. |
| A4 screenshots | 22 screenshots in `C:\Program Files (x86)\Steam\userdata\43900557\760\remote\625960\screenshots` (2026-10-22 16:54). | Source for `skill-trees.json` (Task 13). |
| A5 Brynn_NW: Pyromancy I–IV | Not found by the owner; possibly loaded by accident. | Redo in the next session. |
| Exit saves | Test characters 4 (Brynn), 6 (Denbrie) and 8 (Brynn_NW) **disappeared**; 3, 5 and 7, which the game re-saved on quitting, remain. | Stoneshard consumes an exit save when it is loaded and writes a new one only on a proper quit; a character whose only save was consumed is removed. Test characters should get a manual-save slot too, and the checklist must say "quit to the main menu". The editor itself is unaffected. |
| Steam Cloud | Not reported yet. | Check in the next session. |

## Session B — 2026-10-22

Built with `Lab make-session-b`; saves captured by the Lab watcher in `tests/fixtures/experiments/session-b/`.

| Experiment | Observation (owner + save snapshots) | Decision |
|---|---|---|
| B1 Brynn: every tree opened and every skill learned | 22 screenshots (`…\screenshots61022205947_1.jpg` to `…210021_1.jpg`) show every icon and the lines between them. Basic Skills shows its 4 known skills over 12 padlocked slots that are not in the save; Survival's top row also shows Butchering. | Transcribed into `skill-trees.json` (21 trees, 227 skills): row, order in the row, and requirements. A shared bar means every skill above it is needed (as A3 showed for Greataxes); every line into a skill is read as "needed". Butchering (innate, filed under Basic) is left out of Survival's requirements; Basic has no layout. |
| B2 Denbrie: gold coin stacks 100, 50, 7 in the bag; equipped bolts 5 | Bolts showed 5. The gold total first showed just over 3000 (purse 2864 + 157 coins). After opening the purse it showed **157**: the purse was empty and the coins were loose in the inventory. | Ammo counts work. A purse's gold is the coin stacks **inside it** (its `lootList`, stacks of up to 100); its `Stack` is only a cached total the game recomputes when the purse is opened. Velmir's purse claims 2864 with no coins inside (since his `save_2` of 2026-10-21, before this editor existed; `autosave_1` still had 136 as coins 100 + 36). Editing gold must write coins into the purse, not just `Stack`. |
| B3 Brynn_NW: Pyromancy opened by the editor, Treatises II–IV read | The tree opened with its first skills available without reading a book; II, III and IV each unlocked one row. | **Unlock tree** works. Volume II → Scorch, Feed the Flames, Magma Rain, Flame Wave; III → From Blaze to Furnace (`o_pass_skill_safe_distance`), Melting Ray, Incineration; IV → Pyromania, Excess Heat, Inferno. "Unlock tree" opens what Volume I opens. |
| Exit saves | Characters 8 and 9 disappeared again after loading; 10 was re-saved on each quit. | As in session A: loading an exit save consumes it. |

## Session C — 2026-10-22

A purse is a 5 × 4 grid of 20 slots (owner's screenshot), shared by gold coins and valuables such as gems.
Built with `Lab make-session-c`; saves captured in `tests/fixtures/experiments/session-c/`.

| Experiment | Observation (owner + save snapshots) | Decision |
|---|---|---|
| C1 Osbrook: Velmir's empty purse filled by the editor with 1,234 gold (13 stacks, cells down the first column first) | Looks good. The game re-saved the purse with the same 13 stacks and `Stack` 1234; it only reordered them and set each coin's `i_index` to 6 (5 for the 34 stack). | Gold as coin stacks in the purse's `lootList`, with `Stack` = their sum, is accepted. |
| C2 Mannshire: 4,500 gold as three purses | Looks good. Velmir's purse held a morion (a gem, slot 4) since the owner's 21:38 play, so the editor filled its other 19 slots (1,900) and added purses of 2,000 and 600; the game kept all three unchanged (coins reordered, `i_index` 6). | Several purses work, and valuables keep their slots. |
| Character list | Characters `character_11` and `character_12` did not appear in the game's list; rebuilt as `character_3`/`character_4`, they did. | Probably the game lists only `character_1`–`character_10`; the Lab should reuse free low numbers. |
| Steam Cloud | Test characters deleted while the game was closed came back on the next launch (21:50: 5, 7, 10, 11, 12). | Steam Cloud restores files missing locally on launch; delete test characters while the game is running, or in its menu. |

## Final acceptance — 2026-10-22

The owner edited a test character (`character_3`) in the single-file exe and loaded it in the game.

| Check | Observation | Decision |
|---|---|---|
| Walkthrough: gold, catalog weapon, charges, durability, respec and learn, unlock Pyromancy, reputation, Save to game | "Seems to work." | Accepted. |
| Reputation | The game's maximum is 6000; the editor allowed up to 100,000 and scaled its bars to 5000. | Editor maximum and bars set to 6000. |
| Salve charges | The editor allowed 6; the game's maximum is 3. | Charges capped per item at the most any copy has anywhere in the save (traders' stock and loot hold full copies). |
| Velmir's exit save | Gone since 22:04 (loading an exit save consumes it). | Left as it is, at the owner's request; his other saves are intact. |

### Still open

- Steam Cloud: does an edited save survive a full Steam restart? (No cloud prompt reported.)
