# PC Info Screen Studio

A visual Windows theme editor and runtime for 3.5-inch Turing/TURZX-style USB-CDC PC info screens.

> **Status:** active alpha development (`0.7.0-alpha.1`). The visual editor, shareable theme format, live Windows/hardware/weather data, Revision-A display compatibility layer and physical-display diagnostics are implemented. Animated GIF/video playback and advanced media optimization remain active development items.

PC Info Screen Studio is designed around a simple workflow: add a widget, drag it on a real 480x320 or 320x480 canvas, style it, bind it to data, preview it on the physical display, and save everything as one shareable `.t3theme` file.

## Current milestone

Implemented in the source tree:

- .NET 10 WPF desktop application
- 480x320 landscape and 320x480 portrait theme canvases
- 0°, 90°, 180° and 270° physical display rotation model
- drag-to-move and handle-to-resize editor objects
- layers, visibility, locking, duplicate and layer-order controls
- text and dynamic-value widgets
- circular gauge
- segmented bar gauge
- graphs: line, stepped line, filled area and block style
- graph color, track/grid color and text color
- shapes
- JPG/PNG/WebP/BMP import
- GIF and video asset import plumbing
- custom TTF/OTF font embedding and per-widget selection
- one-file `.t3theme` ZIP package containing JSON, fonts and media
- theme load/save with package path traversal protection
- SkiaSharp-based common renderer for editor and device framebuffer
- COM-port discovery and live display path through the vendored `Tedd.TuringScreen` driver
- simulated data values for layout design
- live Windows metrics for CPU load, RAM, system-drive usage/free space, network throughput, date and time
- LibreHardwareMonitor-powered CPU temperature/power/clock, GPU usage/temperature/hotspot/VRAM/power/fan, storage temperature and motherboard/controller fan/pump RPM
- city-based current weather from Open-Meteo: temperature, feels-like, humidity, wind speed/direction, condition and resolved location
- clear N/A state when a requested hardware sensor is genuinely unavailable on the current machine
- live graph history buffering without modifying the saved theme
- USB serial device discovery with friendly COM-port names and likely-screen selection
- 921600 to 115200 automatic baud fallback for screen drivers with restricted serial line coding
- configurable editor grid and snap-to-grid movement/resizing
- built-in theme list plus user theme library under Documents/PC Info Screen Studio/Themes
- automatic text scaling for Text, Value and Circular Gauge widgets as their layer is resized
- GitHub-ready license, third-party notice, build workflow and self-contained publish scripts

## Planned next

The next implementation milestones are:

1. Temperature/GPU/fan/pump hardware sensor providers.
2. Weather provider and location configuration.
3. GIF frame playback in both editor and display runtime.
4. Video import pipeline that **removes audio**, crops/resizes once, chooses an optimal FPS and creates a screen-optimized silent asset.
5. Automatic device throughput benchmark with stored per-device tuning.
6. Media change suppression for tiny RGB565 differences, reducing USB traffic.
7. Undo/redo, snapping, guides, grouping, multi-selection and alignment tools.
8. Dedicated lightweight tray runtime and Windows startup support.
9. Automatic preview thumbnail inside `.t3theme` packages.
10. Installer, file association and signed release packaging.

See [`docs/PRODUCT_SPEC.md`](docs/PRODUCT_SPEC.md) for the target feature set and [`docs/ROADMAP.md`](docs/ROADMAP.md) for implementation status.

Release notes are tracked in [`CHANGELOG.md`](CHANGELOG.md).

## Build

Requirements for developers:

- Windows 10/11
- .NET 10 SDK
- Visual Studio 2026, Rider, or `dotnet` CLI

```powershell
dotnet restore .\PCInfoScreenStudio.slnx
dotnet build .\PCInfoScreenStudio.slnx -c Debug
dotnet run --project .\src\PCInfoScreenStudio.App\PCInfoScreenStudio.App.csproj
```

## Standalone end-user build

End users do **not** have to install .NET when the application is published self-contained.

```powershell
.\scripts\publish-win-x64.ps1
```

The output is placed under `artifacts/win-x64`. The build is self-contained, so the user does not need to install .NET. During alpha development it is intentionally published as a folder rather than a single EXE for maximum native-library reliability. Extract the complete GitHub artifact and keep all files together when running `PCInfoScreenStudio.exe`.

If the repository is hosted on GitHub, the included **Actions** workflow builds the self-contained `PCInfoScreenStudio-win-x64` artifact automatically on pushes to `main` and runs a startup smoke test against the published application before uploading it. The artifact can also be produced manually with **Run workflow**.

## Theme files

A `.t3theme` is a normal ZIP container with a custom extension. It contains no executable code. A typical package looks like:

```text
MyTheme.t3theme
├─ manifest.json
├─ theme.json
└─ assets/
   ├─ image/...
   ├─ gif/...
   ├─ video/...
   └─ font/...
```

The format is documented in [`docs/THEME_FORMAT.md`](docs/THEME_FORMAT.md).

## 0.7 alpha display compatibility

Revision-A 3.5-inch screens are not completely uniform. PC Info Screen Studio now supports three transport strategies instead of hard-coding one interpretation of the protocol:

- **Auto (recommended):** USB35INCHIPSV2 / VID `1A86:5722` and HELLO-identified UsbMonitor 3.5-inch devices use native 320×480 portrait transfer with software rotation. Other Revision-A devices use the logical-dimensions hardware-rotation profile.
- **Rev-A native portrait:** renders the application's 480×320 landscape frame, rotates it to the physical 320×480 framebuffer, packs RGB565 little-endian and sends it in four-row chunks.
- **Rev-A hardware landscape:** uses command 121 with logical dimensions, including 480×320 in landscape.
- **Rev-A legacy 320×480:** uses hardware orientation while retaining native 320×480 dimensions in command 121.

The **Color mode** control defaults to standard Revision-A **RGB565 little-endian**. Alternate BGR and byte-swapped modes are available for firmware variants and diagnostics. **Color test** sends six known bars in this exact order: red, green, blue, cyan, magenta, yellow. This makes red/blue channel swaps and byte-order errors immediately visible.

Both settings are local device preferences, not part of shared theme files, and can be changed while the display is connected.

## 0.6 alpha highlights

- **Rev-A display transport:** 115200 baud, RTS/CTS flow control, HELLO/model handshake, correct orientation dimensions and four-row framebuffer chunks.
- **Automatic labels:** selecting `CPU.Temperature`, `Weather.FeelsLike`, `Cooling.Fan2RPM`, etc. updates the widget label automatically.
- **Regional formats:** temperature follows the Windows measurement setting, while date/time and sunrise/sunset follow the current Windows culture and clock format.
- **Hardware sources:** in addition to friendly aliases, detected LibreHardwareMonitor sensors are added to the Source list as exact `Sensor: ...` entries. This is useful for motherboard-specific fan and pump names.
- **Traditional clock:** use **Add → Clock** for an analog clock with hour, minute and second hands.
- **Built-in fonts:** Bungee, Fredoka, Monoton and Orbitron are bundled with the application and can be selected without installing them in Windows.

If CPU package temperature, CPU power, motherboard fans or pump sensors are missing, try running PC Info Screen Studio as Administrator once. Some motherboard sensor chips require elevated low-level access.

## Connecting the screen

Use **Detect screen** to inspect Windows USB/serial device information and select the most likely screen port. PC Info Screen Studio also sends the Rev-A HELLO handshake after reset and uses the returned model code when the display supports it.

For the 3.5-inch Rev-A/UsbMonitor protocol, the app tries 115200 first, uses RTS/CTS hardware flow control and DTR, sends the HELLO model probe, and can re-detect the COM port if Windows assigns a new number after reset. Use **Color test** after connecting to validate geometry and RGB565 ordering before troubleshooting theme rendering.

If Windows reports **Access denied** for a COM port, close any other program that is using that screen/port, including the manufacturer's monitor application or tray process, before reconnecting.

## Weather

Enter a city in the Properties panel when a Weather data source is selected and press **Update**. The city is stored as a local application preference, not inside a shareable theme file. PC Info Screen Studio geocodes the city and refreshes current conditions approximately every 10 minutes.

Weather and geocoding are provided by Open-Meteo. Attribution and licensing information is listed in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

## Hardware sensors

Live hardware sensors are read through **LibreHardwareMonitorLib**. Supported mappings include CPU package temperature and power, CPU clocks, GPU core load/temperature/hotspot/VRAM/power/fan RPM, storage temperature, and motherboard/controller fan and pump RPM.

Sensor availability depends on the motherboard, controller, GPU/SSD firmware and Windows permissions. Some low-level sensors exposed by LibreHardwareMonitor may require running the program with elevated privileges.

## Hardware driver

The repository vendors source derived from [Tedd.TuringScreen](https://github.com/tedd/Tedd.TuringScreen), licensed under MIT. It has been adapted from a console demo project into a class library. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

This project is independent and is not affiliated with or endorsed by TURZX, Turing, or the display manufacturer.

## License

MIT. See [`LICENSE`](LICENSE).
