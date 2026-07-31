using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using UPSPowerMonitor.ViewModels;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;

namespace UPSPowerMonitor;

public partial class MainWindow : Window
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtApmPowerStatusChange = 0x000A;
    private HwndSource? _windowSource;
    private readonly DrawingIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private readonly Forms.NotifyIcon _notifyIcon;
    private bool _allowExit;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();

        _trayIcon = LoadTrayIcon();
        _trayMenu = CreateTrayMenu();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "UPS 电源监控",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);

        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public void PrepareForSystemShutdown()
    {
        _allowExit = true;
        _notifyIcon.Visible = false;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmPowerBroadcast && wParam.ToInt32() == PbtApmPowerStatusChange)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.RefreshPowerStatus();
            }
        }

        return IntPtr.Zero;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();

        if (_trayHintShown)
        {
            return;
        }

        _trayHintShown = true;
        _notifyIcon.BalloonTipTitle = "UPS 电源监控仍在运行";
        _notifyIcon.BalloonTipText = "程序已最小化到系统托盘。双击图标可恢复窗口，右键可退出。";
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitFromTray()
    {
        _allowExit = true;
        _notifyIcon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem("显示主界面");
        var exitItem = new Forms.ToolStripMenuItem("退出");

        showItem.Click += (_, _) => Dispatcher.Invoke(ShowFromTray);
        exitItem.Click += (_, _) => Dispatcher.Invoke(ExitFromTray);

        menu.Items.Add(showItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private static DrawingIcon LoadTrayIcon()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            using var extractedIcon = DrawingIcon.ExtractAssociatedIcon(Environment.ProcessPath);
            if (extractedIcon is not null)
            {
                return (DrawingIcon)extractedIcon.Clone();
            }
        }

        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/app-icon.ico"));
        using var stream = resource?.Stream
                           ?? throw new InvalidOperationException("无法加载应用托盘图标。");
        using var icon = new DrawingIcon(stream);
        return (DrawingIcon)icon.Clone();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _windowSource?.RemoveHook(WindowMessageHook);
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayMenu.Dispose();
        _trayIcon.Dispose();
    }
}
