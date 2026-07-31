using System.Windows;
using UPSPowerMonitor.DesktopServices;
using UPSPowerMonitor.Services;
using UPSPowerMonitor.ViewModels;

namespace UPSPowerMonitor;

public partial class App : System.Windows.Application
{
    private PowerMonitorService? _powerMonitor;
    private NotificationCoordinator? _notificationCoordinator;
    private WindowsServiceManager? _windowsServiceManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (TryHandleServiceCommand(e.Args))
        {
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                args.Exception.Message,
                "UPS 电源监控",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var settingsService = new SettingsService();
        var settings = await settingsService.LoadAsync();
        var barkService = new BarkNotificationService();
        var powerProvider = new SystemPowerStatusProvider();
        _windowsServiceManager = new WindowsServiceManager();

        _powerMonitor = new PowerMonitorService(powerProvider, TimeSpan.FromSeconds(3));
        _notificationCoordinator = new NotificationCoordinator(barkService, settings);

        var viewModel = new MainViewModel(
            settingsService,
            barkService,
            _powerMonitor,
            _notificationCoordinator,
            _windowsServiceManager,
            settings);

        var window = new MainWindow
        {
            DataContext = viewModel
        };

        MainWindow = window;
        window.Show();
        viewModel.StartMonitoring();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is UPSPowerMonitor.MainWindow window)
        {
            window.PrepareForSystemShutdown();
        }

        var serviceHandlesShutdown = _windowsServiceManager?.IsRunning() == true;
        if (e.ReasonSessionEnding == ReasonSessionEnding.Shutdown
            && !serviceHandlesShutdown
            && _notificationCoordinator is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                _notificationCoordinator.SendShutdownNotificationAsync(timeout.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Windows may already be tearing down networking. Shutdown must never be blocked.
            }
        }

        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_powerMonitor is not null)
        {
            _powerMonitor.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }

    private bool TryHandleServiceCommand(string[] args)
    {
        var command = args.FirstOrDefault();
        if (command is not (WindowsServiceManager.InstallArgument or WindowsServiceManager.UninstallArgument))
        {
            return false;
        }

        try
        {
            if (command == WindowsServiceManager.InstallArgument)
            {
                WindowsServiceManager.InstallService();
            }
            else
            {
                WindowsServiceManager.UninstallService();
            }

            Shutdown(0);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "Windows 服务操作失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }

        return true;
    }
}
