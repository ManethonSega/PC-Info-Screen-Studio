# Matrix bitmap fonts

The Matrix renderer uses editable binary glyph rows in `Assets/MatrixFonts.json`, never a scaled desktop fallback font. Every widget saves independent four-, five- and ten-dot settings. The selected widget exposes the menu for its actual row height; edits affect only that widget. Earlier workspace-wide values are read as migration defaults for missing tile fields, while explicit tile values (including enum value zero) take precedence. Existing numeric five-dot selections remain compatible.

| Height | Choices | Source and terms |
| --- | --- | --- |
| 4 | mem 4×4, mem mono 4×4 | [oidoid/mem](https://github.com/oidoid/mem), AGPL-3.0; full license bundled |
| 4 | Able 4 | [Joseph Fatula / stormgold](https://stormgold.itch.io/able-4-font), software embedding allowed with designer credit |
| 4 | Limitdot H4 | [Masubiwa](https://masubiwa.itch.io/limitdot-h4), included for development at the owner’s request; redistribution grant unverified, TTF metadata says All Rights Reserved |
| 5 | Tom Thumb | [Adafruit source](https://github.com/adafruit/Adafruit-GFX-Library/blob/master/Fonts/TomThumb.h), BSD; notice bundled |
| 5 | PixelFont 5×5 Thin | [mcer12/PixelFonts](https://github.com/mcer12/PixelFonts), MIT; notice bundled |
| 5 | Petty 5H, Petty 5×5 | [TakWolf/retro-pixel-font](https://github.com/TakWolf/retro-pixel-font), OFL-1.1; notice bundled |
| 5 | Able 5 | [Joseph Fatula / stormgold](https://stormgold.itch.io/able-font), software embedding allowed with designer credit |
| 5 | mem 5×6 | [oidoid/mem](https://github.com/oidoid/mem), AGPL-3.0; full license bundled |
| 10 | PixelFont 5×5 Thin ×2 | PixelFonts five-row glyphs doubled in both axes |
| 10 | PixelFont 5×10 Condensed | PixelFonts five-row glyphs doubled vertically only |
| 10 | Micro 3×10 | [u8g2 micro.bdf](https://github.com/olikraus/u8g2/blob/master/tools/font/bdf/micro.bdf), public domain; five-row glyphs doubled vertically only |
| 10 | piconano 3×10 | [picocherry/picotype](https://github.com/picocherry/picotype), included for development at the owner’s request; redistribution grant unverified; doubled vertically only |

## Pixel adaptations

- Source glyphs are sampled at native pixel-cell centers, without antialiasing or synthetic four-to-five-row stretching.
- Petty has uppercase letters. Lowercase text falls back to its corresponding capital, rather than a different typeface.
- Descenders that fit the target band move within it. mem 5×6 normally needs six rows for Q; the five-row app variant uses `111 / 101 / 101 / 111 / 001`. The control explains this adjustment. Other glyphs retain their native pixel shapes.
- A small degree symbol is supplied where a source omits it, for generated sensor temperatures. Unknown characters use the selected font's question mark.
- Word gaps count actual dark columns, including empty side columns inside monospaced cells. Repeated spaces still leave three dark LEDs.
- The original Tom Thumb / PixelFonts source asset remains in the repository as `MatrixFiveDotFonts.json`; the application embeds the consolidated `MatrixFonts.json` instead.

## Development inclusion and release review

All fourteen bitmap choices are embedded and work immediately. At the project owner's explicit request, the development build also includes Limitdot H4 and piconano-derived glyphs. No local font-file loading step is required. This instruction is a development-scope decision, not evidence of a redistribution license. Permission for these two sources remains unverified and must be resolved before a public distributable release. Source links and the observed TTF metadata are retained above and in the bundled notices. The original TTF files are not bundled.

## Per-tile alignment

Text, sensor values and UTC time use each tile’s saved Left, Centre or Right alignment. The content region clips overflow, and long scrolling text uses the full viewport from its first frame. Pixel art retains its authored coordinates. Invalid alignment values recover to Left.

## Two-row geometry

| Top + bottom | Top content | Empty rows | Bottom content |
| --- | --- | --- | --- |
| 4 + 4 | 0–3 | 4–5 | 6–9 |
| 5 + 5 | 0–4 | none | 5–9 |
| 4 + 5 | 0–3 | 4 | 5–9 |
| 5 + 4 | 0–4 | 5 | 6–9 |

Rows above are zero-based. The controller always receives six native 30×5 tile buffers. Gaps are black inside those buffers; hidden source artwork is retained. Pointer selection, keyboard selection, painting, image import, previews and USB output share the same content regions.

## Verification

`PCInfoScreenStudio.MatrixProtocolChecks` checks all fourteen bundled bitmap masks, proportions, word gaps, row combinations, artwork preservation, packet fixtures and settings serialization on any supported host. Windows controller checks additionally exercise the actual WPF selectors, compact captures and exact preview-to-HID identity in every two-row layout. Physical Matrix output still requires hardware qualification.

## Free widget canvas (alpha.33)

`Widgets` distinguishes the new ordered software-layer document from an older fixed tile layout. A null list migrates once, copying prior content rectangles and fonts; an empty list stays intentionally empty. Original tile settings remain available in the saved document. Every widget has a stable ID, an integer X/Y position, a 1–90-dot width and a 4-, 5-, 6- or 10-dot height. Six-dot text keeps the selected five-dot glyph intact and adds one blank row above it. Validation caps the document at 64 layers and bounds the complete widget inside 90×10. Pixel-art source buffers use a fixed 90×10 stride, so reducing width/height does not delete hidden artwork. Existing bottom-row hidden art is rotated behind visible rows during migration.

The last visible layer draws in front. Empty pixel-art cells are transparent, while text uses its actual glyph coverage. Each frame is composited into one 90×10 pixel widget for the existing HID driver. Locks protect interaction without changing the rendered image. Native and emoji text caches are bounded; complete graphemes are shaped in Segoe UI Emoji and reduced to the chosen LED height. Emoji retain their palette rather than inheriting the text colour.

## Content-fitted widths and full readings (alpha.34)

Free text, sensor and clock widgets default to `AutoWidth`. The renderer measures
the cached raster’s occupied columns, trims outer blank columns, caps the width
at 90 and bounds X so growing content stays on-screen. The view model applies
the same measurement to selection/hit rectangles after edits and live samples.
`AutoWidth=false` retains a custom alignment/scroll viewport. Pixel art keeps its
explicit drawing width and source stride. Fixed-tile migration uses manual width
to preserve prior pixels; alpha.33 widget files default to automatic fitting.

`Prefix` belongs to each sensor layer, and the software compositor receives both
a numeric alarm value and the formatted source text, with its actual unit. The
full Info Screen catalog is shared rather than translated to the four firmware
sensor IDs. Extended source keys are preserved during validation and restart,
including while advanced sources have not yet been rediscovered. All transport
content remains a single ordinary pixel-art frame.
