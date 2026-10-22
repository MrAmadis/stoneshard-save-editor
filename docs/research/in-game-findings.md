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

### Still open (next in-game session)

- Pyromancy treatise volumes II–IV: do they unlock more of the tree?
- Steam Cloud: does an edited save survive a full Steam restart?
- Gold coin stacks and ammo counts added or changed by the editor.
- **Unlock tree** done by the editor (flags + `Books_Read`) behaves like reading the book.
