namespace PCInfoScreenStudio.Models;

public enum DisplayProtocolProfile
{
    Auto,
    RevANativePortrait,
    RevAHardwareLogical,
    RevAHardwareNative
}

public enum DisplayColorMode
{
    Auto,
    Rgb565LittleEndian,
    Bgr565LittleEndian,
    Rgb565BigEndian,
    Bgr565BigEndian
}

public sealed record DisplayProtocolOption(string Label, DisplayProtocolProfile Value);
public sealed record DisplayColorOption(string Label, DisplayColorMode Value);
