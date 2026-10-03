# Changelog

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
