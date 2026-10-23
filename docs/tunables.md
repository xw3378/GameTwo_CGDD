# Tunables

Generated from `@tune` headers by the menu `CGDD/Export Trace Docs`. Hand edits are overwritten on the next export.

| Script | Field | Type | Range | Default | Note |
| --- | --- | --- | --- | --- | --- |
| AudioManager | HearRange | float | 5-60 | 20 | max audible distance (m) for cat/guard footsteps |
| CatCheeseGame | eatRadius | float | 0.3-3 | 0.9 | cheese pickup radius m |
| CatCheeseGame | catchRadius | float | 0.5-5 | 1.2 | cat catch radius m |
| CatCheeseGame | CatSpeed | float | 0.3-6 | 1.6 | cat patrol speed m/s |
| NbFridge | Layers | int | 2-12 | 6 | number of shelves |
| NbFridge | LayerH | float | 0.2-2 | 0.56 | shelf spacing (floor-to-floor) m |
| NbFridge | StepsPerLayer | int | 1-10 | 4 | stair steps between shelves |
| NbGuard | Speed | float | 0.3-3 | 0.95 | patrol walk speed m/s |
| NbGuard | Sweep | float | 0.1-3 | 0.55 | flashlight sweep pace (higher = faster) |
| NbGuard | Range | float | 5-40 | 18 | flashlight beam range m |
| NbGuard | Half | float | 2-45 | 11 | beam cone half-angle deg |
| NbGuard | Damage | float | 1-100 | 25 | exposure gained per second in the beam |
| NbGuard | ChaseSpeed | float | 1-6 | 2.9 | chase speed m/s (below player run) |
| NbKitchen | BenchW | float | 1-10 | 4.8 | bench width m |
| NbKitchen | AisleW | float | 0.3-3 | 1.2 | aisle width m |
| NbLevels | BiteDamage | int | 1-50 | 10 | health lost per catch |
| NbLevels | StartHp | int | 10-500 | 100 | starting health |
| NbLevels | Count | int | 1-20 | 10 | levels per scene |
| NbLighting | KeyPower | float | 0-1.5 | 0.62 | moon key light intensity |
| NbLighting | FillPower | float | 0-0.5 | 0.07 | fill light intensity |
| NbMode | DoorWarnTime | float | 1-6 | 3 | fridge door-open warning seconds |
| NbMode | CatHeight | float | 0.5-3 | 0.95 | cat height m (feeds cat body probe scale) |
| NbPlayer | WalkSpeed | float | 1-4 | 2.3 | walk speed m/s |
| NbPlayer | RunMul | float | 1-3 | 1.7 | run multiplier on WalkSpeed |
| NbPlayer | JumpSpeed | float | 1-6 | 3.2 | jump take-off velocity m/s |
| NbPlayer | Gravity | float | -40--5 | -20 | gravity acceleration m/s2 |
| NbPlayer | LookSens | float | 0.02-0.5 | 0.1 | mouse look sensitivity deg/pixel |

