# Third-party notices

## Tedd.TuringScreen

This repository vendors source derived from **Tedd.TuringScreen** by Tedd.

Original project: https://github.com/tedd/Tedd.TuringScreen

License: MIT

Copyright (c) 2025 Tedd

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

The vendored project has been changed from a console executable into a class
library and excludes the original demo `Program.cs` and demo GIF assets.


## LibreHardwareMonitor

PC Info Screen Studio uses the **LibreHardwareMonitorLib** NuGet package for live CPU, GPU, storage, motherboard, fan, pump and power sensors.

Original project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

NuGet package: https://www.nuget.org/packages/LibreHardwareMonitorLib/

Package version currently referenced: 0.9.6

License: Mozilla Public License 2.0 (MPL-2.0)

The LibreHardwareMonitor library remains under its own licence. PC Info Screen Studio does not relicense that dependency under MIT.

## Open-Meteo

Weather and geocoding data are retrieved from Open-Meteo.

Weather API: https://open-meteo.com/

Geocoding API: https://open-meteo.com/en/docs/geocoding-api

Open-Meteo weather data is provided under CC BY 4.0 and requires attribution. Geocoding results use GeoNames-derived location data. PC Info Screen Studio caches current conditions to avoid unnecessary API traffic.


## Bundled Google Fonts

PC Info Screen Studio bundles the following fonts from the Google Fonts project:

- Bungee
- Fredoka
- Monoton
- Orbitron

Source: https://github.com/google/fonts

These font families are distributed under the SIL Open Font License 1.1 (OFL-1.1). Their font files remain under the OFL and are not relicensed under the application's MIT licence.
