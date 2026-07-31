using System.ServiceProcess;

namespace UPSPowerMonitor.ServiceHost;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (Environment.UserInteractive && args.Contains("--console", StringComparer.OrdinalIgnoreCase))
        {
            await RunConsoleAsync();
            return;
        }

        ServiceBase.Run(new UpsPowerMonitorWindowsService());
    }

    private static async Task RunConsoleAsync()
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var runtime = new ServiceMonitorRuntime(message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"));
        await runtime.StartAsync(cancellation.Token);
        Console.WriteLine("UPS 电源监控服务正在控制台模式运行，按 Ctrl+C 停止。");

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await runtime.StopAsync(false, CancellationToken.None);
        }
    }
}
