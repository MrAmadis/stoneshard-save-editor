# Inventory drag and drop — Design

Date: 2026-10-23
Status: draft for review

## 1. Intent

The owner wants to move items in the editor's Inventory tab by dragging them, for two everyday jobs:

- **swap gear**: take one item off and put another on;
- **tidy the backpack**: rearrange items so something else fits.

Today the tab only shows where things are. Items can be selected, edited, duplicated, deleted and added, but not moved: the Equipped panel and the backpack are read-only for placement.

## 2. Goals

1. **Equip by dragging** an item from the backpack onto a slot on the character.
2. **Unequip by dragging** an equipped item into the backpack.
3. **Swap** by dropping an item on a slot that's already taken. The new item goes on and the old one comes off into the backpack.
4. **Rearrange** by dragging an item to another spot in the backpack.
5. **Move between slots** of the same kind: from one weapon set to the other, or from one ring to the other.
6. **Refuse what the game wouldn't allow** (owner). A refused drop leaves everything where it was.
7. **Show while dragging** where the item can go:
   - the slots it fits light up, and the others fade;
   - in the backpack, an outline of the item's whole shape shows where it would land, green if it fits and red if not.

   Mockups: [swap-helmet](mockups/inventory-drag-swap-helmet.html), [backpack-move](mockups/inventory-drag-backpack-move.html). The owner said both "look good".

## 3. Non-goals

- Moving things into or out of containers, such as coins and gems in a purse or arrows in a quiver.
- Merging stacks by dropping one onto another.
- Deleting by dragging out of the window. The Delete button stays for that.
- Swapping two items inside the backpack. Dropping onto an occupied spot is refused (owner: "if there is no room for it, it should go back where it was").
- A "follow game rules" switch like the Skills tab's. Refusing invalid drops is always on (owner: "It should refuse it").

## 4. Anti-goals

- A save the game rejects, or one that loads with gear the game doesn't treat as worn. Examples: armor that doesn't count, or a weapon that can't be used.
- Losing an item without saying so. The one agreed case is in §5.3, and the editor says when it happens.
- Breaking what works: clicking an item still selects it and shows its details; editing durability, charges and gold is unchanged.

## 5. Behaviour

### 5.1 Equipping

- Drag an item from the backpack onto a slot it fits. It goes on, and its backpack cell becomes free.
- Which slot an item fits comes from the game's item tables (`Slot` column):

  | Table `Slot` | Save slot |
  |---|---|
  | `Head` | `o_inv_head` |
  | `Chest` | `o_inv_armor` |
  | `Arms` | `o_inv_gloves` |
  | `Legs` | `o_inv_boots` |
  | `Waist` | `o_inv_belt` |
  | `Amulet` | `o_inv_neck` |
  | `Back` | `o_inv_back` |
  | `Ring` | `o_inv_ring_1` or `o_inv_ring_2` |
  | `shield` | `o_inv_left_hand` |
  | one-handed weapons (`sword`, `axe`, `mace`, `dagger`, …) | `o_inv_right_hand`, or `o_inv_left_hand` for dual wielding |
  | two-handed weapons (`2hsword`, `2haxe`, `2hmace`, `2hStaff`, `spear`, `bow`, `crossbow`) | `o_inv_right_hand` only |

  Both hands come in two weapon sets (save field `weaponSet` 0 and 1, shown as "weapon" and "set 2").
- The game makes room rather than refusing (session H): a two-handed item takes the other hand off (bolts or arrows it shoots stay), an item put beside a two-handed one takes that off, and ammunition comes off as soon as its weapon leaves the hand. What comes off goes into the bag, or is removed and named when there's no room. Ammunition needs a weapon that shoots it in the other hand.
- Settled by session G (in-game findings): slings go in either hand with stones, bullets or metal shot in the other; lutes, chains and pickaxes take both hands and can be dropped on either; the torch and the sickle take one hand, either. Wands are still unknown and refused.

### 5.2 Unequipping

- Drag an equipped item into the backpack and drop it on a free spot where it fits. It comes off.
- Dropping it where it doesn't fit is refused.

### 5.3 Swapping

- Dropping an item on a taken slot it fits puts the new item on and takes the old one off into the backpack.
- The old item first tries the spot the new one just left; it fits there exactly when it has the same size. Otherwise it goes to the first free spot.
- If there's no room at all, the old item is removed from the save (owner: "if there is none remove it"), and the editor says so in a banner: "Old helmet had no room and was removed".

### 5.4 Rearranging the backpack

- Drag an item to another spot. It moves there if its whole shape fits on free cells.
- The cells it is leaving count as free while it moves, so it can shift by one cell.
- Otherwise the drop is refused and the item stays where it was.

### 5.5 Moving between slots

- An item can be dragged from one weapon set to the same hand of the other, or from one ring slot to the other, under the same rules.
- If the target is taken, the two items trade places when each fits the other's place. Otherwise the swap rule in §5.3 applies.

### 5.6 Selection

- A click without dragging still selects the item, as now.
- After a move, the moved item is selected and the detail panel follows it.

## 6. Constraints

- **Save format:** one entry in `inventoryDataList` per item, as `[name, props, f2, cell, f4, chargesMirror, count, f7, weaponSet, slot]` (see `InventoryEditor`). From Velmir's save:

  | | `f2` | `cell` | `f4` | `chargesMirror` | `count` | `f7` | `weaponSet` | `slot` |
  |---|---|---|---|---|---|---|---|---|
  | in the backpack | 0 | `y*10+x` | 0 | 1 | -4, or the stack size | 0 | 0 | `N/A` |
  | equipped, set 0 | -4 | -4 | 0 | -4 | -4 | true | 0 | e.g. `o_inv_head` |
  | equipped, set 1 (crossbow) | -4 | -4 | 0 | -4 | -4 | 0 | 1 | `o_inv_right_hand` |
  | equipped bolts, set 1 | -4 | -4 | 5 | 1 | 19 | 0 | 1 | `o_inv_left_hand` |

  `f7` looks like "worn in the active set". `f4` on the bolts is unexplained. The values to write come from these observations, confirmed in-game (§7). A move changes only those fields; the item's `props` (stats, durability, charges) are left alone.
- **Backpack:** 10 × 5 cells; an item's size comes from its sprite (`IItemShapeProvider.Footprint`). Cell occupancy already exists in `BagGrid`.
- **Stats:** the save's `characterDataMap` holds base attributes and some `b…` bonus fields, but no armor totals. So the game most likely works out gear bonuses when it loads; the in-game check confirms this.
- **Stack:** Blazor Hybrid (WebView2). The UI is tested with bUnit, Core with xunit, and all rules live in Core so they can be tested without the UI.
- **Owner rules:** never write to `character_1` or `character_2` from tests or dev runs; test characters come from the Lab, written as `save_1`. Game files are read-only.

## 7. First step: an in-game check

Before building the UI, find out whether the game accepts gear moved by the editor. This is a Lab test character, as in sessions A–F.

- **On the test character, the Lab:**
  - takes the helmet off into the backpack;
  - puts a different helmet on;
  - moves a one-handed weapon from set 0 to set 1;
  - moves a ring from slot 1 to slot 2;
  - rearranges two backpack items.
- **The owner checks in the game that:**
  - it loads;
  - the moved gear shows where expected;
  - armor and stats in the character screen match the worn gear;
  - weapons in both sets can be used;
  - nothing doubles or disappears.
- **The open categories in §5.1** get checked the same way.

If the game doesn't recompute stats, for example armor staying at the old helmet's value, the design comes back for a decision before any UI is built.

## 8. Left to the builder

- How the drag is done in WebView2, such as HTML5 drag and drop or pointer events with a small script, and how it looks beyond the mockups.
- Splitting the "rings" box into two drop targets, one per ring slot, and each weapon-set box into a right-hand and a left-hand target (my call), so a drop picks the ring or the hand.
- What `f4` and `f7` need on each move, from the in-game check.
- Edits go through `EditorService.Edit` like every other edit, so a failed move rolls back.

## 9. My calls (not asked)

- Moving between weapon sets and ring slots (§5.5), with items trading places when each fits the other's place.
- The old item goes to the new item's old spot first (§5.3).
- A banner when an item is removed for lack of room, rather than asking first.
- Two drop targets for the rings, and two (right and left hand) for each weapon set.
- The in-game check comes before any UI work.
