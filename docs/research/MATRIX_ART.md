# Matrix art and native progress bars

The owner supplied `419266235_ebd3d78e-94a7-470c-9095-84b8f1e5145c.eps`,
`426110610_99db5b27-9cca-4bf4-a1e5-33b848eb7ead.eps`, and
`426110618_f9b6e76a-7c58-4bf5-b78c-89e8314e945b.eps`, plus a bar-style reference
and a right-facing rocket screenshot. The converted duplicate EPS is not needed.

`scripts/Generate-MatrixArtBank.py SOURCE_DIRECTORY --preview PREVIEW.png`
uses Ghostscript in safe mode to rasterize the supplied sheets at 96 DPI,
extracts selected artwork, removes the sheet background and quantizes native
4-/5-/6-/10-row grids. Tiny hearts, stars, skulls and the rocket have explicit
native-grid adaptations so features survive the reduced height. `MatrixArtBank.json`
stores palette-index rows with transparent cells. Source bitmaps use the existing
90-column stride. User-facing imports clone the buffers; first manual editing
detaches the preset key, preserving custom work during subsequent height edits.

Progress-bar geometry is drawn directly on the selected LED grid, using the
reference's fill, badge and pattern families. It is not a scaled screenshot.
The renderer clamps progress to the source-unit range, supports manual values,
and composites the result into ordinary RGB pixels. Widget width is one of
30, 45 or 60 LEDs. Optional text or emoji is rasterized before the remaining bar
area. The twelve presets remain distinct through colour, badge, segmentation,
diagonals, chevrons and heart pips.

The Animations bank uses original procedural renderers. Rocket flight clips a
mirrored or forward-facing sprite and a short changing fire trail inside the
selected area. Crab and cat use two-frame walks; starfield adds twinkling stars
and a shooting star; rainbow, breathing, wave, two tone, fade, gradient and
meteor generate native-grid effects. Position is an integer LED offset derived
from the existing 200 ms step and dots/second setting, wraps deterministically
and never spills outside the saved 60- or 90-dot area. Animated text uses the
same direction, speed and area model. The preview has a visibility-gated timer;
applied output uses the existing non-queuing update gate. Layers, locking,
duplication and persistence use the normal Matrix model. Neither the firmware
protocol nor firmware files are modified. The 60/30 guide is drawn only by
MatrixSurface.

Cross-platform checks cover every preset at all four heights, all supported
widths, progress bounds, labels, native art buffers, direction/mirroring,
movement, looping, pause and settings.
Windows checks additionally exercise the actual gallery, Properties selectors,
preview timer, sensor-driven bar updates and animated acknowledged output.
