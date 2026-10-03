# Architecture

## Design principle

The editor preview and the physical screen must share the same renderer. PC Info Screen Studio therefore renders the complete logical theme into a SkiaSharp bitmap first, then either paints that bitmap in the WPF editor or converts it to RGB565 and sends it to the USB display.

```text
ThemeDocument + assets + live data
             |
             v
       ThemeRenderer
             |
        BGRA bitmap
          /      \
         v        v
 WPF preview    RGB565
                  |
                  v
       Tedd.TuringScreen
                  |
                  v
            USB CDC LCD
```

This prevents the common problem where the editor preview and physical screen render differently.

## Projects

### PCInfoScreenStudio.App

WPF application containing:

- editor UI
- theme/document models
- package loading/saving
- Skia renderer
- media/font assets
- device integration
- future sensor and weather providers

### Tedd.TuringScreen

Vendored MIT-licensed hardware communication layer. It owns serial protocol commands, RGB565 framebuffer diffing, dirty-rectangle transmission and reconnect behavior.

## Widget model

A widget stores layout, styling and binding separately. For example, `GPU.Temperature` can be displayed by a value widget, circular gauge, segmented bar or graph without creating a GPU-specific visual class.

This separation is deliberate:

```text
Data source: GPU.Temperature
              |
              +--> Value
              +--> CircularGauge
              +--> BarGauge
              +--> Graph
```

## Theme workspace

An opened theme is extracted to a private temporary workspace. Assets are referenced by safe relative package paths. When saved, the document and assets are rebuilt into one `.t3theme` archive.

Theme archives never execute code. The loader rejects traversal paths such as `../file`.

## Media pipeline target

The planned media importer is a pre-processing pipeline, not a per-frame runtime converter:

```text
User image / GIF / video
          |
     visual crop
          |
 resize to target canvas
          |
 remove audio (video)
          |
 temporal/FPS optimization
          |
 RGB565-aware change analysis
          |
 optimized theme asset/cache
```

The application should do expensive media work once when importing or saving rather than repeatedly while the display is running.
