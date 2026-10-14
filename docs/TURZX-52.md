# TURZX 5.2-inch integration

## First supported scope

Experimental direct output for Info Screen, Photo Frame and Hybrid on the
TURZX USB family identified as **1CBE:0050**. The physical screen has not been
attached to the development environment. The owner confirmed basic output in all three modes on their screen. Exact
hardware IDs, driver version and broader firmware compatibility remain to be
recorded.

- Native portrait image: 720 × 1280; landscape theme: 1280 × 720.
- Discovery: present Windows USB interfaces, including driver interface GUIDs.
- Transfer: Windows WinUSB bulk endpoints, discovered from interface zero.
- Commands: sync (10), brightness (14), PNG (102), JPEG (101).
- Envelope: 500-byte command, DES-CBC zero padding, 512-byte packet and trailer.
- Images: JPEG (101) for every mode, starting at quality 95 with full chroma
  resolution; reduce quality as needed to keep the image within 1 MiB.
- Frame queue: reuse the existing newest-frame-only queue.
- USB failures: propagate to the existing continuous reconnect controller.
- Existing serial display support remains a separate backend.

The app already uses GPL-3.0-or-later, so the pasted summary's MIT/GPL concern
is outdated. Attribution is recorded in THIRD_PARTY_NOTICES.md.

## Setup

1. Close the vendor TURZX application, including its tray process.
2. If using Windows Desktop Mode, use the vendor app to return to smart-screen
   mode, then close the vendor app.
3. Choose **5.2-inch · 1280 × 720** in the main window top bar.
   The canvas and imported themes fit the selected screen automatically.
4. Both screens can remain plugged in. The selected screen is remembered,
   and a missing 5.2-inch screen does not fall back to a COM device.
5. Leave **USB protocol** and colour compatibility on **Auto**. Press
   **Start display**, then **Run display colour test**.
6. Compare the colours, white border, orientation and text with the preview.

If the device is not listed, obtain its **Hardware IDs** from Windows Device
Manager (device Properties, Details, Hardware IDs). Also record the Driver
Provider and Driver Version. The known smart-screen identity is 1CBE:0050.
The vendor Desktop Mode driver in the supplied archive instead lists
1A86:AD10 and AD11/AD12/AD13 interface 00. These identities are not treated as
interchangeable or automatically switched by this implementation.

WinUSB must already be accessible. The app does not replace a Windows driver,
run Zadig, install the vendor display driver, or modify device settings on SD.
A readable status explains a failed driver-access attempt.

## Dimensions and theme compatibility

Info Screen packages and sessions already save CanvasWidth and CanvasHeight.
Orientation changes now retain the chosen resolution. Photo Frame and Hybrid
settings themes also save canvas dimensions; legacy presets with no dimensions
continue using the active canvas. Imported older themes scale their widget positions, sizes and fonts into the
selected canvas. Images retain their configured fit behavior. Quarter-turn
physical rotation can also add margins if the canvas orientation does not
match the mounting direction. Choose the appropriate theme orientation.

## Not included in the first integration

- MP4/H.264 decoding or streaming.
- SD-card uploads, deleting files or standalone playback.
- Switching into Windows Desktop Mode or replacing its display driver.
- Importing the proprietary TURZX theme format.
- Guaranteed frame rate or physically confirmed firmware compatibility.

The editor's existing GIF/photo pipeline can send rendered image frames, but
that does not constitute hardware H.264 playback support.

## Hardware acceptance

Record exact model label, VID/PID, instance identity, Windows version,
firmware if visible, driver provider/version, mode and results:

1. Colour test, borders, portrait/landscape, 0/90/180/270-degree output.
2. Info Screen, Photo Frame and Hybrid with native-resolution saved themes.
3. Unplug/replug and replug after more than 30 seconds.
4. Windows restart, startup in tray and sleep/resume.
5. At least two hours of photos/GIFs and readings, checking memory and CPU.
6. Exit clearing, then reconnecting without the vendor application.
7. Old 3.5-inch display, including switching device families, as a regression.

## References

- Exact-model Python implementation:
  https://github.com/mathoudebine/turing-smart-screen-python/blob/main/library/lcd/lcd_comm_turing_usb.py
- C# protocol reference:
  https://github.com/usausa/turing-smart-screen/tree/main/LcdDriver.TuringSmartScreen
- Vendor software page:
  https://www.turzx.com/2025/08/07/52_inch/
- Microsoft WinUSB interface discovery:
  https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/how-to-write-a-windows-desktop-app-that-communicates-with-a-usb-device

## alpha.8 follow-up test build

The owner reported successful output in Info Screen, Photo Frame and Hybrid in
alpha.7, followed by Info Screen stopping, slow GIF playback, and unwanted
fallback to the serial screen when both were connected. This is basic physical
output evidence, not a long-duration reliability pass.

alpha.8 moves screen selection into the top bar. The selected screen owns canvas
size across blank/new/imported themes, device discovery, reconnection and startup.
Missing USB hardware no longer silently falls back to the serial screen. Search
opens the selected widget's filtered Source dropdown beside the search box.

The GIF decoder now requests the needed frame directly and reuses valid prior
frames. PNG uses fast lossless compression; the WinUSB transport drains stale
input once on opening rather than waiting before every frame, and skips empty
USB termination packets while awaiting an acknowledgement.

Runtime providers are harvested separately with a bounded wait. A slow provider
keeps its last reading while completed providers and screen updates continue,
without launching overlapping reads for that provider.

Retest: keep both screens plugged in; choose 5.2-inch; load blank and saved
themes; search GPU on a Value widget; play the same GIF at speed 1 and 2; run
Info Screen for at least 30 minutes; switch all modes; unplug/replug 5.2-inch;
restart Windows. Confirm it never changes back to 3.5-inch on its own.

These follow-up physical checks and the actual Windows UI checks are pending.
Desktop Mode, SD-card playback and H.264 are still outside this test build.

## alpha.9 Info Screen output test build

The owner reports that alpha.8 renders Info Screen on the app canvas but shows
no Info Screen output on the physical display; Photo Frame and Hybrid still
work. Dashboard frames typically take the PNG path while photo content can
fall back to JPEG. This difference is a suspected cause, not a confirmed
firmware diagnosis.

alpha.9 sends every TURZX frame using the JPEG command, including simple
Info Screen dashboards, colour tests and clearing. Encoding starts at quality
95 with 4:4:4 chroma to retain coloured text detail and lowers quality only
when needed to fit the 1 MiB payload limit. Native output is opaque, composed
over black. JPEG is lossy; it replaces the previous lossless PNG preference.

Retest first with Info Screen on the 5.2-inch device, then switch to Photo
Frame and Hybrid and back. Let Info Screen run for at least 30 minutes.
Physical output and sustained operation remain unverified for alpha.9.

## alpha.10 editing, mode and I/O test build

This build starts from the exact source embedded in the verified alpha.9
portable ZIP (source 0cf7731e3b8660701cb297e9708f52c50ae96edb). The public
main branch was older and was not used as the 5.2-inch baseline.

- Graph/bar headers use font metrics to position the complete text below a
  fixed top padding. Their height follows the font; no 28/30-pixel cap remains.
  Resizing remains independent of font size. Already selected resize grips do
  not briefly disappear during selection, and selected editor hit targets are
  above overlapping unselected widgets.
- Each new display mode drops waiting old frames and synchronizes the TURZX
  connection before its first JPEG frame. USB reply waiting has one two-second
  budget, including empty packets. Firmware freeze recovery still needs a
  physical long-playback test; an acknowledgement is not proof of displayed
  content.
- Photo Frame and Hybrid retain separate playlist copies, global options,
  watched folders and current positions in private resume state. Switching
  through Info Screen keeps both. Queued events from retired folder watchers
  cannot add pictures to the new mode. Legacy embedded assets are retained
  while inactive modes may still use them. Old inactive playlists not present
  in prior saved metadata cannot be reconstructed automatically.
- GIF files up to 64 MiB keep compressed input in RAM; only one composited
  bitmap is retained per GIF, not every decoded frame. Larger files use the
  streaming decoder to bound per-file memory. Photos retain the existing
  bounded prepared-image cache. Actual Windows Task Manager I/O is unmeasured.
- Recovery packages are frozen on the editor thread, compressed/written on a
  background task, and rewritten only after another edit. Unsaved state alone
  no longer recopies every asset every eight seconds. Themes and durable
  resume/recovery files still need disk storage.

Photo Frame theme extension is .pcphoto; Hybrid is .pchybrid. These reusable
settings themes intentionally do not embed a playlist. Per-mode linked photo
lists survive in the app's private resume state; folder settings can repopulate
photos when loading a reusable theme.

Retest: font 70/80 on Graph and Bar, all resize edges with overlapping widgets,
separate manually added Photo Frame/Hybrid pictures, different watched folders,
mode switching after 30 minutes in each mode, exit/reopen, GIF looping and disk
activity after editing stops. Check both screens remain attached and 5.2 stays
selected. Hardware/Windows UI checks remain pending.

## alpha.11 mode-switch regression fix

The owner confirmed that two alpha.10 problems were resolved, but switching
mode changed Stop display to Start display and left the physical screen on its
old image. Alpha.10 incorrectly sent sync (10) and brightness (14) commands at
mode boundaries. That combined setup belongs at connection time; all three app
modes use the same full-frame JPEG output protocol.

The regression test models a screen that rejects initialization commands once
image playback begins. It reproduced the exact disconnected/Start-display
state with alpha.10. Alpha.11 removes the mid-session handshake interface and
uses only JPEG transfers when changing content. Initial connection still
performs its original handshake. Waiting frames from old modes are still
invalidated; font, resize, independent-playlist and RAM-cache fixes remain.

The corrected test keeps the actual controller and TURZX driver connected
through 50 mode frames, retains Stop display, checks the new image's decoded
colour, and rejects any attempted mid-session initialization. This is a
simulated firmware condition, not a confirmed diagnosis of the physical
screen's internal behaviour. Actual mode switching and long-duration output
still require the owner test.

## Brightness control, alpha.19

The top-bar Brightness button sends command 14 only for an explicit brightness change or initial connection, without a sync/reset command. The 1CBE:0050 family retains the exact-model Python reference's 0..102 byte mapping for a 0..100% UI value. It does not use the serial family's inverted 0..255 mapping. Initial setup applies the saved USB value directly. Slider edits are debounced and share the image I/O gate. Changing modes still sends no brightness command. A rejected brightness request reports failure and preserves the image session; software checks exercise subsequent JPEG output even on the earlier simulated rejecting firmware. Physical dimming and firmware response remain to be confirmed.

Reference: [exact-family SetBrightness implementation](https://github.com/mathoudebine/turing-smart-screen-python/blob/main/library/lcd/lcd_comm_turing_usb.py).

## Owner follow-up, 2026-10-14

The owner reports alpha.11 working correctly on their setup. This confirms the mode-switch fix for that setup. Alpha.12 changes interface styling only, keeping the alpha.11 USB and rendering paths.

In a subsequent 14 October report, the owner confirmed overnight tray operation, restart, sleep/wake, unplug/replug, both screens connected and repeated mode changes passed. Exact build/duration, model revisions and CPU/memory traces were not supplied. Switching families still needed manual model selection. Alpha.18 adds automatic handoff when the remembered device is gone and only one recognized screen of the other family is attached, while preserving selection when both are present. That new behavior has simulated-device checks and awaits physical verification. Broader firmware compatibility remains unverified.
