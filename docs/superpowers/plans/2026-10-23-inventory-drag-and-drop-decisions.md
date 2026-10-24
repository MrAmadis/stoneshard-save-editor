# Decisions made while building inventory drag and drop

Plan: `2026-10-23-inventory-drag-and-drop.md`. Each ruling says what was decided, why, and what it costs if wrong.

## Rulings

- Task 1: the Voulge's kind is `2haxe` (the plan guessed `spear`); the game's weapons table is the authority. Cost if wrong: none.
- Task 3: a save with two items in one slot no longer crashes a move (the worn-items lookup keeps the first). Cost if wrong: none.
- Task 4: session G2 has no wand, because the catalog can't build wands; a sickle took its place. Cost if wrong: wands stay unwearable in the editor until a wand the owner has is checked.
- Task 4: G2's items are added biggest first, so all nine fit the 10 × 5 bag. Cost if wrong: none.
- Task 5: G1 empties the bag except one purse and one torch before moving gear, because the live Velmir's bag was too full. The failed runs left extra test characters, which were deleted (never loaded, made with the game closed). Cost if wrong: none.
- Task 5: G2 adds only what the catalog can make for the save (the live Velmir has no sling bullets to copy). Cost if wrong: none.
- Task 5: the owner's G2 report gave the slots. Slings go in either hand, with stones, bullets or metal shot in the other; lutes, chains and pickaxes are two-handed; the sickle (a tool from the weapons table) and the torch go in either hand; other tools from the items table aren't worn; wands are still refused. Cost if wrong: one of those kinds is refused or allowed against the game.
- Task 7: the removal test fills the bag with 1 × 1 copies (torches left room in the last two rows). Cost if wrong: none.
- Final: the active set's left hand is written `f7` 2, as the game does for an off-hand weapon and for sling stones. Cost if wrong: none seen; G1's bolts with `true` loaded too.
- Final: a move that would leave ammunition without its weapon is refused ("Take the ammunition off first"), not done with the ammunition taken off as well. Whether the game allows that state is unknown. Cost if wrong: one extra drag.
- Final: a two-handed item dropped on either hand goes in that set's right hand (owner, for the lute, chain and pickaxe), applied to every two-handed kind. Cost if wrong: none for the reported kinds.
- Moved repo: the owner moved the repo to `D:\Projects`. A shell test that checked "the save opened" before the click was handled failed under the full suite there; it now waits for it (test only).

## Deferred minor findings (final review)

- An item worn in a non-hand slot with weapon set 1 (never seen in a save) would show under "other", and a drop on its slot would not replace it.
- The "had no room and was removed" banner stays after Undo all or Reload, until the next move.
- The landing outline stays in the bag while the pointer is over the equipment slots.
- Dropping an item on its own place still runs a full edit; in a read-only save the slots light up although no drop can succeed.
- Each render during a drag recomputes every slot's answer (up to about 18 ms with charged items); the slot answers could be worked out once when the drag starts.
- Tests: no UI test for a drag ending after hovering the bag; no test opens a real autosave with `weaponSet` `false`.
- Core accepts weapon sets other than 0 and 1 (the UI never sends them).
- The mockup's dashed outline on the dragged item's old spot isn't drawn.
- Not checked in-game: ammunition equipped from a fresh stack keeps `f4` 0 (worn bolts show 5), and a shield in a hand.
