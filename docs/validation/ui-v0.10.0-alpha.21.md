# UI refinement candidate, alpha.21

The four selected design skills were applied to the actual .NET 10 WPF source and existing alpha.13 Windows captures. Decisions and framework sources are recorded in [UI_DESIGN.md](../UI_DESIGN.md).

## Completed local checks

- All application XAML files parse as XML, including the updated templates and resource references.
- The 12 existing repository-maintenance safeguards pass.
- `git diff --check` passes.
- Source comparison checks preserve the existing commands and click/change handlers, named controls, numeric-validation annotations, palette definitions and serialized theme model.

These checks do not establish WPF compilation or rendered layout. Impeccable's native context loader ran successfully but did not identify WPF as an automatically detectable visual platform; no web detector is reported as a XAML audit.

## Awaiting Windows checks

The current candidate still requires Windows build/controller/WPF checks, wide and compact capture review, and published EXE/extracted-ZIP startup checks. Added WPF checks cover toolbar containment/overlap at 1560 and 1080 window widths, rendered widget type labels, and actual UI Automation names for visible fields and colour actions.

The owner explicitly approved pushing the candidate branch and opening a draft PR on 15 October 2026. Windows workflow results and captured UI review are pending; local checks alone are not reported as a successful Windows run.

Interactive Windows DPI, Narrator and high-contrast qualification remain separate. Physical display brightness, transport, media playback and renderer paths are outside this UI change.
