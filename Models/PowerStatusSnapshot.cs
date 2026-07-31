namespace UPSPowerMonitor.Models;

public enum PowerLineState
{
    Unknown,
    Offline,
    Online
}

public sealed record PowerStatusSnapshot(
    PowerLineState LineState,
    int? BatteryPercentage,
    bool IsCharging,
    bool HasBattery,
    DateTimeOffset ObservedAt)
{
    public bool HasSameValues(PowerStatusSnapshot other)
    {
        return LineState == other.LineState
               && BatteryPercentage == other.BatteryPercentage
               && IsCharging == other.IsCharging
               && HasBattery == other.HasBattery;
    }
}
