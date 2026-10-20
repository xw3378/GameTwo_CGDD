# Site

One static page with three blocks:

1. **Framework**: script call graph, tunables, challenge table
2. **Process**: `DEVLOG.md` in time order
3. **Playable**: recording placeholder until a video is added

Data lives in `site/data/bundle.js`. The Unity menu `CGDD/Export Trace Docs` writes both `docs/` and this folder. Hand edits to `docs/script-flow.mmd` or `docs/tunables.md` are overwritten on the next export.

## Open locally

In Unity: **CGDD → Open Student Site** (opens `site/index.html` in your browser).

Or double-click `site/index.html`. The flowchart loads Mermaid from a CDN, so you need internet the first time. A local HTTP server is optional, not required.

## Add a real recording

Put an mp4 at `site/media/play.mp4` and refresh.
