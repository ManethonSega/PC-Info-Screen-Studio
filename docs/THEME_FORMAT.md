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
  "minimumAppVersion": "0.1.0",
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

The final media pipeline will store a silent display-optimized representation. Audio is explicitly outside the theme format's functional scope.
