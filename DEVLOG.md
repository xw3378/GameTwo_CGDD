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

## Interaction 007 — danger glow at the screen edges

- Date: 2026-11-07
- Who asked: student
- Request: Add a visual indicator when an enemy (cat, or the patrol guards) gets too close, e.g. the edges of the screen get brighter.
- What changed: Added a full-screen vignette to the HUD whose edges brighten as the nearest cat or guard approaches. `NbGame` now folds the distance to the nearest enemy into a 0…1 `Threat` (via `ThreatNear`/`ThreatFull`), and `NbHud` drives the edge glow from it: a warm glow that turns red as the enemy gets very close. Cats and guards share the same signal; the fridge scene has neither so it stays off.
- Result: The player's verb did not change. Win and loss did not change. Only the readability of danger changed, not the skeleton.

## Interaction 008 — softer danger glow

- Date: 2026-11-07
- Who asked: student
- Request: The edge vignette is too harsh; make it less obvious and more transparent.
- What changed: Lowered the danger glow's peak alpha (0.55 → 0.22) and pulled the gradient's start radius further out (0.52 → 0.62) so the bright band hugs the screen edges instead of creeping toward the center.
- Result: The player's verb did not change. Win and loss did not change. Only the intensity of the warning readout changed, not the skeleton.

## Interaction 009 — white-to-red danger glow

- Date: 2026-11-07
- Who asked: student
- Request: Change the far-distance glow color from warm/yellow to white, so it reads white → red instead of warm → red.
- What changed: Replaced the `ThreatWarm` color with a pure white `ThreatFar` in `NbHud`; the edge glow now lerps white at distance to red up close.
- Result: The player's verb did not change. Win and loss did not change. Only the color of the warning readout changed, not the skeleton.

## Interaction 010 — smell mechanic + tighter vignette

- Date: 2026-11-07
- Who asked: student
- Request: Halve the vignette detection distance; rework the smell cloud to be a foggy, denser, lighter-yellow cloud that spawns in front of the viewer, waits 0.75 s, then slowly floats toward the cheese.
- What changed: Lowered `ThreatNear` (10 → 5 m) so the edge glow only appears when an enemy is much closer. Added `NbSmell` and an R-key smell power (`SmellCooldown` 5 s) that emits a dense, soft yellow cloud in front of the camera; the cloud holds ~0.75 s then drifts slowly toward the nearest uncollected cheese. Added the `R - SMELL` hint to the HUD.
- Result: The player's verb did not change. Win and loss did not change. A new optional "find the cheese" affordance was added, but the rules are unchanged.

## Interaction 011 — smaller, fainter smell particles

- Date: 2026-11-07
- Who asked: student
- Request: Make the smell particles smaller and slightly more transparent.
- What changed: In `NbSmell`, reduced particle size (0.30 → 0.20 m) and lowered the color-over-lifetime alpha (peak 0.42 → 0.30, plateau 0.32 → 0.22).
- Result: The player's verb did not change. Win and loss did not change. Only the look of the smell cloud changed, not the skeleton.

## Interaction 012 — remove minimap and backyard, trim to 3 levels

- Date: 2026-11-07
- Who asked: student
- Request: Remove the in-level minimap, remove the backyard district, and reduce the level count to 3 for both the residential block and the fridge.
- What changed: Stopped creating the minimap (`NbMap.Create` removed from `NbGame.Boot`, plus its scene-switch teardown). Removed the backyard from the scene-select page (`NbSceneSelect` now shows Street + Fridge, two cards). Set `NbLevels.Count` to 3 and trimmed the `Street` and `Fridge` tables to three levels each. Updated the title-screen and select-page copy accordingly.
- Result: The player's verb did not change. Win and loss did not change. Content and navigation changed (no minimap, two districts, three levels each), but the rules are unchanged.

## Interaction 013 — capsule-shaped smell cloud

- Date: 2026-11-07
- Who asked: student
- Request: Condense the smell particles so they are not a wall of particles at spawn; make a denser horizontal capsule pointing toward the cheese.
- What changed: In `NbSmell`, added a Box emission shape (0.35 × 0.18 × 1.6 m) whose long axis is rotated to the cheese's horizontal direction each sniff, so the cloud spawns as a flat, elongated capsule pointing at the cheese instead of a scattered blob.
- Result: The player's verb did not change. Win and loss did not change. Only the shape of the smell cloud changed, not the skeleton.

## Interaction 014 — teardrop smell trail

- Date: 2026-11-07
- Who asked: student
- Request: Change the smell spawn to a horizontal 3D teardrop, fat end toward the player, denser near the player and sparse toward the cheese, with the particles near the player staying static and most streaming toward the cheese.
- What changed: Reworked `NbSmell` to emit each particle via `EmitParams` with a teardrop-sampled position (circular cross-section tapering to a point) and a per-particle velocity: near-player particles stay put, the rest stream toward the cheese. Removed the box shape and the pause-then-drift velocity curve.
- Result: The player's verb did not change. Win and loss did not change. Only the shape and motion of the smell cloud changed, not the skeleton.
