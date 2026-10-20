namespace DmxPro.App.ViewModels;

/// <summary>An FTDI device in the Open DMX picker.</summary>
/// <param name="SerialNumber">Stable device identifier stored in the configuration.</param>
/// <param name="Label">Shown in the picker, e.g. "FT232R USB UART (A6XYZ123)".</param>
public sealed record FtdiDeviceOption(string SerialNumber, string Label);
