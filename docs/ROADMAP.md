# Roadmap

## Milestone 0.1: editor foundation

- [x] Repository and licensing structure
- [x] .NET 10 WPF application
- [x] SkiaSharp theme renderer
- [x] Landscape/portrait canvas
- [x] Physical rotation model 0/90/180/270
- [x] Move/resize editor items
- [x] Layers, visibility and locking
- [x] Text/value widget
- [x] Circular gauge
- [x] Segmented bar
- [x] Line, stepped, area and block graph
- [x] Image/GIF/video/font asset plumbing
- [x] Shareable `.t3theme` package
- [x] Vendored Tedd hardware driver integration
- [x] Self-contained publish scripts

## Milestone 0.2: live data

- [x] Runtime/design-time value separation
- [x] Windows CPU usage provider
- [x] Windows RAM provider
- [x] System-drive usage/free-space provider
- [x] Network upload/download provider
- [x] Clock/date/day provider
- [x] True one-second history buffering for live graphs
- [ ] Hardware sensor abstraction for temperatures/fans/voltages
- [ ] GPU provider
- [ ] motherboard/fan/pump provider
- [ ] per-widget update rates
- [ ] graph history buffers
- [ ] graceful missing-sensor fallback
- [ ] sensor picker grouped by hardware device

## Milestone 0.3: media engine

- [ ] interactive pan/zoom crop editor
- [ ] still-image resize/cache
- [ ] animated GIF playback
- [ ] video decoder/transcoder
- [ ] discard audio during import
- [ ] trim start/end and playback speed
- [ ] automatic FPS selection from measured device throughput
- [ ] RGB565-aware temporal delta suppression
- [ ] maximum-quality/balanced/performance profiles
- [ ] media asset deduplication

## Milestone 0.4: editing quality

- [ ] undo/redo transaction stack
- [ ] grid and snapping
- [ ] smart alignment guides
- [ ] multi-select
- [ ] grouping
- [ ] alignment/distribution commands
- [ ] keyboard nudge and large nudge
- [ ] zoom controls
- [ ] calibrated physical-size preview
- [ ] copy/paste style
- [ ] theme color palette

## Milestone 0.5: runtime

- [ ] tray-only playback mode
- [ ] auto-load last theme
- [ ] start with Windows
- [ ] device reconnect UI
- [ ] display brightness schedule
- [ ] measured USB throughput profile per device
- [ ] non-blocking render/send queue

## Milestone 0.6: sharing and release

- [ ] theme thumbnail
- [ ] `.t3theme` file association
- [ ] installer
- [ ] portable self-contained ZIP
- [ ] automatic GitHub release workflow
- [ ] signed builds
