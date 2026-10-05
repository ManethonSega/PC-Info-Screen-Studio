# Roadmap

Current source version: **0.10.0-alpha.3**.

The next milestone is product polish, not another broad feature category. Priorities remain **easy to use, lightweight, intuitive, and visually clear**. Checked items below describe implemented functionality, not a claim that every device or workload has been validated.

## Implemented

### Setup and interface

- [x] First-run screen detection/connection assistant and starter theme.
- [x] Prominent Info Screen / Photo Frame / Hybrid selector with mode-specific left panels.
- [x] Dedicated Settings window for connection, rotation, protocol, sensor access, runtime, and editor preferences.
- [x] Direct Add buttons on the left and searchable, one-click data-source adding on the right.
- [x] Separate Edit and Live-view workflows.
- [x] Wider scrollable left panel and expandable sections with persisted preferences.

### Info Screen editor

- [x] Landscape/portrait canvas and physical rotation controls.
- [x] Text, values, circular gauges, analog clocks, bars, graphs, shapes, images, and custom fonts.
- [x] Animated GIF playback.
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
- [x] Independent Hybrid overlay widgets and remembered slideshow position.

### Data, runtime, and distribution

- [x] Windows CPU/RAM, drive, network, clock/date, and weather sources.
- [x] LibreHardwareMonitor sensors and optional HWiNFO shared-memory sources.
- [x] Unavailable-sensor handling and optional PawnIO installation workflow.
- [x] Revision-A detection, compatibility options, and physical display diagnostics.
- [x] Background device I/O.
- [x] Tray operation with editor suspension, released preview resources, and photo controls.
- [x] Bounded prepared-photo cache.
- [x] Self-contained Windows x64 publishing and CI build/publish/startup smoke checks.
- [x] Short-lived build artifacts: 3-day retention and old-artifact cleanup.
- [x] Public repository and GPL-3.0-or-later licensing.
- [x] README refresh with current version, actual features, download, and connection instructions.

## Memory status

The owner reports approximately **150 MB RAM usage as of 5 October 2026**, replacing the earlier approximately 1 GB observation. This is an observation on the owner's setup, not a published benchmark or a guarantee for all themes and albums. No additional memory changes are part of this documentation update.

## Next: polish and maintainability

### 1. Stabilize the existing workflows

- [ ] Check the complete connect, add, edit, save, reopen, and tray workflow with a new user.
- [ ] Check Photo Frame and Hybrid theme/playlist behaviour independently.
- [ ] Verify tooltip, menu, selected-button, and caption contrast in the actual Windows UI.
- [ ] Improve spacing, typography, icon consistency, and descriptive hover help without hiding direct actions.

### 2. Reduce maintenance risk without changing the interface

- [x] First gradual extraction: independent editor, device, photo-playback, and settings controllers, with unchanged UI bindings.
- [ ] Continue extracting theme-library orchestration, metrics, undo snapshot application, and recovery from the remaining coordinator.
- [ ] Extract reusable mode-specific UI sections from MainWindow.
- [ ] Preserve commands, bindings, existing theme compatibility, and undo behaviour during refactoring.

### 3. Validate reliability and performance

- [ ] Record memory and responsiveness with a representative large album and extended tray operation.
- [ ] Verify repeated mode switches, theme reloads, and device reconnects.
- [ ] Document confirmed display models, USB identifiers, orientation, and working settings.
- [ ] Record unsupported devices and known failure cases clearly.

Validation should be focused on these user workflows and physical-device behaviour. Do not make theme-rendering diagnostics a prerequisite for ordinary use.

### 4. Make distribution dependable

- [ ] Publish a versioned GitHub Release with a permanent portable download and matching source.
- [ ] Refresh screenshots and deeper technical documentation to match the current interface.
- [ ] Decide on an installer and file associations after the portable workflow is stable.
- [ ] Evaluate signing and update delivery separately; neither exists today.

## Deferred, not implemented

- Full video decoding/playback; the current import shows a placeholder.
- General Windows-startup registration and automatic loading of the last saved theme.
- Broader display-model compatibility until verified on physical hardware.
- Automatic updates, signed builds, and installer packaging.

## Deliberately outside the current photo workflow

Scheduling, blurred-image backgrounds, continuous Ken Burns animation, and per-photo crop/focal-point/timing controls are not pending UI additions. They were removed to keep the application simpler and reduce display work. Photo Frame and Hybrid settings remain separate from Info Screen themes, and photos stay on the PC.
