using System.ComponentModel;
using System.Runtime.InteropServices;
using UPSPowerMonitor.Models;

namespace UPSPowerMonitor.Services;

public interface IPowerStatusProvider
{
    PowerStatusSnapshot GetCurrentStatus();
}

public sealed class SystemPowerStatusProvider : IPowerStatusProvider
{
    public PowerStatusSnapshot GetCurrentStatus()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取系统电源状态。");
        }

        var lineState = status.AcLineStatus switch
        {
            0 => PowerLineState.Offline,
            1 => PowerLineState.Online,
            _ => PowerLineState.Unknown
        };

        var batteryStateUnknown = status.BatteryFlag == byte.MaxValue;
        var hasBattery = !batteryStateUnknown && (status.BatteryFlag & 128) == 0;
        int? batteryPercentage = hasBattery && status.BatteryLifePercent != byte.MaxValue
            ? Math.Clamp((int)status.BatteryLifePercent, 0, 100)
            : null;

        return new PowerStatusSnapshot(
            lineState,
            batteryPercentage,
            (status.BatteryFlag & 8) != 0,
            hasBattery,
            DateTimeOffset.Now);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus powerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }
}
