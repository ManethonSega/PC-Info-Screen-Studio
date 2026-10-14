# Alpha validation

This document describes what the release workflow verifies and what still needs a person using a physical screen. The exact successful workflow and source SHA are recorded in the portable package's `build-info.json`.

## Alpha.20 stability checks

See [STABILITY.md](STABILITY.md) for validation scope and exact memory/storage limits. New checks exercise corrupt and missing primary/backup settings/session data, backup preservation, older settings, invalid/cancelled writes, concurrent service saves and workspace disposal during asset streaming. WPF checks cover recovery notices, shared command state, editable snapshot saves, footer Cancel/progress, failed Open and cancelled imports. Cache checks cover lease safety, category accounting, active resource preference, GIF snapshots/timeline and 500 frames cycling 30 photos/two GIFs under an 8 MiB retained-cache test budget. Process-memory observations are logged without claiming a complete process-memory cap or overnight hardware validation. Windows/controller/WPF and EXE/ZIP checks gate release.

## Alpha.19 brightness checks

Simulated devices check legacy 50% defaults, clamping and family-specific persistence, initialization at the saved level, rapid edits and edits during an in-flight command, disconnected presets, reconnect restoration, zero-to-full adjustment, rejection handling and router isolation. USB checks decrypt command 14 and compare its payload at the endpoints and intermediate percentages, then verify JPEG output continues without another sync. The mode-switch rejecting-firmware regression retains its initialization-only mode behavior and also checks output after a rejected explicit brightness request. WPF checks open the top-bar popup, commit delayed and final edits, use Reset/Escape, switch families, confirm themes are unchanged by brightness, and check toolbar fit at minimum width. The suite captures the actual popup. Full Windows/controller/WPF and EXE/ZIP checks gate release. No physical serial or WinUSB brightness result is claimed; actual range/firmware response needs an owner check.

Candidate validation passed on **5 October 2026** in [Windows run 37367471392](https://github.com/ManethonSega/PC-Info-Screen-Studio/actions/runs/37367471392), including the extracted portable ZIP. The release workflow repeats these checks for its tagged source.

UI-polish candidate checks passed on **6 October 2026** in [Windows run 37420665087](https://github.com/ManethonSega/PC-Info-Screen-Studio/actions/runs/37420665087), with zero compiler warnings or errors. The alpha.6 release workflow repeats these checks for its tagged source.

## Automated Windows checks

| Workflow | Evidence provided by the checks |
| --- | --- |
| Empty states and property panels | Actual WPF Photo Frame and Hybrid prompts appear and disappear as photos and overlays change. They are absent in Live view; blank property editors hide when no widget is selected. |
| Alignment icons | All six vector buttons retain single-widget canvas alignment and multi-selection alignment, with accessible names and descriptive tooltips. |
| Numeric editing | Actual numeric fields reject invalid dimensions, opacity, range endpoints, photo timing and caption settings before their setters clamp them. Inline feedback appears while typing, corrections clear it, finite-number checks reject malformed values, and comma decimals honour the field culture. Invalid drafts preserve the last valid model value. |
| Configure a selected widget | The actual search box filters the Source dropdown. GPU and multiword queries, empty results, Clear, advanced sensors and switching widgets preserve the current sensor until an explicit choice; Hybrid saves/reopens that choice. Search never adds widgets. |
| Add, edit and save Info Screen | Direct Add command creates a widget; the application save path writes it into the reopened `.t3theme`. |
| Save Photo Frame | The app saves global caption settings and retains the linked playlist. `.pcphoto` files exclude photos. |
| Add and save Hybrid | The app saves an added shape and its exact position into `.pchybrid`. |
| Switch modes and reopen | Independent photo settings, theme paths, dirty state, Info Screen layers, Hybrid positions and imported assets survive session save and recreation. |
| Tray workflow | An actual WPF window is shown, minimized, restored, closed to tray, restored again and exited; a new view model restores its session. |
| Reconnect | A fake display temporarily rejects a connection; automatic error handling, physical-device identity persistence across a changed COM number, and a subsequent successful reconnect are verified. No physical USB transfer is proved. |
| Windows startup | An isolated per-user, interactive task is registered, inspected and deleted on the Windows runner. Single-instance activation and noninteractive scheduled startup policy are checked. This does not simulate a real reboot or a user's UAC approval. |
| UI readability | Actual selected-item templates and tooltip colours are inspected, and real WPF controls are captured for review. The checks do not claim that every OS configuration has been reviewed. |
| Published package | The self-contained executable starts in smoke-test mode, the portable ZIP is extracted and the extracted executable starts again. Required files, native rendering library, fonts, licences and the verified PawnIO prerequisite are checked. |

## Physical-device and first-time user checklist

Use dated owner records below for completed hardware checks. Uncovered checks, new changes and first-time-user validation remain pending. CI success is not a hardware-reliability result.

1. With other screen software closed, detect and connect the actual display. Check orientation, colours and live values.
2. Add a text, value and circular gauge, resize them, choose sources, save and reopen the Info Screen theme.
3. In Photo Frame, add a folder, change captions and timing, save and reopen. Confirm the original photos stay on the PC.
4. In Hybrid, add and position overlays, save and reopen. Confirm both the folder and exact positions.
5. Close to tray, restore, then unplug/replug and reconnect the screen.
6. Exit, reopen, then restart Windows. Confirm the last mode/theme restores and output resumes after sign-in with the approved startup task.
7. Run a representative album and sensor theme for several hours in the tray; note responsiveness, memory and any missing readings.
8. Ask someone unfamiliar with the app to complete connection, adding and saving without instructions. Record where they hesitate.

Report issues with app version, Windows version, device details, exact steps and whether the problem occurs on the preview, physical screen or both. Local crash/startup logs exist under `%LOCALAPPDATA%\PCInfoScreenStudio\Logs`; there is no automatic remote crash reporting.

## Owner everyday hardware record, 2026-10-14

The owner reports the following checks passed on their setup. The tested app build, exact overnight duration, device revisions and CPU/memory traces were not specified; this record does not attribute the overnight result to alpha.17 or alpha.18.

| Check | Owner result |
| --- | --- |
| Overnight tray operation | Passed |
| Restart | Passed |
| Sleep/wake | Passed |
| Unplug/replug | Passed |
| Both screens connected | Passed |
| Repeated mode changes | Passed |

Switching display families still required manually choosing the model before output resumed. The 3.5-inch video looked like a slow slideshow with left-to-right frame replacement. These are separate follow-ups, not reasons to repeat the passed everyday checks. Changed-COM-number, sign-in-task details and resource trends were not separately documented.

Alpha.18 adds conservative automatic handoff within the existing reconnect loop, after its manual Stop guard. Simulated devices check serial-to-USB and USB-to-serial reconnect/output, saved family/identity and notification order, both-family selection retention, live-connection retention, ambiguous replacements, unrelated serial adapters and explicit selection of an unavailable family. The existing selected-port notification resizes the canvas before connecting. Full Windows/controller/WPF and EXE/ZIP checks gate release. Physical verification of this new handoff is pending.

## TURZX 5.2-inch integration

The Windows harness now includes an independently calculated OpenSSL DES
reference vector, image packet length/trailer checks, failed acknowledgement
cleanup, native frame dimensions, aspect-preserving rotation, bounded JPEG
fallback, resolution persistence and changed-interface USB identity checks.
These use a simulated USB transport. They do not exercise physical WinUSB
endpoints. Exact screen identity, driver accessibility, frame orientation,
colour fidelity, sustained updates and restart/reconnect remain pending on
real hardware. See TURZX-52.md.

### Local validation of the unmerged alpha.7 test build

The application and Windows controller harness cross-compiled with .NET SDK
10.0.100, with zero warnings and errors. A portable console harness ran against
the production protocol and driver sources with a simulated USB transport and
real SkiaSharp image encoding. It passed command encryption, image payload
length, acknowledgement handling, all four rotations, PNG/JPEG payload budget,
USB release and failed-handshake cleanup checks. Reflection checks against the
actual compiled app assembly passed high-resolution canvas resizing,
orientation, serialization and Hybrid settings-theme resolution restoration.

The Windows x64 self-contained application was published locally and the ZIP
contents verified. The full WPF workflow executable and packaged EXE were
**not run** because this host is Linux. Physical WinUSB I/O is also untested.
GitHub publication was blocked by automatic approval review; the test build is
provided directly with its matching source. Do not treat this as a CI-validated
public release.

### Local alpha.8 regression validation

The application and WPF check project cross-compiled with zero warnings/errors.
The Linux console regression harness used the actual DeviceController source
with a fake display and a minimal dispatcher substitute. It passed 100 device
discoveries with both screens, missing-selected-screen protection, changed USB
path reconnection, persisted screen preference and switching back to serial.

Actual Skia GIF decoding matched fresh reference decodes for skip/backward/loop
positions at speeds 1, 2 and 4, using keep/background/previous disposal GIFs.
Fast PNG payloads decoded losslessly and remained within the transfer budget.
Protocol, rotation, connection cleanup and assembly-based theme checks passed.
The WPF search/canvas tests compile but were not executed on this Linux host.
The packaged EXE and real USB endpoints still require Windows/hardware testing.
There is no new public GitHub release or CI result for this local test build.

### Local alpha.9 Info Screen output validation

The reported failure occurs on the physical screen while the app canvas still
renders. The suspected PNG/JPEG decoder difference is addressed by selecting
JPEG command 101 for all TURZX frames. The exact firmware cause is unconfirmed.

Checks cover native JPEG dimensions, payload lengths, full chroma resolution,
fine coloured dashboard lines with low average encoding error, noisy-photo
compression limits, transitions between dashboard/photo/hybrid-like frames,
100 consecutive dashboard updates, and clearing. The simulated transport
rejects PNG command 102, and each frame payload is actually decoded with Skia.
Protocol, device selection, GIF composition and assembly-based theme checks
are also retained. The app and Windows harness cross-compile locally.

These checks do not verify physical USB output. The Windows UI executable,
packaged EXE and owner retest remain pending. This is a local test package,
with matching source included, not a new GitHub release or Windows CI result.

### Local alpha.10 regression validation

Portable checks link the actual renderer, models, session controller, device
controller, GIF decoder and TURZX driver. Linux stubs replace WPF dispatch and
Windows-only default discovery, and the transport is simulated. Checks cover
complete Orbitron glyphs at graph/bar font sizes 22/70/80, independent playlist
and mutable item snapshots, JSON resume/positions, queued slow-transfer mode
changes, and four mode boundaries followed by 100 real Skia JPEG decodes.

GIF disposal 1/2/3 fixtures match fresh reference decodes at speeds 1/2/4 with
skip/backward/loop positions. Each of three 300-decode RAM loops added only 103
process read bytes on this Linux host (reading the I/O counter itself), rather
than repeatedly reading the source. This is not a Windows Task Manager result.

The application and Windows harness cross-compile locally. WPF grip/capture and
edit-only recovery-write tests are included but cannot execute on Linux. The
packaged EXE, actual driver timeouts, physical mode switch after long playback,
and the user's reported 29.7 MB/s activity still require Windows/device checks.
No public GitHub release or CI result is claimed for this local package.

### Local alpha.11 mode-switch regression validation

A new transport test deliberately rejects sync/brightness initialization after
image playback has started. The alpha.10 production controller/driver failed
this test by releasing the connection and changing its action label to Start
display. After removing mid-session initialization, the same test passes with
50 mode frames, the live session retained, Stop display unchanged, and the new
frame colour decoded from the payload. The complete run contains one initial
sync/brightness handshake followed only by JPEG image transfers.

The delayed-transfer/newest-mode queue test and prior font, playlist and RAM-GIF
checks are retained. The app and Windows test harness cross-compile locally;
the Windows EXE and real USB output are not executed on this Linux host. No
Windows CI result or public release is claimed. The physical firmware reason
for the reported disconnect remains unconfirmed without a hardware trace.

## GitHub consolidation, 14 October 2026

The alpha.7 through alpha.11 source has been recovered from alpha.11's
included source snapshot for publication. The Windows build workflow runs
the complete WPF/controller harness, publishes the self-contained app and
smoke-tests both the executable and extracted ZIP before releasing alpha.11.
Its result is available in GitHub Actions and the ZIP build-info.json. The
local-build sections above record historical checks, not current CI results.

The owner reported that two alpha.10 fixes succeeded: large-font editing and
independent Photo Frame/Hybrid photos. They also reported a mode-change
disconnect. Alpha.11 addresses that regression in simulated transport checks;
the owner subsequently reported on 14 October 2026 that alpha.11 works correctly on their setup, including mode switching. Sustained GIF speed, Windows
disk activity, restart and sleep/resume remain unmeasured on the real device.

## Alpha.14 video and diagnostic checks

The Windows suite adds generated H.264/AAC decoding with actual frame-pixel/vertical-orientation checks, pause/unpause, EOF, loop/speed, decoder bounds, inactive-mode cleanup and bad-media handling. Export checks inspect the JSON schema/counters and verify private exception messages/paths are absent. UI captures show the actual Video controls and Export diagnostics button. CI build, controller/WPF, published EXE and portable ZIP checks must pass before publication. This does not establish physical video throughput or long-duration reliability; test those on both serial and 5.2-inch USB screens.

## Alpha.15 fragmented video regression

The generated fragmented 720p H.264 fixture reproduced the same “Changing video dimensions are not supported” failure against alpha.14 on Windows CI (PR #26 initial check). The source clip supplied for troubleshooting also decoded without errors in a local independent decoder and its encoding/container metadata was inspected. The customer clip is not published in the repository. The corrected decoder is checked with the generated matching-format fixture at native 1280 × 720 USB size and scaled 480 × 270 serial size, including RGB orientation, pause, progression, EOF, loop and cleanup. Diagnostic schema 2 checks active metadata, retained failure details and continued exclusion of private media paths/messages. Full Windows/controller/WPF and published EXE/ZIP checks are required before release. Physical-device and exact customer-clip playback still need owner confirmation.

## Alpha.16 4K and live playback controls

Owner diagnostics identified the unavailable larger file as 3840 × 2160, rejected by the previous Full HD input cap; no decoder error for the preceding freeze was included. Alpha.15 already clamped requests of 21 and 22 to 20 fps, so the reported threshold does not prove a 22 fps transport failure. A generated fragmented 4K H.264 fixture exercises downscaling to USB 1280 × 720 and serial 480 × 270. Live 20/21/22/20/Auto/5/20 edits check continuing frame progression across loops. Stopped-worker Loop/FPS edits and Restart test recovery without removing or reimporting a widget. Actual WPF controls check per-family validation, Auto clearing manual FPS and unchanged GIF limits. Diagnostic schema 3 retains path-free controls, timestamp/progress and recent disposed sessions. Full Windows/controller/WPF and EXE/ZIP checks gate release. The exact freeze remains unreproduced; the owner should export while frozen, before Restart/removal. The larger customer clip and its codec were not available for exact-file testing. Physical 4K decode load and sustained throughput need owner validation.

## Alpha.17 large legacy theme packages

Owner screenshots show an approximately 1.1 GB Info Screen package rejected by the old 1 GiB extraction guard. The saver previously had no corresponding budget and included every imported asset after widget deletion. A generated legacy format-1 package has four referenced video-kind byte payloads of 256 MiB + 1 byte each (1 GiB + 4 expanded bytes), including hidden and Hybrid widgets. The regression checks opening, save/reopen and unchanged original archive bytes; payloads test packaging, not codecs. Additional cases cover removing unused media from saved metadata/entries, retaining the font palette and embedded photos, preserving live files for Undo, skipping oversized orphan entries in older archives, matching save/load budget enforcement, malformed declared lengths, path traversal, cancellation and preserving an existing destination after failed saves. Package I/O is streamed on background workers. Full Windows/controller/WPF and EXE/ZIP checks gate release. The owner’s actual large `.t3theme` was not attached, so exact-file reopening still needs owner confirmation.
