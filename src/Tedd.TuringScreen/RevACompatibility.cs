namespace Tedd.TuringScreen;

/// <summary>
/// Compatibility strategies for the family of 3.5-inch Revision-A controllers.
/// Different firmware builds use the same USB identity but disagree about how
/// landscape orientation should be described to the controller.
/// </summary>
public enum RevACompatibilityMode
{
    /// <summary>
    /// Keep the LCD controller in native 320x480 portrait mode and rotate a
    /// 480x320 logical canvas in software before transmission. This matches
    /// USB35INCHIPSV2 / VID 1A86 PID 5722 implementations tested by the
    /// usb-lcd-dashboard project.
    /// </summary>
    NativePortraitSoftwareRotation,

    /// <summary>
    /// Let the controller rotate the framebuffer and report the logical
    /// dimensions (480x320 in landscape) in command 121. This matches the
    /// mature turing-smart-screen-python implementation and TuringMonitor.
    /// </summary>
    HardwareLogicalDimensions,

    /// <summary>
    /// Let the controller rotate the framebuffer but keep 320x480 in the
    /// orientation command. This matches TuringSmartScreenLib Revision A.
    /// </summary>
    HardwareNativeDimensions
}

/// <summary>
/// Pixel packing options used for diagnostics and firmware compatibility.
/// Revision-A normally uses RGB565 little-endian.
/// </summary>
public enum Rgb565Encoding
{
    RgbLittleEndian,
    BgrLittleEndian,
    RgbBigEndian,
    BgrBigEndian
}
