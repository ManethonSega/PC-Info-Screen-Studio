# Product specification

This describes the current **0.10.0-alpha.13** product. Remaining work is tracked in the [roadmap](ROADMAP.md) and [GitHub issues](https://github.com/ManethonSega/PC-Info-Screen-Studio/issues). A listed feature does not certify every device or workload; see [compatibility](COMPATIBILITY.md) and [validation](VALIDATION.md).

## Product and interface

PC Info Screen Studio is a portable Windows x64 visual editor and runtime for small USB displays. Its priorities are easy setup, a readable preview and lightweight operation.

Keep the existing editor structure: Add/Layers on the left, a display-sized canvas in the centre and contextual properties on the right. Editor/Hardware navigation, Undo/Redo, Settings, Live view, display selection, Mode and Start/Stop stay in the top bar. Each mode has a Themes panel. Settings contains connection, orientation, physical mounting rotation, sensor access, runtime and editor preferences.

Use the shared MechKeyMesh navy/slate palette with silver text and cyan accents, consistent spacing and visible focus/selection states. Scrollable panels and inline numeric validation keep configuration readable. Hardware shows PC names and live metrics without stopping the selected display mode.

## Display families

| Selected family | Landscape canvas | Portrait canvas | Transport and status |
| --- | --- | --- | --- |
| 3.5-inch Revision-A Turing/TURZX-style display | 480 × 320 | 320 × 480 | USB-CDC serial, current supported focus |
| TURZX 5.2-inch smart-screen display | 1280 × 720 | 720 × 1280 | WinUSB JPEG output, experimental; target VID 1CBE / PID 0050 |

Canvas orientation and physical mounting rotation are independent. Rotation supports 0, 90, 180 and 270 degrees. New and imported themes fit the explicitly selected display. A missing screen does not silently select a different family when both are connected. The app does not install a display driver or control the TURZX desktop-monitor, SD-card or H.264 features.

The owner confirms basic output and alpha.11 mode switching on their setup. Sustained operation, firmware variants and broader compatibility require further evidence.

## Modes and editing

**Info Screen** arranges widgets and imported assets. **Photo Frame** runs a linked photo playlist. **Hybrid** combines its own playlist with a separate overlay layout. Modes retain independent settings and working edits.

The editor supports selection, dragging, resize handles, rotation, keyboard movement, layers, hide/lock, duplication, multi-selection, grouping, alignment/distribution, grid snapping, smart guides, zoom and undo/redo. Resizing a widget retains its explicit font size. Live view provides a clean preview; physical output is controlled separately with Start/Stop.

Implemented widget types are Text, Value, Circular Gauge, Analog Clock, Bar Gauge, Graph, Shape, Image, Animated Image and Hardware Name. Video is an import placeholder, with no decoding or playback. Shapes and gauges expose relevant fill, track, outline, range and text controls; graph styles include line, area and history bars. Widget background transparency affects only the background fill.

Hardware Name detects CPU, GPU, SSD/drive or RAM brand/model once in the background. Users can choose a component, edit Displayed name or restore Use detected name. Custom names persist in themes, clones and session resume. Firmware may omit meaningful RAM names; the app does not guess them.

## Text, fonts and data

Text supports installed Windows fonts, bundled fonts and imported TTF/OTF fonts. Imported fonts are passive theme assets and require redistribution rights. Rendered widget text, labels, values, hardware names and photo captions support colour emoji through Windows Segoe UI Emoji and grapheme-aware shaping. Native editing fields may show monochrome glyphs. Users can paste emoji or use Win + .; the canvas and device output share the renderer.

Data sources include Windows CPU/RAM, drive/network readings, date/time, Open-Meteo weather, LibreHardwareMonitor and optional HWiNFO shared memory. Optional PawnIO setup and administrator access extend sensor availability. Missing sensors are labelled unavailable, not invented zeroes. Source search filters the selected widget's source list. Preview.Value supplies a manual design value.

## Photos and media

Photo Frame and Hybrid support file/folder import, playlist reordering, previous/play/pause/next, shared timing, loop/shuffle, Fit/Fill/Stretch/Original sizing, solid backgrounds and Crossfade/Slide/Zoom/Instant/Random transitions between photos. EXIF orientation is corrected. Global captions support filename, date, location or custom text with configurable font, size, colour and outline. Missing metadata leaves a caption blank. Folder watching and linked playlist presets are available.

PNG, JPEG, WebP, BMP and GIF assets are supported. Animated GIFs play; video assets currently show a placeholder. Prepared photos use a bounded display-sized cache, and compressed GIF data is limited to 64 MiB per file. General image/GIF cache memory is not yet governed by a complete shared budget; this is tracked work.

Scheduling, blurred backgrounds, continuous Ken Burns motion and per-photo crop, focal-point or duration controls are outside the current simplified workflow. There is no implemented video optimizer, media-trimming pipeline or one-click USB benchmark profile.

## Saving and sharing

| Format | Content | Portability |
| --- | --- | --- |
| `.t3theme` | Info Screen layout, bindings, styles, imported fonts/images/GIFs and passive assets | Shareable ZIP-compatible package; [format details](THEME_FORMAT.md) |
| `.pcphoto` | Photo Frame settings and remembered folder | Photos remain on the PC |
| `.pchybrid` | Hybrid settings and overlay widgets | Linked photos/assets can depend on local paths |
| `.pcalbum` | Photo references and playlist settings | Linked preset, not a portable photo bundle |

New/Open/Save/Save as and library actions use the active mode's format. Opening a folder-based mode theme reloads that folder; a theme without a folder retains the current playlist. Exit/logoff saves a private resume snapshot of modes, layouts, photo links and unsaved edits without overwriting reusable themes. Backup/recovery hardening is still pending.

Themes contain declarative data and passive assets, never executable code. Preserve existing format compatibility during maintenance. File associations are not installed today.

## Runtime and distribution

Background USB output uses a newest-frame queue. Tray operation suspends editor-only preview work while output continues. Windows startup uses an approved per-user scheduled task, resumes the working session and retries a remembered device quietly, including COM-number changes and USB reconnects. Optional clear-on-exit is best effort. Sensor elevation and prerequisite installation remain explicit user actions.

The app is GPL-3.0-or-later, self-contained and distributed as an unsigned Windows x64 portable ZIP. Keep the complete folder together. There is no signed installer or automatic updater. Windows CI builds, checks controller/WPF workflows, captures the interface and smoke-tests published EXE and ZIP output. Physical reliability and usability results are recorded separately.

## Next milestone

Prioritize settings/session recovery, safe long-running commands, total asset-memory bounds, physical reliability evidence and first-time/DPI/accessibility validation. Keep the current interface structure. Architecture, diagnostics and test-reporting improvements follow; installer/signing/update decisions remain deferred. Full video playback is a separate future feature, not an alpha.13 promise.
