# DEVLOG

Only append. Do not edit old sections. Two samples stay so students can see the format.

Only changes to the game itself are logged: the verb, win and loss, pressure, and what the player can feel.
Site language, diagram styling, and click-to-read-script do not go into an Interaction or a Stage Reflection.

Each gameplay change should add a section `## Interaction NNN — title` (the Agent can append this when it changes the game).
Add `## Stage Reflection NNN — title` only when you ask for one: Keep / Change / Cut, and which AI suggestions you accepted or rejected. That reflection stays on the game.

## Interaction 000 — EXAMPLE (sample, do not delete)

- Date: —
- Who asked: teacher (sample)
- Request: Tighten one tunable and see if it is still the same game.
- What changed: Sample only. The current code was not changed. A real change would touch only one `@tune` field.
- Result: The player's verb did not change. Win and loss did not change. Pressure changed, not the skeleton.

## Stage Reflection 000 — EXAMPLE (sample, do not delete)

### Keep
- A core verb you can say in one sentence, and a clear win/loss.

### Change
- To make it harder, turn an existing `@tune` first. Do not add a new system yet.

### Cut
- No shop, no deep inventory, no optimization that is off-lesson.

### AI
- Accept: tune feel with existing fields; avoid opening new scripts.
- Reject: GraphView, object pools, long hand-written scene YAML. This class is about script relationships and tunable numbers.

## Interaction 001 — brighten the trash piles

- Date: 2026-11-03
- Who asked: student
- Request: Make the trash colors bright and vivid enough to contrast against the dark alley, but still dirty and rundown.
- What changed: Raised the `NbRubbish` palette (bags, cardboard, wood, skip, tarp, cans, glass, paper) to brighter, more saturated tones while keeping the earthy brown/grey/green character. Left the deepest charcoal bag (BagA) and the burrow interior dark so the piles keep depth.
- Result: The player's verb did not change. Win and loss did not change. Only the readability of the refuse changed, not the skeleton.

## Interaction 002 — rainbow trash

- Date: 2026-11-03
- Who asked: student
- Request: Recolor the trash to be colorful/rainbow and very saturated, but still give a dirty vibe.
- What changed: Repainted the `NbRubbish` palette across the full rainbow (dirty red/orange/yellow/green/teal/blue, purple skip, blue tarp, blue glass) with high saturation but low-to-mid value, so it pops against the dark alley without glowing. Updated the skip lid and burrow-lining comments to match.
- Result: The player's verb did not change. Win and loss did not change. Only the color of the refuse changed, not the skeleton.

## Interaction 003 — darker, more saturated trash

- Date: 2026-11-03
- Who asked: student
- Request: Make the rainbow trash darker and more saturated still.
- What changed: Dropped the `NbRubbish` palette values a step and pushed each hue's chroma up, so the rainbow reads deeper and grimier without glowing. Adjusted the skip lid to match.
- Result: The player's verb did not change. Win and loss did not change. Only the color of the refuse changed, not the skeleton.

## Interaction 004 — darker trash, orange guards, rainbow fridge stock

- Date: 2026-11-03
- Who asked: student
- Request: Darken the trash further; recolor the human guards saturated orange; make the fridge food as colorful as the trash.
- What changed: Dropped the `NbRubbish` palette another step (same hues, higher value). Repainted the guard uniform saturated orange in `NbGuardArt.Paint()` and the geometry fallback in `NbGuard` (boots/hair stay dark). Tinted each food texture's material with a saturated rainbow hue in `NbFoodArt` (deterministic per texture).
- Result: The player's verb did not change. Win and loss did not change. Guards are more visible and the fridge reads more colorful, but the rules are unchanged.

## Interaction 005 — colorful dark yard landmarks

- Date: 2026-11-03
- Who asked: student
- Request: Make the backyard's poles, clothes, door, and shed colorful and dark, like the trash.
- What changed: In `NbYard`, recolored the clothesline posts (dark violet-blue), the hanging clothes (dark red/green/orange cycling), the back door (dark purple), and the shed body + roof (dark teal / dark red). Left the woodpile, doghouse, walls, and other unrelated props unchanged.
- Result: The player's verb did not change. Win and loss did not change. Landmarks read as distinct colored shapes in the dark, but the rules are unchanged.

## Interaction 006 — woodpile, doghouse, turquoise walls

- Date: 2026-11-03
- Who asked: student
- Request: Recolor the woodpile and doghouse colorful/dark, and make the walls a deep turquoise.
- What changed: In `NbYard`, set the woodpile to dark orange with a dark yellow cap, the doghouse to dark green with a dark purple roof, and the brick walls to deep turquoise. Left wall caps/steps unchanged.
- Result: The player's verb did not change. Win and loss did not change. The yard's color layout changed, but the rules are unchanged.
