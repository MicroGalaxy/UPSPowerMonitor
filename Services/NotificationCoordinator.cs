using UPSPowerMonitor.Models;

namespace UPSPowerMonitor.Services;

public sealed class NotificationReportEventArgs(string message, bool succeeded) : EventArgs
{
    public string Message { get; } = message;

    public bool Succeeded { get; } = succeeded;
}

public sealed class NotificationCoordinator
{
    private readonly BarkNotificationService _barkService;
    private readonly SemaphoreSlim _notificationLock = new(1, 1);
    private readonly string _machineName = Environment.MachineName;
    private AppSettings _settings;
    private int? _batteryAlertAnchor;
    private DateTimeOffset? _outageStartedAt;

    public NotificationCoordinator(BarkNotificationService barkService, AppSettings settings)
    {
        _barkService = barkService;
        _settings = settings.Clone();
    }

    public event EventHandler<NotificationReportEventArgs>? NotificationReported;

    public void Initialize(PowerStatusSnapshot initialStatus)
    {
        if (initialStatus.LineState == PowerLineState.Offline)
        {
            _batteryAlertAnchor = initialStatus.BatteryPercentage;
            _outageStartedAt = initialStatus.ObservedAt;
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings.Clone();
    }

    public async Task HandleStatusChangeAsync(
        PowerStatusSnapshot previous,
        PowerStatusSnapshot current,
        CancellationToken cancellationToken = default)
    {
        await _notificationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (previous.LineState == PowerLineState.Online && current.LineState == PowerLineState.Offline)
            {
                _outageStartedAt = current.ObservedAt;
                _batteryAlertAnchor = current.BatteryPercentage;
                await TrySendAsync(
                    "UPS 电源已断开",
                    BuildPowerBody("市电连接已断开，设备正在使用电池供电。", current),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (previous.LineState == PowerLineState.Offline && current.LineState == PowerLineState.Online)
            {
                var duration = _outageStartedAt.HasValue
                    ? $"\n断电持续：{FormatDuration(current.ObservedAt - _outageStartedAt.Value)}"
                    : string.Empty;

                await TrySendAsync(
                    "UPS 电源已恢复",
                    BuildPowerBody("市电连接已恢复。", current) + duration,
                    cancellationToken).ConfigureAwait(false);

                _outageStartedAt = null;
                _batteryAlertAnchor = null;
                return;
            }

            if (current.LineState != PowerLineState.Offline || !current.BatteryPercentage.HasValue)
            {
                return;
            }

            _batteryAlertAnchor ??= previous.BatteryPercentage ?? current.BatteryPercentage;
            if (current.BatteryPercentage.Value > _batteryAlertAnchor.Value - 10)
            {
                return;
            }

            await TrySendAsync(
                "UPS 电池电量下降",
                BuildPowerBody("断电期间电池容量已再次下降 10%。", current),
                cancellationToken).ConfigureAwait(false);

            do
            {
                _batteryAlertAnchor -= 10;
            }
            while (current.BatteryPercentage.Value <= _batteryAlertAnchor.Value - 10);
        }
        finally
        {
            _notificationLock.Release();
        }
    }

    public async Task SendShutdownNotificationAsync(CancellationToken cancellationToken = default)
    {
        await _notificationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await TrySendAsync(
                "服务器即将关机",
                $"设备：{_machineName}\n时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n系统正在执行关机。",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _notificationLock.Release();
        }
    }

    private async Task TrySendAsync(string title, string body, CancellationToken cancellationToken)
    {
        if (_settings.BarkDeviceKeys.Count == 0)
        {
            NotificationReported?.Invoke(
                this,
                new NotificationReportEventArgs("事件已检测到，但尚未配置 Bark ID。", false));
            return;
        }

        try
        {
            await _barkService.SendAsync(_settings, title, body, cancellationToken).ConfigureAwait(false);
            NotificationReported?.Invoke(
                this,
                new NotificationReportEventArgs($"{DateTime.Now:HH:mm:ss} · {title} 已推送", true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            NotificationReported?.Invoke(
                this,
                new NotificationReportEventArgs($"推送失败：{exception.Message}", false));
        }
    }

    private string BuildPowerBody(string message, PowerStatusSnapshot status)
    {
        var battery = status.BatteryPercentage.HasValue
            ? $"{status.BatteryPercentage.Value}%"
            : "未知";
        return $"设备：{_machineName}\n{message}\n当前电量：{battery}\n时间：{status.ObservedAt:yyyy-MM-dd HH:mm:ss}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分钟";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes} 分钟";
        }

        return $"{Math.Max(1, (int)duration.TotalSeconds)} 秒";
    }
}
