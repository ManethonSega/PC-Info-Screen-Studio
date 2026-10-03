# PC Info Screen Studio

A visual Windows theme editor and runtime for 3.5-inch Turing/TURZX-style USB-CDC PC info screens.

> **Status:** early development milestone (`0.3-dev`). The editor foundation, theme package format, core widgets, custom asset embedding and the Tedd screen driver integration are present. Live hardware sensors, animated GIF playback, video transcoding/audio stripping and automatic media optimization are intentionally tracked as the next milestones.

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
- optional live Windows data mode for CPU load, RAM, system-drive usage/free space, network throughput, date and time
- live graph history buffering without modifying the saved theme
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

## Hardware driver

The repository vendors source derived from [Tedd.TuringScreen](https://github.com/tedd/Tedd.TuringScreen), licensed under MIT. It has been adapted from a console demo project into a class library. See [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

This project is independent and is not affiliated with or endorsed by TURZX, Turing, or the display manufacturer.

## License

MIT. See [`LICENSE`](LICENSE).
