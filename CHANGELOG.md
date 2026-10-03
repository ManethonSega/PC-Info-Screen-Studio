# Changelog

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
