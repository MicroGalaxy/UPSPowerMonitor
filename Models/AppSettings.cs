namespace UPSPowerMonitor.Models;

public sealed class AppSettings
{
    public List<string> BarkDeviceKeys { get; set; } = [];

    public bool ContinuousRinging { get; set; }

    public string MessageGroup { get; set; } = "UPS 电源监控";

    public bool CriticalAlert { get; set; }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            BarkDeviceKeys = BarkDeviceKeys is null ? [] : [.. BarkDeviceKeys],
            ContinuousRinging = ContinuousRinging,
            MessageGroup = MessageGroup ?? string.Empty,
            CriticalAlert = CriticalAlert
        };
    }
}
