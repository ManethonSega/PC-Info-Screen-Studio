# Roadmap

Current source version: **0.10.0-alpha.5**.

The next milestone is product polish, not another broad feature category. Priorities remain **easy to use, lightweight, intuitive, and visually clear**. Checked items below describe implemented functionality, not a claim that every device or workload has been validated.

## Implemented

### Setup and interface

- [x] First-run screen detection/connection assistant and starter theme.
- [x] Prominent Info Screen / Photo Frame / Hybrid selector with mode-specific left panels.
- [x] Dedicated Settings window for connection, rotation, protocol, sensor access, runtime, and editor preferences.
- [x] Direct Add buttons on the left; configuration on the right, with search filtering the selected widget's Source dropdown.
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
- [x] Independent Hybrid overlay widgets, exact saved positions, caption font selection and remembered slideshow position.
- [x] Background-only widget transparency from 0 to 100%.

### Data, runtime, and distribution

- [x] Windows CPU/RAM, drive, network, clock/date, and weather sources.
- [x] LibreHardwareMonitor sensors and optional HWiNFO shared-memory sources.
- [x] Unavailable-sensor handling and optional PawnIO installation workflow.
- [x] Revision-A detection, compatibility options, and physical display diagnostics.
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

## Next: polish and maintainability

### 1. Stabilize the existing workflows

- [ ] Check the complete connect, add, edit, save, reopen, and tray workflow with a new user.
- [x] Automated Save/Load, independent settings, linked folder, layer position and session-resume checks for all three modes.
- [x] Actual WPF minimize, restore, close-to-tray and Exit checks, plus simulated reconnect failure and recovery.
- [ ] Confirm these workflows on the physical screen and with a new user.
- [x] Check actual selected-item and tooltip colours on the Windows runner and review WPF interface captures.
- [ ] Confirm hover, menu and caption readability on the owner's Windows configuration and physical display.
- [x] Controlled dark-control templates, consistent sizes/headings, toolbar icons, clear labels, descriptive hover help and a dark tray menu; direct actions stay in place.

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

- [x] Publish 0.10.0-alpha.5 as a versioned GitHub Release with a permanent portable download and matching source.
- [x] Compatibility and validation records distinguish automated checks from physical-device evidence.
- [x] Refresh the UI screenshots from the actual Windows WPF controls before publication.
- [ ] Decide on an installer and file associations after the portable workflow is stable.
- [ ] Evaluate signing and update delivery separately; neither exists today.

## Deferred, not implemented

- Full video decoding/playback; the current import shows a placeholder.
- Broader display-model compatibility until verified on physical hardware.
- Automatic updates, signed builds, and installer packaging.

## Deliberately outside the current photo workflow

Scheduling, blurred-image backgrounds, continuous Ken Burns animation, and per-photo crop/focal-point/timing controls are not pending UI additions. They were removed to keep the application simpler and reduce display work. Photo Frame and Hybrid settings remain separate from Info Screen themes, and photos stay on the PC.

### Layout cleanup

- Editor/Hardware navigation separates live PC readings from screen design without stopping USB output.
- One consistent Themes panel replaces the File menu across all three screen modes.
- Mode-aware theme commands retain photo playlists and unsaved edits in other modes.
