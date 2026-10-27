# Phanteks Matrix 900 interoperability observations

Recorded 2026-10-27 from the owner's uploaded `Phanteks NexLinq.zip`. The vendor application was inspected statically, not executed. These observations describe software behavior, not a completed physical-device qualification.

## Evidence identity

| Item | Value |
| --- | --- |
| Uploaded archive SHA-256 | `c6cfacee17916755203d90925506032334a0fe6ed39ca4fc98efd5b0990fb0b7` |
| NexLinq executable assembly file version | `1.5.0.1` |
| `LibPhanteks.dll` SHA-256 | `44f042f737ef3855e55d4424b09e709c541c5cf8171996d970ea72f40a45330a` |
| `PhanteksNexLinq.dll` SHA-256 | `801ee9d0bc2455cff5a447ce46aae8046222d6d5b474a2efabfad46136c0b67f` |
| Vendor application runtime | .NET 8 Windows desktop |
| Relevant library type | `LibPhanteks.LedMaster900` |
| Relevant embedded UI resources | `PhanteksNexLinq.html.matrix900.html` and `.js` |

No vendor DLLs, decompiled source, firmware, fonts, UI resources or sample artwork are included in PC Info Screen Studio. Its packet encoder, transport, controls and tests are original implementation work based on the observed interoperability facts. HidSharp 2.6.4 was already a transitive dependency of LibreHardwareMonitor; it is now referenced explicitly and its notice is included.

## Device and transport

The runtime Matrix 900 VID/PID is **2E3C:6500**. The library also contains **2E3C:2F30** as a bootloader identity. Our discovery intentionally targets only the runtime identity, refuses ambiguous multiple HID interfaces, and implements no firmware update or bootloader operations.

NexLinq uses HidSharp input/output reports, obtains their actual lengths from the HID descriptors, and uses report ID **1**. Its payload requires at least 64 bytes including the report ID. Our implementation uses the actual discovered length, bounded to 64–1024 output bytes, and validates inputs before indexing. HID access itself needs no serial COM port or replacement WinUSB driver.

## Report observations

Offsets include the report-ID byte at offset 0. Unused bytes are zero-filled.

| Command byte at offset 1 | Purpose | Observed format |
| --- | --- | --- |
| `01` | Older heartbeat / information request | Older sensor format; not used for live telemetry by this driver |
| `02` | Tile text | Offset 5: text-byte count + 1; offset 6: slot 0–5; offset 7: text bytes |
| `03` | Tile configuration | Offset 2: tile count; offsets 3–4: big-endian slot; offset 5: 41; offset 6: 41-byte configuration |
| `04` | Brightness / active / units | Offset 5: 1; offset 6: PWM level or 0 for inactive; offset 7: Celsius 0 / Fahrenheit 1 |
| `05` | Pixel-art count | Offset 2: FF; offset 5: 1; offset 6: number of pixel-art tiles |
| `05` | Pixel-art page | Offset 2: tile; offsets 3–4: big-endian page number; offset 5: pixel bytes + 2; offsets 6–7: width/height; offset 8: RGB bytes |
| `06` | New telemetry heartbeat | Offset 5: 58; offset 6: big-endian Unix timestamp; offset 10: DST bitmask; offset 16 + slot × 6: progress, followed by signed big-endian numeric value |
| `0D` | Firmware/product information | CSV payload at offset 6, length at offset 5; package 0 includes firmware version in CSV field 6 |

The vendor library switches to the new telemetry path when its version comparison establishes firmware **newer than V1.0.0.4**. Our driver first queries product information and refuses unreadable or older firmware. The owner can update firmware through NexLinq; our app does not perform that operation.

Brightness in NexLinq is a percentage translated to approximately `59 + round(percent × 1.21)`. The original helper accepts larger numbers, but its UI uses a percentage. Our app clamps to 0–100 and treats 0% as inactive rather than emitting the minimum lit PWM value.

## Geometry and content

The embedded layout table confirms **90 × 10 pixels**. Native tile sizes include 30/60/90 columns and 5/10 rows. The initial workspace exposes these verified layouts:

| Native layout ID | Tiles | Geometry and slot order |
| --- | --- | --- |
| 1 | 1 | 90 × 10, slot 0 |
| 3 | 3 | Three 30 × 10 columns, left to right |
| 9 | 6 | Three columns of two 30 × 5 tiles; top/bottom slots 0/1, then 2/3, then 4/5 |

Pixel art is row-major **R,G,B**, three bytes per LED, transferred in **56-byte chunks**. A full panel contains 2,700 RGB bytes and requires 49 pages. NexLinq advances after a command-05 reply. Our driver drains bounded stale input, then waits for a matching tile and page acknowledgement before sending the next page; missing acknowledgements abort instead of continuing an incomplete upload.

The vendor editor supports animation groups, but the initial driver deliberately sends one static image per tile. Importing GIF uses its first frame. Neither arbitrary MP4/GIF playback nor a maximum animation frame rate has been established for this controller.

Native widget categories observed: time/date (1), system data (2), media/effects (3), creator text/editor (4). Initial controls expose text, selected sensors, UTC clock and static pixel art. Sensor IDs used: CPU temperature 1, first GPU temperature 2, CPU utilisation 27, first GPU utilisation 30. Clock uses timezone table entry 12 (UTC/GMT), DST disabled, and UTC Unix timestamps.

The text writer has a 50-byte field and vendor-specific substitutions, including seven special emoji tokens. These are not a general Unicode renderer. The initial driver accepts printable ASCII for firmware text and explains unsupported characters. Pixel-art import provides an alternative for glyph artwork. Existing LCD Unicode/colour-emoji rendering is unaffected.

## Workspace and verification boundaries

Choosing Phanteks Matrix 900 opens its own controls; it does not resize the LCD document or add Matrix data to `.t3theme`, `.pcphoto` or `.pchybrid`. Its validated draft settings and pixel buffers are saved separately in app settings and their recovery backup. Apply sends a snapshot, so later edits cannot change an in-flight upload. A successful write is reported as sent, with a request to verify the panel, rather than proof of correct physical output.

Selecting Matrix stops LCD output and suspends hidden LCD preview rendering. Starting hardware output is explicit for this experimental target. Changes to the selected LCD target retain the existing LCD session behavior. Matrix connections are released on stop, target changes and application exit. The controller may retain its last visual after disconnect; this is stated in the UI.

The observed native heartbeat can carry numeric sensor values. Alpha.29 uses host-rendered RGB for text, sensors and clock so exact glyph height and word/row spacing match the preview. The 41-byte widget payload exposes no corresponding font-height or word-spacing field. One-panel and three-column text occupies the full 10-row band. Six-tile content defaults to four rows at native offsets 0–3 and 6–9, or five rows each when selected. Native tile geometry and RGB page formats stay unchanged. Hidden artwork rows are masked, not deleted.

Apply configures pixel widgets from the shared renderer. Subsequent updates resend only changed tiles and retain matching page acknowledgements. Text scrolling is scheduled at most every 200 ms, with one refresh in flight and no queued frames; actual throughput depends on controller acknowledgements and remains physically unqualified. Short/static text sends no repeated image pages. The keepalive/brightness heartbeat runs at most once per second, with immediate sensor blank/resume transitions. Missing/stale sensors prevent initial Apply or blank the active output; recovery resumes it. Draft source/layout/spacing changes require Apply.

Automated checks cover packet fixtures, colour order, paging and acknowledgements, firmware refusal, validation before I/O, preference repair and fake-HID lifetime. Native Windows checks cover target selection, dedicated controls, LCD state preservation, keyboard painting, tile mapping, compact layout, navigation and restart persistence. These do not establish physical LED orientation/colour, firmware font appearance, clock behavior, reconnect reliability or performance on a real Matrix 900.

Physical qualification still needed: device descriptors/report sizes, firmware reply, first text upload, every layout/tile index, known RGB corner pixels, brightness limits, UTC clock, live sensor/alarm behavior, stale-data blanking/recovery, unplug/replug, and concurrent NexLinq conflict behavior. Close NexLinq before using this driver.

Official references: [Matrix installation guide](https://phanteks.com/manuals/Matrix_Upgrade-Kit_Manual_v1.0.pdf), [NexLinq](https://phanteks.com/nexlinq/), [HidSharp API/source](https://github.com/IntergatedCircuits/HidSharp).
