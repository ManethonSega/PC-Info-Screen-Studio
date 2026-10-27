# Matrix bitmap fonts

The Matrix renderer uses editable binary glyph rows in `Assets/MatrixFonts.json`, never a scaled desktop fallback font. Four-, five- and ten-dot settings are saved independently. The selected tile exposes the menu for its actual row height. Existing numeric five-dot selections remain compatible.

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
