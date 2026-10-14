# Device compatibility

This catalogue separates owner observations from repeatable automated checks. Brand name or diagonal size alone does not identify a display protocol.

| Device or workload | Evidence | Current position |
| --- | --- | --- |
| Owner's 3.5-inch, 480 × 320 Turing/TURZX-style USB serial display | Owner reported correct output/colours and working sensors; later everyday hardware checks passed on their setup | Revision-A device path is the current supported focus. Full-screen video is reported slow with visible redraw. Exact model/revision and USB VID/PID have not been recorded in a public validation record. |
| Other Revision-A-compatible 480 × 320 USB serial displays | Protocol implementation and simulated device checks | Provisional compatibility; needs confirmation on the specific physical device. |
| Other display revisions or sizes | No completed hardware validation for this release | No broad compatibility claim. |
| TURZX 5.2-inch display with a desktop-monitor function | Owner confirmed all three modes, alpha.11 fixes and subsequent everyday checks including overnight tray operation | Experimental WinUSB JPEG backend. Functional checks passed on the owner's setup; exact tested build/duration, firmware variants and resource traces remain unrecorded. Desktop Mode is not controlled by the app. |
| CPU/GPU/cooling/storage sensors | LibreHardwareMonitor, optional PawnIO and HWiNFO sources | Availability varies by hardware, driver and permissions. Unavailable readings are labelled rather than invented. |
| GIF playback and photo transitions | Software playback checks, owner feedback from earlier builds | Physical speed depends on the device and USB bandwidth. |
| Video | Windows Media Foundation decoding checks and owner report of slow 3.5-inch playback with sideways redraw | Silent PC-decoded widgets. Full-screen serial video may visibly repaint and run below the application FPS ceiling. Measured per-device throughput remains pending. Native-device H.264 remains unimplemented. |

When reporting a working device, include the exact model, USB VID/PID, selected protocol, orientation/rotation, colour mode, Windows version and app version. Also record whether reconnecting and Windows sign-in startup work on that setup.

The owner reported approximately 150 MB RAM on their setup on 5 October 2026. This is an observation, not a benchmark for all themes or large albums.

## TURZX 5.2-inch USB (experimental)

The direct smart-screen backend targets USB VID 1CBE, PID 0050, with a native
720 × 1280 portrait framebuffer. Landscape themes use 1280 × 720. This device
is enumerated separately from COM ports and sends compressed JPEG frames,
not Rev-A RGB565 buffers. The existing latest-frame queue and USB reconnect
workflow apply. The Windows transport uses built-in WinUSB and requires a
compatible driver already installed for the device.

Software checks cover command encryption, payload budgets, orientation,
failed-handshake cleanup, saved theme dimensions and remembered USB identity.
The owner has confirmed basic physical output in all three modes and alpha.11
mode switching on their setup. On 14 October 2026 they also reported overnight
tray operation, restart, sleep/wake, unplug/replug, both screens attached and
repeated mode changes passing. Exact tested build/duration, resource traces and
firmware variants were not provided. Windows
Desktop Mode, SD-card management and H.264 playback are not implemented by this
backend. See TURZX-52.md for setup and the hardware acceptance checklist.

Alpha.18 follows a replacement display family during automatic reconnect only when the remembered screen is absent and exactly one recognized screen of the other family is present. Both families attached, a live connection, manual Stop, an explicit absent-family choice and ambiguous/unrelated serial adapters retain existing selection behavior. Serial recognition uses known screen identifiers/branding, not generic CH340/FTDI adapter scores. Windows enumeration/retry timing can still delay detection. This new handoff has software checks; physical verification remains pending.

## Info Screen package size

Alpha.17 uses the same 4 GiB expanded referenced-asset ceiling when saving and opening `.t3theme` packages. Metadata is limited to 16 MiB. Older versions could save packages larger than their 1 GiB loader limit. Removed/replaced media is now omitted from package snapshots and from extraction of older packages. Hidden widgets, Hybrid layers, embedded photos and imported fonts are retained. Live workspace files are kept for Undo. Per-video import/playback limits remain 512 MiB and 4K sources; a larger package does not raise those limits. Package reads/writes use bounded streaming on background workers and pending-file replacement, retaining extension/path checks.
