# Product specification

This document defines the target user experience. Implementation status is tracked separately in `ROADMAP.md`.

## Core principle

The application should feel closer to a simple visual poster/layout editor than to CAD or monitoring software. Advanced options are progressively disclosed. The 480x320 or 320x480 screen is always the center of the editor.

## Main window

Three permanent areas:

1. **Add / Layers** on the left.
2. **Real-pixel display canvas** in the center.
3. **Contextual properties** on the right.

The top toolbar contains file actions, layout orientation, physical mounting rotation, live display, COM device selection and connection state.

## Display orientation

Theme layout and physical mounting are independent concepts.

Theme canvas:

- Landscape: 480x320
- Portrait: 320x480

Physical output rotation:

- 0°
- 90°
- 180°
- 270°

The theme package remembers both.

## Editing

Target editing behavior:

- click to select
- drag to move
- resize handles
- rotate where useful
- keyboard nudging
- multi-select
- copy/paste/duplicate
- lock/hide
- group/ungroup
- reorder layers
- snap to grid
- smart alignment guides
- align and distribute selections
- undo/redo

## Widgets

### Text

- static text
- date
- time
- label
- formatting

### Dynamic value

Any compatible data source can be shown as text.

### Circular gauge

- full/partial ring
- configurable thickness
- foreground and track colors
- value and label
- min/max
- optional thresholds

### Bar gauge

- continuous or segmented
- horizontal/vertical
- configurable segment count/gap
- foreground/track colors
- thresholds

### Graph

- line
- stepped line
- filled area
- block/bar history, including the retro segmented style
- graph color
- fill color
- text color
- background/grid/border colors
- history duration
- line thickness
- fixed or automatic Y scale
- current value and label

### Shapes

- line
- rectangle
- rounded rectangle
- ellipse
- fill/stroke/opacity

## Data sources

Target providers:

- CPU usage, temperatures, clocks, power, voltage
- GPU usage, core/hotspot temperature, clocks, VRAM, fan, power
- RAM used/free/percentage
- storage used/free, read/write rate, temperature
- network upload/download and totals
- motherboard temperatures/voltages
- fan RPM
- pump RPM
- clock/date
- weather temperature
- feels-like temperature
- humidity
- wind
- condition/icon
- forecast fields

Data and visual representation are separate. `GPU.Temperature` can therefore be bound to a value, ring, bar or graph.

## Fonts

- installed Windows fonts
- imported TTF/OTF
- imported fonts are embedded in the theme package
- imported fonts can be used without installing them system-wide
- font licence warning before embedding
- size, weight, italic, alignment, spacing, outline and shadow

## Colors

Every relevant visual component should have independent color control. A later milestone adds reusable theme palette colors so changing a palette entry can update multiple objects.

## Media

Supported target formats:

- PNG
- JPG/JPEG
- WebP
- BMP
- GIF
- MP4
- WebM
- MOV
- AVI/MKV where the bundled decoder permits

Video has **no audio output**. Audio streams are discarded during optimization.

### Import UX

The user drops or opens media, then gets a visual crop view with:

- Fit
- Fill
- Stretch
- Original
- pan
- zoom
- crop
- rotation/mirroring
- opacity
- brightness
- contrast
- saturation
- playback speed
- trim start/end for animation/video
- loop
- FPS Auto/manual

### Automatic optimization

The app should pre-process media for the 3.5-inch screen:

- resize/crop to target area
- discard video audio
- reduce FPS to what the actual display can sustain
- precompute expensive transformations
- convert/render with RGB565 behavior in mind
- suppress visually insignificant temporal changes
- cache optimized frames/metadata

Profiles:

- Maximum quality
- Balanced
- Maximum performance
- Auto

## Device benchmark

A one-click device optimization should measure real USB/display performance and save a hardware profile. That profile is used by Auto FPS and media optimization rather than assuming a fixed frame rate.

## Theme packages

One `.t3theme` file contains everything required to reproduce a theme:

- layout
- widgets
- styles/colors
- data bindings
- custom fonts
- images
- GIFs
- silent optimized video
- media crop/playback settings
- compatibility metadata
- preview thumbnail

The package contains declarative data and passive assets only, never executable code.

## Runtime mode

After editing, the full editor should not have to stay open.

Target runtime features:

- system tray
- start with Windows
- start minimized
- auto-connect display
- auto-load last theme
- switch themes from tray
- brightness control
- recover after USB reconnect

## Shareability

Double-clicking a registered `.t3theme` file should open it in the application. Dragging one onto the application should do the same. Themes should remain backward-compatible through an explicit format version.
