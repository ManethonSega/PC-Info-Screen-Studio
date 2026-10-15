# UI refinement candidate, alpha.21

The four selected design skills were applied to the actual .NET 10 WPF source and existing alpha.13 Windows captures. Decisions and framework sources are recorded in [UI_DESIGN.md](../UI_DESIGN.md).

## Completed local checks

- All application XAML files parse as XML, including the updated templates and resource references.
- The 12 existing repository-maintenance safeguards pass.
- `git diff --check` passes.
- Source comparison checks preserve the existing commands and click/change handlers, named controls, numeric-validation annotations, palette definitions and serialized theme model.

These checks do not establish WPF compilation or rendered layout. Impeccable's native context loader ran successfully but did not identify WPF as an automatically detectable visual platform; no web detector is reported as a XAML audit.

## Windows checks and capture review

[Windows run 38727780723](https://github.com/ManethonSega/PC-Info-Screen-Studio/actions/runs/38727780723) passed for candidate head `1095ab4e348591a664b98fd1869e39b2620db769` (PR merge checkout `2b24cfc3ad7f9126c3d6256e9d5c06189ffe5abc`). The build reported zero warnings and errors. Controller/WPF checks, native captures, published EXE startup and extracted portable-ZIP startup all passed. Added WPF checks cover toolbar containment/overlap at 1560 and 1080 window widths, rendered widget type labels, and actual UI Automation names for visible fields and colour actions.

The wide and compact Info Screen, Photo Frame and Hybrid captures, plus Hardware, Settings and control states, were visually reviewed. Compact canvas cropping at 100% zoom remains intentional; scrolling and zoom are available. The canvas tools wrap without overlapping and inspector/layer wording is readable. Sample hardware values and empty photo playlists are illustrative, not physical-device evidence.

Capture review found the renderer's empty-photo text visible between Hybrid's two editor hints. A follow-up gives those editor-only hints one opaque shared background without changing their existing visibility conditions or the device renderer. The [draft PR's checks](https://github.com/ManethonSega/PC-Info-Screen-Studio/pull/32/checks) record the follow-up revision's Windows results; the initial run above is not presented as evidence for a later source change.

The owner explicitly approved pushing the candidate and opening draft [PR #32](https://github.com/ManethonSega/PC-Info-Screen-Studio/pull/32) on 15 October 2026. The PR stays a draft for review.

Interactive Windows DPI, Narrator and high-contrast qualification remain separate. Physical display brightness, transport, media playback and renderer paths are outside this UI change.
