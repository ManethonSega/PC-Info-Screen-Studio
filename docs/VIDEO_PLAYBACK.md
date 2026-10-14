# Video playback, alpha.16

Video is a silent widget in Info Screen and Hybrid. Photo Frame continues to use photo playlists. Existing themes with video assets attempt playback instead of rendering only a placeholder. Loop, speed, FPS, pause and restart controls are available. Playback state is independent for each widget, even when two widgets share a file.

## Decode and output

Windows Media Foundation Source Reader runs on dedicated MTA workers through Vortice.MediaFoundation 3.8.3. Only the first video stream is selected; audio streams are deselected and no audio output is created. The advanced video processor converts to RGB32 at a bounded, aspect-preserving display size. Output-format notifications refresh and validate the RGB32 frame size and stride before the associated sample is copied. Normal metadata negotiation, including fragmented H.264 MP4 headers, does not stop playback. A changed output that exceeds or differs from the requested bounded size is still rejected. Samples are scheduled from their presentation timestamps, with playback-speed changes integrated into the media clock. Presentation work is sampled at the selected rate, while interdependent compressed frames still need decoding.

There are at most two active decoders and one cached current bitmap per widget. A semaphore also caps retiring/new workers during rapid mode or theme changes. Renderer snapshots are owned copies; a worker cannot dispose a bitmap being drawn. Hidden/inactive mode widgets, replaced workspaces and stopped preview/output release video state. A paused widget retains its frame; restart opens a fresh timeline. Non-looping clips retain their last displayed frame.

| Family | Auto rate | Hard maximum | Transport |
| --- | --- | --- | --- |
| 3.5-inch serial | 3 fps | 5 fps | Existing Revision-A RGB565 path |
| 5.2-inch USB | 12 fps | 20 fps | Existing WinUSB JPEG path |

Requested rates are clamped to the family maximum, then reduced using the moving average of completed frame-transfer time (with headroom). The USB sender retains only the newest pending frame. These engineering defaults do not claim measured physical frame rates. Large frames, rotation, content and device firmware can lower throughput.

The 5.2-inch device's native H.264/SD-card capability is a separate protocol feature. This implementation keeps PC-side compositing so video can coexist with widgets and overlays. It does not switch the display into vendor desktop mode or send native H.264 streams.

## Scope and limits

H.264 MP4 is recommended. Other Windows-supported containers/codecs may work, but are not promised by extension alone. Windows N can require Microsoft's Media Feature Pack. No FFmpeg/codec binaries are bundled. Imports are limited to 512 MiB and source video to 4K (3840 × 2160 or portrait equivalent, subject to installed codec support); output is bounded by the selected canvas. The PC still decodes the original compressed source, so 4K can use more CPU/native decoder memory than 720p even though cached RGB bitmaps stay screen-sized. 720p is recommended for lower decode cost. Info Screen import copying is asynchronous and guarded against overlapping imports/workspace replacement. Hybrid overlays link to the original video instead of copying it; `.pchybrid` saves that link and reopening resolves it independently of the previous workspace. Keep linked files available. Missing files show a restore/restart instruction.

This first stage has no audio, trim/crop timeline, scrubbing, playlist videos, native-device H.264 playback or codec installer. Unsupported, corrupt and changing-dimension streams show a readable status. Native decoder cancellation is cooperative between sample reads; a slow Windows decoder can delay resource release, while the editor stays responsive and the worker limit remains enforced.

## Validation

A generated fragmented 1280 × 720 H.264 High-profile, 29.97 fps, B-frame, BT.709 fixture reproduces the alpha.14 format-notification failure. Regression checks exercise USB-size and serial-scaled output, pause, movement, EOF, looping and cleanup. The original generated H.264/AAC fixture checks actual RGB pixels and vertical orientation, pause/unpause, timestamp progression, non-looping EOF, loop/speed changes, bounded frames, decoder limits, inactive cleanup and unsupported media. Windows CI also exercises the existing WPF/controller workflows and EXE/portable ZIP startup. Physical smoothness, long-duration video memory and real reconnect behaviour still need checks on both displays.

Primary implementation reference: [Microsoft, using the Source Reader](https://learn.microsoft.com/en-us/windows/win32/medfound/processing-media-data-with-the-source-reader). Binding source and licence: [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows).

Diagnostics schema 3 includes active decoder stages, stable failure codes, source/output dimensions, stride, format-notification counts, published-frame counts and last reader flags. It also captures requested/effective FPS, Auto/Loop/Pause/Speed, sample and media-clock timestamps, sample counts, frame age and stopped-worker state. The last ten disposed sessions retain this path-free progress snapshot after removing/replacing a video. Recent decoder errors retain that numeric snapshot after the worker exits. File names, source paths and raw exception messages remain excluded.

The FPS editor validates 0 to 5 for serial video and 0 to 20 for USB video; GIFs retain 0 to 60. Selecting Auto clears a manual rate to 0, and committing a positive rate leaves Auto. Existing themes with larger FPS requests still load, with backend clamping and actual effective FPS shown in status/diagnostics. Healthy decoder timelines continue through FPS edits. Explicit playback edits reopen ended/failed workers; Restart video can be used without removing or importing the widget again. These recovery cases do not establish the cause of an unreproduced native decoder stall.
