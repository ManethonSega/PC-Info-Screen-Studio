# Device compatibility

This catalogue separates owner observations from repeatable automated checks. Brand name or diagonal size alone does not identify a display protocol.

| Device or workload | Evidence | Current position |
| --- | --- | --- |
| Owner's 3.5-inch, 480 × 320 Turing/TURZX-style USB serial display | Owner reported correct output and colours, and many working sensors after PawnIO installation | Revision-A device path is the current supported focus. Exact model/revision and USB VID/PID have not been recorded in a public validation record. |
| Other Revision-A-compatible 480 × 320 USB serial displays | Protocol implementation and simulated device checks | Provisional compatibility; needs confirmation on the specific physical device. |
| Other display revisions or sizes | No completed hardware validation for this release | No broad compatibility claim. |
| TURZX 5.2-inch display with a desktop-monitor function | Discussed, but not integrated or validated here | Not supported by this portable alpha's USB serial output path. |
| CPU/GPU/cooling/storage sensors | LibreHardwareMonitor, optional PawnIO and HWiNFO sources | Availability varies by hardware, driver and permissions. Unavailable readings are labelled rather than invented. |
| GIF playback and photo transitions | Software playback checks, owner feedback from earlier builds | Physical speed depends on the device and USB bandwidth. |
| Video | Import and placeholder only | Full decoding/playback is not implemented. |

When reporting a working device, include the exact model, USB VID/PID, selected protocol, orientation/rotation, colour mode, Windows version and app version. Also record whether reconnecting and Windows sign-in startup work on that setup.

The owner reported approximately 150 MB RAM on their setup on 5 October 2026. This is an observation, not a benchmark for all themes or large albums.
