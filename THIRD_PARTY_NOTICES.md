# Third-party notices

The original PC Info Screen Studio application code is licensed under
GPL-3.0-or-later. The third-party components listed below retain their own
licenses and attribution requirements; they are not automatically relicensed.

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

The LibreHardwareMonitor library remains under its own licence. PC Info Screen Studio does not replace that dependency's MPL-2.0 license with its application license.

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

These font families are distributed under the SIL Open Font License 1.1 (OFL-1.1). Their font files remain under the OFL and are not relicensed under the application's GPL license.


## Revision-A compatibility references and adapted implementations

The Revision-A compatibility layer in PC Info Screen Studio was informed by and, where appropriate, adapted from the following MIT-licensed projects. The application keeps multiple protocol profiles because real 3.5-inch firmware revisions differ in orientation framing even when they share the same USB identity.

### TuringSmartScreenLib / usausa/turing-smart-screen

Source: https://github.com/usausa/turing-smart-screen

Relevant implementation: Revision-A C# serial framing, command 121 orientation behavior, RGB565 little-endian buffer packing and ScreenFactory abstraction.

MIT License

Copyright (c) 2021 machi_pon

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

### usb-lcd-dashboard / viktorkav

Source: https://github.com/viktorkav/usb-lcd-dashboard

Relevant implementation: tested USB35INCHIPSV2 / VID 1A86 PID 5722 native-portrait transport, 115200 baud with RTS/CTS, HELLO handshake, 480x320 to 320x480 software rotation, RGB565 little-endian packing and four-row framebuffer chunks.

MIT License

Copyright (c) 2026 ViktorKav

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

### TuringMonitor / Bendak

Source: https://github.com/Bendak/TuringMonitor

Relevant implementation: .NET 10 Revision-A framing, reconnect strategy, logical 480x320 hardware-orientation profile, inverted brightness mapping and dashboard-oriented partial-update design.

MIT License

Copyright (c) 2026 Maurício

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

### turing-smart-screen-python

Source: https://github.com/mathoudebine/turing-smart-screen-python

License: GPL-3.0

This project is used as a protocol-behavior and hardware-compatibility reference. No source code from this project is currently vendored. If implementations are adapted in future, preserve their exact upstream license and attribution and verify compatibility with the distributed application.


## Hwinfo.SharedMemory.Net

PC Info Screen Studio optionally reads an already-running HWiNFO instance through its shared-memory interface using **Hwinfo.SharedMemory.Net**.

Source: https://github.com/Seraksab/Hwinfo.SharedMemory.Net

NuGet package: Hwinfo.SharedMemory.Net 4.0.0

License: MIT

Copyright (c) 2023 Manfred Graf

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

## PawnIO

For full low-level CPU and motherboard sensor access, PC Info Screen Studio can install **PawnIO**, the signed hardware-access driver used by LibreHardwareMonitor.

Source: https://github.com/namazso/PawnIO

Official setup releases: https://github.com/namazso/PawnIO.Setup/releases

Windows release artifacts bundle the **unmodified official PawnIO 2.2.0 installer** as a separate prerequisite under `Prerequisites/PawnIO_setup.exe`. The build verifies this SHA-256 before packaging:

`1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`

PawnIO remains a separate driver component and is not incorporated into PC Info Screen Studio's application code. It is installed only after explicit user confirmation/UAC. Its GPL-2.0-or-later license text and source information are distributed alongside the installer under `Prerequisites`.

If the bundled prerequisite is unavailable, PC Info Screen Studio can fall back to the official Windows Package Manager package `namazso.PawnIO`.

## SkiaSharp

PC Info Screen Studio references SkiaSharp 3.119.1.

Source: https://github.com/mono/SkiaSharp

License: MIT. Skia and its other bundled native components retain their own upstream notices.

Copyright (c) 2015-2016 Xamarin, Inc.  
Copyright (c) 2017-2018 Microsoft Corporation.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
