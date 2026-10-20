# AGENTS.md

Agent-facing rules for this Unity project. Students keep using their normal AI Agent; this file tells that Agent how to leave course evidence. Students do not need to open this file as homework.

## Read first

- `STATUS.md`: where the project stands.
- `DEVLOG.md`: append-only history. Do not edit old sections. Do not delete the sample sections.

## After gameplay code changes

1. Keep `@role`, `@calls`, and `@tune` at the top of each gameplay script aligned with real behavior. Format:

```
// @role …
// @calls OtherScript
// @tune name:type:min-max:note
```

2. In Unity, run `CGDD/Export Trace Docs` so `Assets/CGDD/Editor/TraceDocsExporter.cs` refreshes:
   - `docs/script-flow.mmd`
   - `docs/tunables.md`
   - matching copies under `site/data/`, plus `challenge-map.md`, `DEVLOG.md`, and `bundle.js`
3. If challenge feel changed, ask first whether win/loss changed and whether the player verb changed. Write the answers into `docs/challenge-map.md`.
4. **Automatically** append one Interaction section at the end of `DEVLOG.md` when you change gameplay. Do not overwrite history. Log only the game itself.
5. **Stage Reflection only when the student asks** for one (Keep / Change / Cut, and which AI suggestions they accepted or rejected). Do not invent a Stage Reflection on your own. Keep that reflection about the game.

## Do not

- Do not log site language, diagram styling, or click-to-read-script into an Interaction or Stage Reflection when those changes do not alter the game.
- Do not add oral reports or defense slides.
- Do not use GraphView. That is for a later stage.
- Spawn intervals, damage, timers, and similar numbers follow the script field defaults.
