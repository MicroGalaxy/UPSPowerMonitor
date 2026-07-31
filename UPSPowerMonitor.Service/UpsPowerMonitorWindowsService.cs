using System.ServiceProcess;

namespace UPSPowerMonitor.ServiceHost;

internal sealed class UpsPowerMonitorWindowsService : ServiceBase
{
    private readonly ServiceMonitorRuntime _runtime;

    public UpsPowerMonitorWindowsService()
    {
        ServiceName = "UPSPowerMonitor";
        CanStop = true;
        CanShutdown = true;
        CanPauseAndContinue = false;
        AutoLog = true;
        _runtime = new ServiceMonitorRuntime(WriteInformation);
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            _runtime.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            WriteInformation("UPS 电源监控服务已启动。");
        }
        catch (Exception exception)
        {
            WriteError($"服务启动失败：{exception}");
            throw;
        }
    }

    protected override void OnStop()
    {
        try
        {
            _runtime.StopAsync(false, CancellationToken.None).GetAwaiter().GetResult();
            WriteInformation("UPS 电源监控服务已停止。");
        }
        catch (Exception exception)
        {
            WriteError($"服务停止时发生错误：{exception}");
        }
    }

    protected override void OnShutdown()
    {
        try
        {
            RequestAdditionalTime(8000);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            _runtime.StopAsync(true, timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            WriteError($"发送关机通知时发生错误：{exception}");
        }

        base.OnShutdown();
    }

    private void WriteInformation(string message)
    {
        try
        {
            EventLog.WriteEntry(message, System.Diagnostics.EventLogEntryType.Information);
        }
        catch
        {
        }
    }

    private void WriteError(string message)
    {
        try
        {
            EventLog.WriteEntry(message, System.Diagnostics.EventLogEntryType.Error);
        }
        catch
        {
        }
    }
}
