# `.t3theme` format v1

`.t3theme` is a ZIP-compatible package with a custom extension.

## Security rules

A theme may contain declarative configuration and passive assets only. It must never execute scripts, assemblies or programs.

Allowed asset categories are currently image, GIF, video and font. Future importers must validate file type, size and decoded dimensions before processing.

All package paths are normalized and extraction is constrained to the temporary theme workspace.

## Root files

### `manifest.json`

Small package header used for quick compatibility checks.

```json
{
  "format": "PCInfoScreenStudio",
  "formatVersion": 1,
  "minimumAppVersion": "0.10.0-alpha.13",
  "themeName": "Example"
}
```

### `theme.json`

Contains canvas settings, device rotation, widgets and the asset index.

## Assets

Assets use unique package paths such as:

```text
assets/image/3fa8....png
assets/gif/111a....gif
assets/video/e72d....mp4
assets/font/018f....ttf
```

The user's original filename is retained as metadata but is not used as the archive path.

## Fonts

Custom fonts are embedded as passive TTF/OTF assets. A theme author is responsible for making sure the font licence permits redistribution.

## Video

Video assets are passive files decoded silently by Windows Media Foundation in alpha.14. H.264 MP4 is recommended. Old placeholder widgets now attempt playback. Audio is deselected; a video file can still contain an audio track. Files are not transcoded or stripped during import. Loop, speed, FPS and MediaPaused are declarative widget settings; runtime frames/status are not serialized.

## Feature compatibility and other formats

Hardware Name widgets record `0.10.0-alpha.13` as their minimum app version. The example above shows a package using current features; older packages may have an older minimum. Format version remains 1. New and loaded canvases fit the selected 3.5-inch or 5.2-inch display in the editor.

Photo Frame uses `.pcphoto` settings; Hybrid uses `.pchybrid` settings and overlays. `.pcalbum` saves linked photo references and playlist settings. These files do not embed the photo collection. See the [product specification](PRODUCT_SPEC.md#saving-and-sharing).

Video widgets imported by alpha.14 record `0.10.0-alpha.14` as their minimum app version. No package format-version bump is required.
