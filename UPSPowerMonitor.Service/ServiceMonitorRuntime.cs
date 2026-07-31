using UPSPowerMonitor.Services;

namespace UPSPowerMonitor.ServiceHost;

internal sealed class ServiceMonitorRuntime(Action<string> log)
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private CancellationTokenSource? _runtimeCancellation;
    private SettingsService? _settingsService;
    private PowerMonitorService? _powerMonitor;
    private NotificationCoordinator? _notificationCoordinator;
    private Task? _settingsReloadTask;
    private DateTime _settingsLastWriteUtc;
    private bool _running;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_running)
            {
                return;
            }

            _settingsService = new SettingsService();
            var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var barkService = new BarkNotificationService();
            var provider = new SystemPowerStatusProvider();
            _notificationCoordinator = new NotificationCoordinator(barkService, settings);
            _notificationCoordinator.NotificationReported += (_, args) => log(args.Message);

            _powerMonitor = new PowerMonitorService(provider, TimeSpan.FromSeconds(3));
            _powerMonitor.StatusChanged += OnPowerStatusChanged;
            _powerMonitor.ReadFailed += (_, exception) => log($"读取电源状态失败：{exception.Message}");

            var initialStatus = _powerMonitor.Start();
            _notificationCoordinator.Initialize(initialStatus);
            _settingsLastWriteUtc = _settingsService.GetLastWriteTimeUtc();

            _runtimeCancellation = new CancellationTokenSource();
            _settingsReloadTask = ReloadSettingsAsync(_runtimeCancellation.Token);
            _running = true;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync(bool sendShutdownNotification, CancellationToken cancellationToken)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_running)
            {
                return;
            }

            if (sendShutdownNotification && _notificationCoordinator is not null)
            {
                await _notificationCoordinator.SendShutdownNotificationAsync(cancellationToken).ConfigureAwait(false);
            }

            _runtimeCancellation?.Cancel();

            if (_settingsReloadTask is not null)
            {
                try
                {
                    await _settingsReloadTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (_powerMonitor is not null)
            {
                await _powerMonitor.DisposeAsync().ConfigureAwait(false);
            }

            _runtimeCancellation?.Dispose();
            _runtimeCancellation = null;
            _settingsReloadTask = null;
            _powerMonitor = null;
            _notificationCoordinator = null;
            _running = false;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void OnPowerStatusChanged(object? sender, PowerStatusChangedEventArgs args)
    {
        if (_notificationCoordinator is not null)
        {
            _ = _notificationCoordinator.HandleStatusChangeAsync(args.Previous, args.Current);
        }
    }

    private async Task ReloadSettingsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_settingsService is null || _notificationCoordinator is null)
            {
                continue;
            }

            var lastWriteUtc = _settingsService.GetLastWriteTimeUtc();
            if (lastWriteUtc <= _settingsLastWriteUtc)
            {
                continue;
            }

            var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            _notificationCoordinator.UpdateSettings(settings);
            _settingsLastWriteUtc = lastWriteUtc;
            log("Bark 通知设置已重新加载。");
        }
    }
}
