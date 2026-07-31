using UPSPowerMonitor.Models;

namespace UPSPowerMonitor.Services;

public sealed class PowerStatusChangedEventArgs(
    PowerStatusSnapshot previous,
    PowerStatusSnapshot current) : EventArgs
{
    public PowerStatusSnapshot Previous { get; } = previous;

    public PowerStatusSnapshot Current { get; } = current;
}

public sealed class PowerStatusObservedEventArgs(PowerStatusSnapshot current) : EventArgs
{
    public PowerStatusSnapshot Current { get; } = current;
}

public sealed class PowerMonitorService : IAsyncDisposable
{
    private readonly IPowerStatusProvider _provider;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _monitorTask;

    public PowerMonitorService(IPowerStatusProvider provider, TimeSpan pollInterval)
    {
        _provider = provider;
        _pollInterval = pollInterval;
    }

    public event EventHandler<PowerStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<PowerStatusObservedEventArgs>? StatusObserved;

    public event EventHandler<Exception>? ReadFailed;

    public PowerStatusSnapshot? CurrentStatus { get; private set; }

    public PowerStatusSnapshot Start()
    {
        if (_monitorTask is not null)
        {
            return CurrentStatus ?? throw new InvalidOperationException("电源监控尚未完成初始化。");
        }

        CurrentStatus = _provider.GetCurrentStatus();
        _monitorTask = MonitorAsync(_cancellation.Token);
        return CurrentStatus;
    }

    public void RefreshNow()
    {
        TryRefresh();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                TryRefresh();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void TryRefresh()
    {
        try
        {
            var current = _provider.GetCurrentStatus();
            var previous = CurrentStatus;
            CurrentStatus = current;
            StatusObserved?.Invoke(this, new PowerStatusObservedEventArgs(current));

            if (previous is null || previous.HasSameValues(current))
            {
                return;
            }

            StatusChanged?.Invoke(this, new PowerStatusChangedEventArgs(previous, current));
        }
        catch (Exception exception)
        {
            ReadFailed?.Invoke(this, exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();

        if (_monitorTask is not null)
        {
            await _monitorTask.ConfigureAwait(false);
        }

        _cancellation.Dispose();
    }
}
