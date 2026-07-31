using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace UPSPowerMonitor.DesktopServices;

public enum WindowsServiceState
{
    NotInstalled,
    Stopped,
    StartPending,
    StopPending,
    Running,
    Paused,
    Unknown
}

public sealed class WindowsServiceManager
{
    public const string ServiceName = "UPSPowerMonitor";
    public const string InstallArgument = "--install-service";
    public const string UninstallArgument = "--uninstall-service";

    private const uint ScManagerConnect = 0x0001;
    private const uint ScManagerCreateService = 0x0002;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint DeleteAccess = 0x00010000;
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceAutoStart = 0x00000002;
    private const uint ServiceErrorNormal = 0x00000001;
    private const uint ServiceConfigDescription = 1;
    private const uint ServiceControlStop = 1;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceNotActive = 1062;

    public WindowsServiceState GetStatus()
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法连接 Windows 服务控制管理器。");
        }

        try
        {
            var service = OpenService(manager, ServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorServiceDoesNotExist)
                {
                    return WindowsServiceState.NotInstalled;
                }

                throw new Win32Exception(error, "无法读取 UPS 电源监控服务状态。");
            }

            try
            {
                return MapState(QueryState(service));
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public bool IsRunning()
    {
        try
        {
            return GetStatus() == WindowsServiceState.Running;
        }
        catch
        {
            return false;
        }
    }

    public async Task RunElevatedAsync(string commandArgument, CancellationToken cancellationToken = default)
    {
        if (commandArgument is not (InstallArgument or UninstallArgument))
        {
            throw new ArgumentOutOfRangeException(nameof(commandArgument));
        }

        var executablePath = Environment.ProcessPath
                             ?? throw new InvalidOperationException("无法确定当前程序路径。");
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = commandArgument,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法启动管理员权限操作。");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var operation = commandArgument == InstallArgument ? "安装" : "卸载";
            throw new InvalidOperationException($"Windows 服务{operation}失败，退出代码：{process.ExitCode}。");
        }
    }

    public static void InstallService()
    {
        var serviceExecutable = Path.Combine(AppContext.BaseDirectory, "UPSPowerMonitor.Service.exe");
        if (!File.Exists(serviceExecutable))
        {
            throw new FileNotFoundException("发布目录中缺少 UPSPowerMonitor.Service.exe。", serviceExecutable);
        }

        var manager = OpenSCManager(null, null, ScManagerConnect | ScManagerCreateService);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开 Windows 服务控制管理器。");
        }

        try
        {
            var service = CreateService(
                manager,
                ServiceName,
                "UPS 电源监控服务",
                ServiceQueryStatus | ServiceStart | ServiceStop | ServiceChangeConfig | DeleteAccess,
                ServiceWin32OwnProcess,
                ServiceAutoStart,
                ServiceErrorNormal,
                $"\"{serviceExecutable}\"",
                null,
                IntPtr.Zero,
                null,
                null,
                null);

            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "创建 UPS 电源监控服务失败。");
            }

            try
            {
                var description = new ServiceDescription
                {
                    Description = "在无人登录时持续监控 UPS 市电和电池状态，并通过 Bark 发送告警。"
                };
                ChangeServiceConfig2(service, ServiceConfigDescription, ref description);

                if (!StartService(service, 0, null))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorServiceAlreadyRunning)
                    {
                        throw new Win32Exception(error, "启动 UPS 电源监控服务失败。");
                    }
                }

                WaitForState(service, 4, TimeSpan.FromSeconds(15));
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public static void UninstallService()
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开 Windows 服务控制管理器。");
        }

        try
        {
            var service = OpenService(manager, ServiceName, ServiceQueryStatus | ServiceStop | DeleteAccess);
            if (service == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorServiceDoesNotExist)
                {
                    return;
                }

                throw new Win32Exception(error, "无法打开 UPS 电源监控服务。");
            }

            try
            {
                if (QueryState(service) != 1)
                {
                    if (!ControlService(service, ServiceControlStop, out _))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorServiceNotActive)
                        {
                            throw new Win32Exception(error, "停止 UPS 电源监控服务失败。");
                        }
                    }

                    WaitForState(service, 1, TimeSpan.FromSeconds(15));
                }

                if (!DeleteService(service))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "删除 UPS 电源监控服务失败。");
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    private static uint QueryState(IntPtr service)
    {
        if (!QueryServiceStatus(service, out var status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "查询 Windows 服务状态失败。");
        }

        return status.CurrentState;
    }

    private static void WaitForState(IntPtr service, uint expectedState, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (QueryState(service) == expectedState)
            {
                return;
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException("等待 Windows 服务状态变化超时。");
    }

    private static WindowsServiceState MapState(uint state)
    {
        return state switch
        {
            1 => WindowsServiceState.Stopped,
            2 => WindowsServiceState.StartPending,
            3 => WindowsServiceState.StopPending,
            4 => WindowsServiceState.Running,
            7 => WindowsServiceState.Paused,
            _ => WindowsServiceState.Unknown
        };
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateService(
        IntPtr serviceManager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr serviceManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus serviceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartService(IntPtr service, int argumentCount, string[]? arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, uint control, out ServiceStatus serviceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(
        IntPtr service,
        uint infoLevel,
        ref ServiceDescription serviceDescription);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceDescription
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Description;
    }
}
