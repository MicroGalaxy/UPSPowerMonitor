using System.Windows;
using System.Windows.Media;
using UPSPowerMonitor.DesktopServices;
using UPSPowerMonitor.Infrastructure;
using UPSPowerMonitor.Models;
using UPSPowerMonitor.Services;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace UPSPowerMonitor.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly Brush OnlineBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
    private static readonly Brush OnlineBackground = new SolidColorBrush(Color.FromRgb(240, 253, 244));
    private static readonly Brush OfflineBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
    private static readonly Brush OfflineBackground = new SolidColorBrush(Color.FromRgb(254, 242, 242));
    private static readonly Brush UnknownBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    private static readonly Brush UnknownBackground = new SolidColorBrush(Color.FromRgb(248, 250, 252));
    private static readonly char[] DeviceKeySeparators = ['\r', '\n', ',', ';', '，', '；'];

    private readonly SettingsService _settingsService;
    private readonly BarkNotificationService _barkService;
    private readonly PowerMonitorService _powerMonitor;
    private readonly NotificationCoordinator _notificationCoordinator;
    private readonly WindowsServiceManager _windowsServiceManager;
    private bool _isDashboardSelected = true;
    private string _powerStatusText = "正在读取";
    private string _powerDescription = "正在获取系统电源状态…";
    private Brush _powerAccentBrush = UnknownBrush;
    private Brush _powerCardBackground = UnknownBackground;
    private string _batteryText = "--";
    private double _batteryPercentage;
    private string _batteryDescription = "等待电池信息";
    private string _lastUpdatedText = "尚未更新";
    private string _lastNotificationText = "尚无推送记录";
    private string _operationMessage = "设置保存在 %ProgramData% 的共享配置目录中。";
    private bool _isBusy;
    private string _barkIdsText;
    private bool _continuousRinging;
    private string _messageGroup;
    private bool _criticalAlert;
    private bool _hasBarkConfiguration;
    private string _windowsServiceStatusText = "正在检查";
    private string _windowsServiceDescription = "正在读取 Windows 服务状态…";
    private Brush _windowsServiceAccentBrush = UnknownBrush;
    private bool _isWindowsServiceInstalled;
    private bool _isWindowsServiceRunning;

    public MainViewModel(
        SettingsService settingsService,
        BarkNotificationService barkService,
        PowerMonitorService powerMonitor,
        NotificationCoordinator notificationCoordinator,
        WindowsServiceManager windowsServiceManager,
        AppSettings settings)
    {
        _settingsService = settingsService;
        _barkService = barkService;
        _powerMonitor = powerMonitor;
        _notificationCoordinator = notificationCoordinator;
        _windowsServiceManager = windowsServiceManager;

        _barkIdsText = string.Join(Environment.NewLine, settings.BarkDeviceKeys);
        _continuousRinging = settings.ContinuousRinging;
        _messageGroup = settings.MessageGroup;
        _criticalAlert = settings.CriticalAlert;
        _hasBarkConfiguration = settings.BarkDeviceKeys.Count > 0;

        NavigateCommand = new RelayCommand(Navigate);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy, HandleCommandException);
        TestNotificationCommand = new AsyncRelayCommand(SendTestNotificationAsync, () => !IsBusy, HandleCommandException);
        InstallServiceCommand = new AsyncRelayCommand(
            InstallWindowsServiceAsync,
            () => !IsBusy && !IsWindowsServiceInstalled,
            HandleCommandException);
        UninstallServiceCommand = new AsyncRelayCommand(
            UninstallWindowsServiceAsync,
            () => !IsBusy && IsWindowsServiceInstalled,
            HandleCommandException);
        RefreshServiceStatusCommand = new RelayCommand(_ => RefreshWindowsServiceStatus());

        _powerMonitor.StatusChanged += OnPowerStatusChanged;
        _powerMonitor.StatusObserved += OnPowerStatusObserved;
        _powerMonitor.ReadFailed += OnPowerReadFailed;
        _notificationCoordinator.NotificationReported += OnNotificationReported;
        RefreshWindowsServiceStatus();
    }

    public RelayCommand NavigateCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand TestNotificationCommand { get; }

    public AsyncRelayCommand InstallServiceCommand { get; }

    public AsyncRelayCommand UninstallServiceCommand { get; }

    public RelayCommand RefreshServiceStatusCommand { get; }

    public bool IsDashboardSelected
    {
        get => _isDashboardSelected;
        private set
        {
            if (SetProperty(ref _isDashboardSelected, value))
            {
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public bool IsSettingsSelected => !IsDashboardSelected;

    public string MachineName => Environment.MachineName;

    public string PowerStatusText
    {
        get => _powerStatusText;
        private set => SetProperty(ref _powerStatusText, value);
    }

    public string PowerDescription
    {
        get => _powerDescription;
        private set => SetProperty(ref _powerDescription, value);
    }

    public Brush PowerAccentBrush
    {
        get => _powerAccentBrush;
        private set => SetProperty(ref _powerAccentBrush, value);
    }

    public Brush PowerCardBackground
    {
        get => _powerCardBackground;
        private set => SetProperty(ref _powerCardBackground, value);
    }

    public string BatteryText
    {
        get => _batteryText;
        private set => SetProperty(ref _batteryText, value);
    }

    public double BatteryPercentage
    {
        get => _batteryPercentage;
        private set => SetProperty(ref _batteryPercentage, value);
    }

    public string BatteryDescription
    {
        get => _batteryDescription;
        private set => SetProperty(ref _batteryDescription, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public string LastNotificationText
    {
        get => _lastNotificationText;
        private set => SetProperty(ref _lastNotificationText, value);
    }

    public string OperationMessage
    {
        get => _operationMessage;
        private set => SetProperty(ref _operationMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveSettingsCommand.RaiseCanExecuteChanged();
                TestNotificationCommand.RaiseCanExecuteChanged();
                InstallServiceCommand.RaiseCanExecuteChanged();
                UninstallServiceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BarkIdsText
    {
        get => _barkIdsText;
        set => SetProperty(ref _barkIdsText, value);
    }

    public bool ContinuousRinging
    {
        get => _continuousRinging;
        set => SetProperty(ref _continuousRinging, value);
    }

    public string MessageGroup
    {
        get => _messageGroup;
        set => SetProperty(ref _messageGroup, value);
    }

    public bool CriticalAlert
    {
        get => _criticalAlert;
        set => SetProperty(ref _criticalAlert, value);
    }

    public bool HasBarkConfiguration
    {
        get => _hasBarkConfiguration;
        private set => SetProperty(ref _hasBarkConfiguration, value);
    }

    public string WindowsServiceStatusText
    {
        get => _windowsServiceStatusText;
        private set => SetProperty(ref _windowsServiceStatusText, value);
    }

    public string WindowsServiceDescription
    {
        get => _windowsServiceDescription;
        private set => SetProperty(ref _windowsServiceDescription, value);
    }

    public Brush WindowsServiceAccentBrush
    {
        get => _windowsServiceAccentBrush;
        private set => SetProperty(ref _windowsServiceAccentBrush, value);
    }

    public bool IsWindowsServiceInstalled
    {
        get => _isWindowsServiceInstalled;
        private set
        {
            if (SetProperty(ref _isWindowsServiceInstalled, value))
            {
                InstallServiceCommand.RaiseCanExecuteChanged();
                UninstallServiceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsWindowsServiceRunning
    {
        get => _isWindowsServiceRunning;
        private set => SetProperty(ref _isWindowsServiceRunning, value);
    }

    public void StartMonitoring()
    {
        try
        {
            var initialStatus = _powerMonitor.Start();
            _notificationCoordinator.Initialize(initialStatus);
            UpdatePowerDisplay(initialStatus);
        }
        catch (Exception exception)
        {
            PowerStatusText = "读取失败";
            PowerDescription = exception.Message;
            PowerAccentBrush = OfflineBrush;
            PowerCardBackground = OfflineBackground;
        }
    }

    public void RefreshPowerStatus()
    {
        _powerMonitor.RefreshNow();
    }

    private void Navigate(object? parameter)
    {
        IsDashboardSelected = !string.Equals(parameter?.ToString(), "Settings", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SaveSettingsAsync()
    {
        IsBusy = true;
        OperationMessage = "正在保存设置…";

        try
        {
            var settings = BuildSettingsFromForm();
            await _settingsService.SaveAsync(settings);
            _notificationCoordinator.UpdateSettings(settings);
            HasBarkConfiguration = settings.BarkDeviceKeys.Count > 0;
            OperationMessage = $"设置已保存 · {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendTestNotificationAsync()
    {
        IsBusy = true;
        OperationMessage = "正在发送测试通知…";

        try
        {
            var settings = BuildSettingsFromForm();
            if (settings.BarkDeviceKeys.Count == 0)
            {
                throw new BarkNotificationException("请至少填写一个 Bark 推送 ID。");
            }

            await _settingsService.SaveAsync(settings);
            _notificationCoordinator.UpdateSettings(settings);
            HasBarkConfiguration = true;
            await _barkService.SendAsync(
                settings,
                "UPS 电源监控测试",
                $"设备：{MachineName}\nBark 通知配置成功。\n时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            OperationMessage = $"测试通知已发送 · {DateTime.Now:HH:mm:ss}";
            LastNotificationText = $"{DateTime.Now:HH:mm:ss} · 测试通知已推送";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallWindowsServiceAsync()
    {
        IsBusy = true;
        OperationMessage = "正在安装 Windows 服务…";

        try
        {
            var settings = BuildSettingsFromForm();
            await _settingsService.SaveAsync(settings);
            _notificationCoordinator.UpdateSettings(settings);
            HasBarkConfiguration = settings.BarkDeviceKeys.Count > 0;

            await _windowsServiceManager.RunElevatedAsync(WindowsServiceManager.InstallArgument);
            await Task.Delay(500);
            RefreshWindowsServiceStatus();
            OperationMessage = "Windows 服务已安装并启动，将在系统启动时自动运行。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UninstallWindowsServiceAsync()
    {
        IsBusy = true;
        OperationMessage = "正在停止并卸载 Windows 服务…";

        try
        {
            await _windowsServiceManager.RunElevatedAsync(WindowsServiceManager.UninstallArgument);
            await Task.Delay(500);
            RefreshWindowsServiceStatus();
            OperationMessage = "Windows 服务已卸载，托盘程序仍会继续监控。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshWindowsServiceStatus()
    {
        try
        {
            var state = _windowsServiceManager.GetStatus();
            IsWindowsServiceInstalled = state != WindowsServiceState.NotInstalled;
            IsWindowsServiceRunning = state == WindowsServiceState.Running;

            switch (state)
            {
                case WindowsServiceState.NotInstalled:
                    WindowsServiceStatusText = "未安装";
                    WindowsServiceDescription = "安装后无需用户登录也能持续监控 UPS。";
                    WindowsServiceAccentBrush = UnknownBrush;
                    break;
                case WindowsServiceState.Running:
                    WindowsServiceStatusText = "服务运行中";
                    WindowsServiceDescription = "后台服务正在监控；托盘程序仅负责显示和配置。";
                    WindowsServiceAccentBrush = OnlineBrush;
                    break;
                case WindowsServiceState.Stopped:
                    WindowsServiceStatusText = "服务已停止";
                    WindowsServiceDescription = "服务已安装但未运行，可卸载后重新安装。";
                    WindowsServiceAccentBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
                    break;
                case WindowsServiceState.StartPending:
                    WindowsServiceStatusText = "正在启动";
                    WindowsServiceDescription = "Windows 正在启动监控服务。";
                    WindowsServiceAccentBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                    break;
                case WindowsServiceState.StopPending:
                    WindowsServiceStatusText = "正在停止";
                    WindowsServiceDescription = "Windows 正在停止监控服务。";
                    WindowsServiceAccentBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
                    break;
                default:
                    WindowsServiceStatusText = "状态未知";
                    WindowsServiceDescription = "暂时无法确认后台服务状态。";
                    WindowsServiceAccentBrush = OfflineBrush;
                    break;
            }
        }
        catch (Exception exception)
        {
            IsWindowsServiceInstalled = false;
            IsWindowsServiceRunning = false;
            WindowsServiceStatusText = "读取失败";
            WindowsServiceDescription = exception.Message;
            WindowsServiceAccentBrush = OfflineBrush;
        }
    }

    private AppSettings BuildSettingsFromForm()
    {
        var keys = BarkIdsText
            .Split(DeviceKeySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new AppSettings
        {
            BarkDeviceKeys = keys,
            ContinuousRinging = ContinuousRinging,
            MessageGroup = MessageGroup?.Trim() ?? string.Empty,
            CriticalAlert = CriticalAlert
        };
    }

    private void OnPowerStatusChanged(object? sender, PowerStatusChangedEventArgs e)
    {
        if (!_windowsServiceManager.IsRunning())
        {
            _ = _notificationCoordinator.HandleStatusChangeAsync(e.Previous, e.Current);
        }
    }

    private void OnPowerStatusObserved(object? sender, PowerStatusObservedEventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() => UpdatePowerDisplay(e.Current));
    }

    private void OnPowerReadFailed(object? sender, Exception exception)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            LastUpdatedText = $"读取失败 · {DateTime.Now:HH:mm:ss}";
            PowerDescription = exception.Message;
        });
    }

    private void OnNotificationReported(object? sender, NotificationReportEventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() => LastNotificationText = e.Message);
    }

    private void UpdatePowerDisplay(PowerStatusSnapshot status)
    {
        switch (status.LineState)
        {
            case PowerLineState.Online:
                PowerStatusText = "市电已连接";
                PowerDescription = status.IsCharging ? "供电正常，电池正在充电" : "供电正常，系统运行稳定";
                PowerAccentBrush = OnlineBrush;
                PowerCardBackground = OnlineBackground;
                break;
            case PowerLineState.Offline:
                PowerStatusText = "市电已断开";
                PowerDescription = "当前正在使用 UPS 电池供电";
                PowerAccentBrush = OfflineBrush;
                PowerCardBackground = OfflineBackground;
                break;
            default:
                PowerStatusText = "电源状态未知";
                PowerDescription = "系统未能识别当前供电来源";
                PowerAccentBrush = UnknownBrush;
                PowerCardBackground = UnknownBackground;
                break;
        }

        if (!status.HasBattery)
        {
            BatteryText = "--";
            BatteryPercentage = 0;
            BatteryDescription = "系统未检测到电池或 UPS 电量接口";
        }
        else if (status.BatteryPercentage.HasValue)
        {
            BatteryText = $"{status.BatteryPercentage.Value}%";
            BatteryPercentage = status.BatteryPercentage.Value;
            BatteryDescription = status.IsCharging ? "电池正在充电" : GetBatteryDescription(status.BatteryPercentage.Value);
        }
        else
        {
            BatteryText = "未知";
            BatteryPercentage = 0;
            BatteryDescription = "设备未报告剩余电量";
        }

        LastUpdatedText = $"最后更新 · {status.ObservedAt:yyyy-MM-dd HH:mm:ss}";
    }

    private static string GetBatteryDescription(int percentage)
    {
        return percentage switch
        {
            <= 10 => "电量严重不足，请尽快处理",
            <= 30 => "电量偏低，请关注续航",
            <= 70 => "电池电量正常",
            _ => "电池电量充足"
        };
    }

    private void HandleCommandException(Exception exception)
    {
        OperationMessage = exception.Message;
    }
}
