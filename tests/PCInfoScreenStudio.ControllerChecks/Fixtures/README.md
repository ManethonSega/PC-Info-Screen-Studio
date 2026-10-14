# Generated video fixture

`video-colours.mp4` is an original synthetic 160 × 96 H.264/AAC test clip generated with FFmpeg lavfi colour and sine sources. The top/bottom colours change from red/blue to green/yellow after 0.6 seconds, testing RGB order, vertical orientation and timestamp-based playback. The included sine audio checks a container with an audio track while the app selects only video. No third-party media content or FFmpeg binary is distributed.

`video-fragmented-720p.mp4` is an original generated 1280 × 720 H.264 High-profile clip with B-frames, one reference frame, BT.709 metadata, no audio, and a 30000/1001 frame rate. It uses fragmented MP4 (`+frag_keyframe+empty_moov`, `iso5`), matching the reported alpha.14 playback failure. Top/bottom colours change from red/blue to green/yellow halfway through. No customer media is included.

Generation uses FFmpeg lavfi colour sources (four 1280 × 360 inputs, 0.8 seconds each), vstack/concat, libx264 with `-profile:v high -level:v 3.1 -pix_fmt yuv420p -bf 2 -refs 1 -g 24 -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 -video_track_timescale 30000 -movflags +frag_keyframe+empty_moov`. FFmpeg is a fixture-generation tool only and is not bundled with the application.

`video-fragmented-4k.mp4` is an original 3840 × 2160 H.264 High-profile fixture made by scaling the generated 720p fixture, using level 5.1 and fragmented MP4. It tests Windows decoding followed by USB/serial-sized RGB output without retaining a 4K bitmap. Generation: `ffmpeg -i video-fragmented-720p.mp4 -vf scale=3840:2160 -c:v libx264 -profile:v high -level:v 5.1 -pix_fmt yuv420p -bf 2 -refs 1 -g 24 -video_track_timescale 30000 -movflags +frag_keyframe+empty_moov video-fragmented-4k.mp4`.
