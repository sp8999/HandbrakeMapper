namespace HandbrakeMapper.Models;

public sealed class AppSettings
{
    public int DeviceId { get; set; } = -1;
    public string DeviceName { get; set; } = string.Empty;
    public AxisKind Axis { get; set; } = AxisKind.X;
    public bool AxisOutputEnabled { get; set; } = true;
    public uint CalibrationMin { get; set; } = 0;
    public uint CalibrationMax { get; set; } = 65535;
    public decimal DeadzonePercent { get; set; } = 2;
    public bool Invert { get; set; }
    public bool ButtonOutputEnabled { get; set; }
    public int SourceButton { get; set; } = 1;
    public bool HidePhysicalDevice { get; set; } = true;
    public int VJoyDeviceId { get; set; } = 1;
    public List<string> HidHidePaths { get; set; } = [];
}

public enum AxisKind
{
    X,
    Y,
    Z,
    R,
    U,
    V
}
