# Device compatibility

This catalogue separates owner observations from repeatable automated checks. Brand name or diagonal size alone does not identify a display protocol.

| Device or workload | Evidence | Current position |
| --- | --- | --- |
| Owner's 3.5-inch, 480 × 320 Turing/TURZX-style USB serial display | Owner reported correct output and colours, and many working sensors after PawnIO installation | Revision-A device path is the current supported focus. Exact model/revision and USB VID/PID have not been recorded in a public validation record. |
| Other Revision-A-compatible 480 × 320 USB serial displays | Protocol implementation and simulated device checks | Provisional compatibility; needs confirmation on the specific physical device. |
| Other display revisions or sizes | No completed hardware validation for this release | No broad compatibility claim. |
| TURZX 5.2-inch display with a desktop-monitor function | Owner confirmed initial smart-screen output in all three modes in alpha.7, with stoppage/animation/selection issues reported | Experimental WinUSB backend; alpha.11 retains JPEG output and removes mode-change setup that disconnected playback in alpha.10. The owner confirmed the large-font and independent-playlist fixes; the owner reports alpha.11 working correctly on their setup, including mode switching. Broader compatibility and long-duration evidence are still limited. Desktop Mode is not controlled by the app. |
| CPU/GPU/cooling/storage sensors | LibreHardwareMonitor, optional PawnIO and HWiNFO sources | Availability varies by hardware, driver and permissions. Unavailable readings are labelled rather than invented. |
| GIF playback and photo transitions | Software playback checks, owner feedback from earlier builds | Physical speed depends on the device and USB bandwidth. |
| Video | Import and placeholder only | Full decoding/playback is not implemented. |

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
Physical output, firmware variants and real unplug/replug are pending. Windows
Desktop Mode, SD-card management and H.264 playback are not implemented by this
backend. See TURZX-52.md for setup and the hardware acceptance checklist.
