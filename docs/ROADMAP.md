# Roadmap

Current source version: **0.10.0-alpha.35** (draft candidate).

The next milestone is product polish, not another broad feature category. Priorities remain **easy to use, lightweight, intuitive, and visually clear**. Checked items below describe implemented functionality, not a claim that every device or workload has been validated.

## Implemented

### Setup and interface

- [x] First-run screen detection/connection assistant and starter theme.
- [x] Prominent Info Screen / Photo Frame / Hybrid selector with mode-specific left panels.
- [x] Dedicated Settings window for connection, rotation, protocol, sensor access, runtime, and editor preferences.
- [x] Direct Add buttons on the left; configuration on the right, with search filtering the selected widget's Source dropdown.
- [x] Separate Edit and Live-view workflows.
- [x] Wider scrollable left panel and expandable sections with persisted preferences.
- [x] Editor-only empty-state prompts, selected-widget-only properties and vector alignment icons with descriptive tooltips.
- [x] Inline numeric validation for dimensions, opacity, sensor ranges and photo settings, with invalid drafts preserving the last valid value.

- [x] Labelled collapsible panel strips with independent open sections, resizable stacks, keyboard reordering and saved workspace layout/reset.
- [x] Automatic preview fitting across window/panel changes, with separate manual zoom and 100% view.
- [x] Free panel dragging with docking previews, cross-docking, keyboard menu alternatives and owned floating windows with saved bounds and off-screen recovery. Interactive mixed-DPI qualification remains open.

- [x] Experimental Phanteks Matrix 900 workspace, direct tile selection, exact-height pixel typography and configurable row spacing; physical-device qualification remains open.
- [x] Matrix widget buttons and layers, LED-snapped dragging, content-fitted widths, read-only widget headings, value labels and the shared full sensor picker.
- [x] Twelve native progress-bar styles, a curated pixel-art bank, reversible rocket flight and the 60/30 panel guide.

### Info Screen editor

- [x] Silent Windows-decoded video widgets with separate serial/USB rate limits, bounded active decoders and pause/restart controls.
- [x] Local Export diagnostics action with resource/connection counters and error codes, excluding media and personal paths.

- [x] Hardware-name widgets with automatic CPU/GPU/SSD/RAM identification and persistent custom names.
- [x] Colour emoji in rendered text, labels and captions, with grapheme-aware shaping and bounded text-layout caching.

- [x] Top-bar brightness control (0..100%) with separate remembered levels for both screen families, live changes and reconnect restoration.
- [x] Landscape/portrait canvas and physical rotation controls.
- [x] Text, values, circular gauges, analog clocks, bars, graphs, shapes, images, and custom fonts.
- [x] Animated GIF playback with sequential composition and compressed GIF data cached in RAM up to 64 MiB per file.
- [x] Dragging, resizing, layers, visibility, locking, and duplication.
- [x] Undo/redo, multi-selection, grouping, alignment, and distribution.
- [x] Grid snapping, snap-aware keyboard movement, smart guides, and editor zoom.
- [x] Explicit font size retained during widget resizing; centred circular-gauge text.
- [x] Shareable `.t3theme` packages and generated theme-library thumbnails.

### Photos and Hybrid

- [x] Multiple-file/folder import, drag-to-reorder playlist, and playback controls.
- [x] Global timing, transitions, loop/shuffle, image fit, and solid background colour.
- [x] EXIF orientation correction and display-sized photo preparation.
- [x] Global captions with explicit caption font size and configurable text outline.
- [x] Missing caption metadata leaves the caption blank.
- [x] Folder watching and reusable linked `.pcalbum` playlists.
- [x] Separate `.pcphoto` / `.pchybrid` settings folders, without embedded photos.
- [x] Independent Photo Frame/Hybrid playlists, folders, settings and slideshow positions, plus separate Hybrid overlay widgets.
- [x] Background-only widget transparency from 0 to 100%.

### Data, runtime, and distribution

- [x] Windows CPU/RAM, drive, network, clock/date, and weather sources.
- [x] LibreHardwareMonitor sensors and optional HWiNFO shared-memory sources.
- [x] Unavailable-sensor handling and optional PawnIO installation workflow.
- [x] Revision-A detection, compatibility options, and physical display diagnostics.
- [x] Experimental TURZX 5.2-inch WinUSB backend, bounded JPEG frames and retained connection during mode changes.
- [x] Top-bar device family selection, remembered USB identity and canvas sizing for blank/imported themes.
- [x] Continuous reconnect retries and optional best-effort exit clearing.
- [x] Large-font graph/bar headings and resize-grip selection correction.
- [x] Recovery writes deduplicated until another edit occurs.
- [x] Background device I/O.
- [x] Tray operation with editor suspension, released preview resources, and photo controls.
- [x] Windows Startup Apps registration with a one-time approved per-user task, automatic display reconnection, and workspace/mode restoration.
- [x] Hardware overview page with live metrics and hardware names.
- [x] Bounded prepared-photo cache.
- [x] Self-contained Windows x64 publishing and CI build/publish/startup smoke checks.
- [x] Short-lived build artifacts: 3-day retention and old-artifact cleanup.
- [x] Public repository and GPL-3.0-or-later licensing.
- [x] README refresh with current version, actual features, download, and connection instructions.

## Memory status

The owner reports approximately **150 MB RAM usage as of 5 October 2026**, replacing the earlier approximately 1 GB observation. This is an observation on the owner's setup, not a published benchmark or a guarantee for all themes and albums. No urgent memory rewrite is planned on the strength of that observation.

## Next: beta readiness

The owner has supplied the alpha.11 confirmation requested earlier: basic output, mode switching, large fonts and independent playlists work on their setup. On 14 October 2026 they also reported overnight tray operation, restart, sleep/wake, unplug/replug, both screens connected and repeated mode changes passing. Everyday functional hardware checks are complete on that setup. The tested build, exact duration and CPU/memory traces were not supplied. Broader device and first-time-user evidence remains separate.

- [x] Owner's everyday hardware checks, including overnight tray operation and reconnects.
- [x] Automatic family handoff when a remembered screen is absent and exactly one recognized screen of the other family is present; both attached retains the selection. Software checked; physical retest of this new behavior remains pending.
- [ ] Measure video output on each display; owner reports slow, visibly progressive full-screen video on the 3.5-inch serial screen.

Remaining work is tracked in [labelled GitHub issues](https://github.com/ManethonSega/PC-Info-Screen-Studio/issues?q=is%3Aissue+is%3Aopen). Each issue has completion criteria. The [beta-readiness queue](https://github.com/ManethonSega/PC-Info-Screen-Studio/issues?q=is%3Aissue+is%3Aopen+label%3Abeta-readiness) contains the high-priority stability and validation work; medium and low priorities are follow-ups.

| Priority | Work | Tracking label |
| --- | --- | --- |
| Implemented in alpha.20 | Validated settings/session backups, automatic recovery and inline notices | `area: persistence` |
| Implemented in alpha.20 | Shared file-operation guards, asynchronous preparation, progress/Cancel and workspace file lifetimes | `area: runtime` |
| Implemented, measurement follow-up | Shared 128 MiB retained media budget; representative extended hardware resource measurements remain | `area: memory` |
| High | Complete device/build records, measured resource trends and new handoff/video evidence; everyday functional checks passed | `area: hardware` |
| High | Validate first-time setup, Windows scaling and keyboard accessibility | `area: usability` |
| Medium | Extract remaining coordinator responsibilities and reusable mode panels | `area: architecture` |
| Medium | Improve conventional unit-test and CI result reporting | `area: tests` |
| Low, deferred | Decide installer, file associations, signing and update strategy | `area: distribution` |

Existing controller checks cover Save/Load, independent mode settings, linked folders, layer positions, session resume, actual WPF tray/window behaviour and simulated reconnect recovery. Windows captures verify current controls and rendered colour emoji. These checks complement physical-device and new-user testing; they do not replace it. See [VALIDATION.md](VALIDATION.md) and [COMPATIBILITY.md](COMPATIBILITY.md).

Preserve the current three-panel editor, commands, theme formats and undo behaviour during maintenance. Diagnostics should stay optional for ordinary use. There is no urgent memory rewrite based solely on the owner's 150 MB observation, but large albums and multiple GIFs still need total-cache limits and representative measurements.

## Documentation and repository upkeep

- [x] Alpha.13 version and owner confirmation recorded consistently.
- [x] README and secondary screenshots refreshed from the actual alpha.13 Windows interface.
- [x] Product specification covers both display families and separates implemented features from deferred work.
- Bug-report forms and labelled backlog issues accompany this cleanup.
- Obsolete feature branches are removed only after their complete histories are verified in main and checks pass.

## Deferred, not implemented

- Native 5.2-inch H.264/SD-card playback, advanced video editing and video playlists.
- Broader display-model compatibility until verified on physical hardware.
- Automatic updates, signed builds, and installer packaging.

## Deliberately outside the current photo workflow

Scheduling, blurred-image backgrounds, continuous Ken Burns animation, and per-photo crop/focal-point/timing controls are not pending UI additions. They were removed to keep the application simpler and reduce display work. Photo Frame and Hybrid settings remain separate from Info Screen themes, and photos stay on the PC.

### Layout cleanup

- Editor/Hardware navigation separates live PC readings from screen design without stopping USB output.
- One consistent Themes panel replaces the File menu across all three screen modes.
- Mode-aware theme commands retain photo playlists and unsaved edits in other modes.
