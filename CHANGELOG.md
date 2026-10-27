# Changelog

## 0.10.0-alpha.31 (draft)

- Added the selected 4-/5-/10-dot Matrix font collection, with saved choices per height.
- Added 4+5 and 5+4 two-row layouts alongside 4+4 and 5+5, with exact blank-row spacing and preserved artwork.
- Included all fourteen bitmap choices for development, with source credits and recorded release-review items for Limitdot H4 and piconano.
- Kept preview, sensor/clock text and uploaded RGB output on the same bitmap renderer.

## 0.10.0-alpha.30 (draft)

- Add a saved five-dot Matrix font choice: Tom Thumb or PixelFont 5×5 Thin. Four- and ten-dot defaults remain pending review.

## 0.10.0-alpha.29 (draft)

- Select Matrix columns and tiles directly in the preview, with visible boundaries and keyboard tile navigation.
- Use all 10 rows for single-row text and cap word gaps at three LEDs. Two-row content defaults to 4 + 4 dots with two blank rows between; 5 + 5 is available without a gap.
- Render text, sensors and UTC clock into the same RGB pixels for preview and acknowledged hardware uploads. Only changed tiles are resent; scrolling is bounded without queuing frames. Artwork outside the selected four-row area is preserved.

## 0.10.0-alpha.28 (draft)

- Added a dedicated experimental Phanteks Matrix 900 workspace selected through the top-bar Screen menu, with separate saved settings and LCD session preservation.
- Added verified native one-panel, three-column and six-tile layouts with text, sensors, UTC clock and static pixel-art controls, image import and keyboard painting.
- Added HID firmware probing, serialized packet transfers, acknowledgement-gated RGB paging and unavailable-sensor blanking.
- Recorded NexLinq software analysis and outstanding physical qualification. Vendor binaries, firmware and assets are not redistributed.


## 0.10.0-alpha.27 (draft)

- Drag panel headers or rail entries to reorder, move between sides, or float, with visible docking targets and Escape cancellation.
- Added native resizable tool windows and keyboard-accessible Float / Dock left / Dock right menu actions.
- Remember panel sides and floating bounds, recover off-screen positions, and restore original sides through Reset workspace.
- Hide floating tools with Live view, Hardware, mode changes, the tray and minimized windows; retain their live controls and current edits.
- Preserve content-sized dock surfaces, single photo-playlist scrolling and automatic canvas fitting.

## 0.10.0-alpha.21

- Refined the native WPF editor using Impeccable, Web Design Guidelines, UI/UX Pro Max and Emil Design Engineering, retaining its three-pane structure and navy/cyan palette.
- Made canvas tool groups wrap with the available width instead of overlapping in a fixed-height row at compact window sizes.
- Reduced toolbar and canvas framing, made section headings easier to scan, and replaced internal widget type names and ambiguous Copy/Del labels with readable wording.
- Added contextual UI Automation names to editor fields, screen/mode choices, colour actions and layer controls. Editable font inputs inherit their control's accessible name.
- Kept frequent editing interactions immediate, removed dropdown fade animation, and improved consecutive toolbar tooltip timing and expander focus indication.
- Added Windows checks for compact toolbar geometry, rendered type labels and actual UI Automation field/colour names.

## 0.10.0-alpha.11

- Consolidated the previously local alpha.7 through alpha.11 source into GitHub.
- Added experimental TURZX 5.2-inch WinUSB output and a top-bar screen selector, automatic canvas sizing and remembered device family.
- Use bounded high-quality JPEG transfers in all TURZX modes; mode changes retain the existing connection without repeating setup.
- Improved source-search dropdown visibility and isolated display updates from slow sensor/weather reads.
- Improved GIF composition and cached compressed playback data in RAM, with a 64 MiB per-file limit.
- Fixed large graph/bar headings and resize-grip selection handling.
- Isolated Photo Frame and Hybrid playlists, settings and resume positions.
- Write recovery copies only after new edits.
- Added Windows regression checks for USB image transfers, device/canvas selection, GIF composition, fonts, mode queues and recovery writes.
- Updated release documentation and preserved the histories of ten obsolete branches before guarded cleanup.
- Physical confirmation of the alpha.11 mode-switch fix and prolonged playback remains pending.


## 0.10.0-alpha.6

- Added editor-only prompts to empty Photo Frame and Hybrid previews.
- Hidden widget property fields when no layer is selected.
- Replaced alignment letters with vector icons, accessible names and explanatory tooltips.
- Added inline numeric errors while retaining valid settings and commit-on-focus-loss editing.
- Added actual WPF checks for these workflows and refreshed the README workspace captures.

## 0.8.2-alpha.1

- Bundled the official unmodified PawnIO 2.2.0 setup executable inside Windows release artifacts under `Prerequisites`.
- Release builds verify PawnIO's SHA-256 before packaging: `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`.
- Hardware sensor setup now prefers the bundled PawnIO installer and falls back to Winget only when the bundled prerequisite is unavailable.
- Added PawnIO GPL-2.0-or-later license text and source/version information beside the prerequisite.
- Added a build-time multi-resolution Windows ICO generator using the PC Info Screen Studio cat artwork.
- The generated icon is embedded into `PCInfoScreenStudio.exe`, fixing the generic File Explorer executable icon and the tray icon obtained from the executable.
- Kept PawnIO as a separately installed driver component; Windows still shows UAC when the driver is installed.


## 0.8.1-alpha.1

- Improved GIF refresh on UsbMonitor 3.5-inch Rev-A screens.
- Native-portrait compatibility mode now keeps a logical framebuffer and sends only changed 16x16 tiles for sparse animations.
- Dense/full-screen GIF frames use a single streamed frame path instead of hundreds of tiny managed serial writes.
- The display connection now prefers 921600 baud and falls back to 115200 when required.
- Added a persistent **Close window to system tray** option. The tray menu provides Show and Exit commands while the LCD and sensor runtime continue in the background.
- Moved **Hardware sensors...** into the main toolbar so the low-level sensor setup is no longer hidden in the status bar.
- Clarified that running as Administrator alone does not install the low-level hardware driver.
- The Hardware sensors action can install the official `namazso.PawnIO` package through Windows Package Manager after explicit confirmation, restart the app, and run a direct CPU/GPU/VRAM/disk sensor check.
- LibreHardwareMonitor now keeps GPU and storage providers active before PawnIO is installed instead of allowing inaccessible motherboard/controller groups to hide otherwise readable telemetry.
- The sensor status line now shows readable CPU temperature/power, GPU temperature/VRAM and disk temperature values directly.


## 0.8.0-alpha.1

- Reworked hardware telemetry after comparing the original UsbMonitor behavior and established Turing-screen projects.
- CPU temperature now prioritizes CPU Package and uses LibreHardwareMonitor first, optional HWiNFO shared-memory readings second.
- CPU package power ignores invalid zero-value sensors and falls back to HWiNFO when available.
- GPU VRAM now prefers used/total dedicated memory and computes the real percentage before falling back to generic memory-load sensors.
- Added GPU.VRAMUsed and GPU.VRAMTotal data sources.
- Added Windows MSFT_StorageReliabilityCounter as a storage-temperature fallback.
- Added exact raw sensor sources from both LibreHardwareMonitor and HWiNFO for hardware-specific selection.
- Added optional guided PawnIO setup through the official Windows Package Manager package for full low-level CPU/motherboard sensor access.
- Added one-click elevated restart for low-level hardware sensors without forcing the whole application to always request administrator rights.
- Live metric units are now kept separately from saved theme suffixes, fixing cases such as CPU power appearing with a percent sign.
- Missing live sensors now stay N/A instead of being converted to a fake numeric zero.
- Fixed numeric editor fields so multi-digit values such as font size 20 or grid size 20 can be typed normally before validation.
- Connecting now pushes the current theme to the physical screen once even when Live display is not enabled.
- Added real animated GIF playback using SkiaSharp frame timing in both the editor and the physical display runtime.
- GIF playback respects loop, playback speed and optional target FPS settings.


## 0.7.0-alpha.1

- Added three Revision-A compatibility profiles instead of assuming one firmware behavior:
  - native 320×480 portrait transport with software rotation for USB35INCHIPSV2 / VID 1A86 PID 5722
  - hardware rotation with logical 480×320 landscape dimensions
  - legacy hardware rotation with native 320×480 dimensions in the orientation command
- Auto mode now prefers the proven native-portrait path for USB35INCHIPSV2/UsbMonitor 3.5-inch devices and uses the HELLO model response as a second signal.
- Added explicit RGB565 color modes: RGB little-endian, BGR little-endian, RGB byte-swapped and BGR byte-swapped. Auto uses the standard Revision-A RGB565 little-endian format.
- Added a six-color full-screen diagnostic pattern: red, green, blue, cyan, magenta and yellow.
- Protocol and color modes can be switched while connected and are remembered locally.
- Added reliable four-row native framebuffer writes matching tested Revision-A implementations.
- Added support for both 16-byte logical-dimension and 11-byte native-dimension orientation commands.
- Matched proven 115200 baud + RTS/CTS + DTR serial behavior.
- Added COM-port re-detection if the screen re-enumerates under a different port after reset.
- Added third-party attribution for usausa/turing-smart-screen, viktorkav/usb-lcd-dashboard and Bendak/TuringMonitor, and kept GPL projects as protocol references only.


## 0.6.0-alpha.1

- Fixed Rev-A landscape transport: orientation commands now send the correct 480×320 or 320×480 dimensions instead of always reporting portrait dimensions.
- Removed the duplicate software transpose for Rev-A screens and switched serial transport to RTS/CTS hardware flow control.
- Added the Rev-A HELLO handshake and model response detection after reset.
- Changed framebuffer transmission to the proven four-row chunk size used by the reference Rev-A implementation.
- Clear-screen handling now temporarily returns the controller to portrait mode to avoid the firmware clear/orientation bug.
- Added automatic source labels. Changing a data source now changes the default label, while the user can still edit the label afterwards.
- Added Windows regional formatting for Celsius/Fahrenheit, date and time.
- Added CPU clock fallback through Windows processor power information and more resilient network upload/download sampling.
- Expanded hardware monitoring with Fan 1 through Fan 6, pump aliases, GPU VRAM fallback calculation and exact vendor-specific raw sensor sources.
- Added a scalable traditional analog clock widget.
- Added four bundled playful fonts: Bungee, Fredoka, Monoton and Orbitron.
- Traced the supplied cat artwork for the window/taskbar icon so it matches the provided logo much more closely.


## 0.5.0-alpha.1

- Added a real hardware sensor engine using LibreHardwareMonitorLib 0.9.6.
- Added live CPU package temperature, CPU power and average core clock.
- Added live GPU core usage, temperature, hotspot, VRAM load, power and fan RPM.
- Added storage temperature and motherboard/controller fan and pump RPM mappings.
- Added city-based weather with automatic geocoding and cached Open-Meteo current conditions.
- Added Weather Temperature, Feels Like, Humidity, Wind, Wind Direction, Condition and Location data sources.
- Added a persistent local Weather City preference without embedding the user's city into shared theme files.
- Changed 3.5-inch serial startup to prefer the vendor-compatible 115200 baud setting, with 921600 as fallback.
- Added display reset/re-enumeration delay, explicit Screen On initialization and corrected brightness protocol mapping.
- Added a Test screen control that sends a full-screen RGB diagnostic pattern.
- Improved known 3.5-inch Turing/UsbMonitor detection using USB35INCHIPSV2 and VID_1A86/PID_5722 metadata.


## 0.4.0-alpha.1

- Added automatic USB serial screen/COM-port discovery with friendly device names.
- Added automatic fallback from 921600 to 115200 baud for screen drivers that reject the higher line-coding rate.
- Added a clearer warning when a COM port is already owned by another program.
- Added a built-in/user theme library. Saved themes default to Documents/PC Info Screen Studio/Themes and appear in the theme list.
- Added a user-configurable editor grid and snap-to-grid for moving and resizing layers.
- Text, Value and Circular Gauge widgets now scale their text automatically with the layer dimensions instead of exposing a manual text-size field.
- Applied the supplied cat logo as the application/window icon.
- Unified dark-interface text and layer-list styling.
- Replaced the visible Benchmark control with screen detection. The low-level benchmark remains available internally for later diagnostics.
- Unsupported live data sources now show N/A instead of silently looking like live simulated data.


## 0.3.0-alpha.3

- Fixed the editor overlay that painted widgets as blank white rectangles.
- Made widget move/select hit areas transparent so rendered text, gauges, graphs and imported images remain visible.
- Fixed Layers Up/Down so the visible list order and actual Z-order change together.
- Moved connection, framebuffer writes, orientation changes and benchmarking off the WPF UI thread.
- Added frame-drop protection so slow USB writes cannot queue unlimited live-preview frames.
- Bounded serial connection retries for busy/inaccessible COM ports and added a write timeout.
- Re-applies orientation to an already-connected display when layout or mount rotation changes.


## 0.3.0-alpha.2

- Removed the incompatible WPF/OpenTK Skia view package and now render the Skia framebuffer into a native WPF image surface.
- Added startup crash logging under `%LOCALAPPDATA%\PCInfoScreenStudio\Logs\startup.log` and a visible fatal-error dialog.
- Added a CI startup smoke test for the published application.
- Switched alpha publishing from single-file bundling to a self-contained folder build for more reliable native-library loading.
- Updated SkiaSharp text rendering calls to the current `SKFont` API.
- Cleaned the vendored driver build warnings and updated GitHub Actions to current action versions.


## 0.3.0-alpha.1

- Established the project identity as **PC Info Screen Studio**.
- Renamed the application project, root namespace, executable assembly, solution, publish scripts, CI artifact names and documentation.
- Theme manifests created by this version identify the format as `PCInfoScreenStudio`.
- Kept the `.t3theme` extension so existing early theme files remain recognizable and portable.
- Updated the bundled Starter Amber sample theme to the new application identity.

## 0.2.0-alpha.1

- Added initial live Windows data providers for CPU load, memory, storage, networking and clock/date values.
- Added live graph-history buffering separated from saved design-time theme data.

