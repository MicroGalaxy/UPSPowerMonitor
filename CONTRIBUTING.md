# 贡献指南

感谢你愿意帮助改进 UPS Power Monitor。

## 提交问题

提交 Issue 前请先搜索是否已有相同问题，并尽量提供：

- Windows 版本和 UPS 型号；
- UPS 驱动是否能在 Windows 中显示电量；
- 复现步骤、预期结果和实际结果；
- 相关错误信息或 Windows 事件日志；
- 是否以托盘模式或 Windows 服务模式运行。

请勿提交 Bark ID、设备密钥、服务器地址或其他敏感信息。

## 本地开发

需要 Windows、.NET 8 SDK，以及 Visual Studio 2022 的“.NET 桌面开发”工作负载。

```powershell
dotnet restore .\UPSPowerMonitor.sln --disable-parallel
dotnet build .\UPSPowerMonitor.sln -c Debug --no-restore -m:1
```

运行 WPF 主程序：

```powershell
.\bin\Debug\net8.0-windows10.0.17763.0\UPSPowerMonitor.exe
```

以控制台模式测试服务宿主，不会注册 Windows 服务：

```powershell
.\UPSPowerMonitor.Service\bin\Debug\net8.0-windows10.0.17763.0\UPSPowerMonitor.Service.exe --console
```

## Pull Request

1. 从最新 `main` 创建功能分支。
2. 保持变更范围集中，并说明行为变化和验证方法。
3. 提交前运行 Release 构建。
4. 涉及 UI 时附上截图；涉及通知策略时说明断电、恢复和电量阈值场景。
5. 不要提交 `bin/`、`obj/`、`publish/`、本机设置或 Bark ID。

```powershell
dotnet build .\UPSPowerMonitor.sln -c Release --no-restore -m:1
dotnet publish .\UPSPowerMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish\win-x64
```

## 代码约定

- 启用可空引用类型，避免无意义的 null 抑制。
- 网络和轮询操作使用异步 API，并正确传递 `CancellationToken`。
- UI 线程只负责展示，监控和 Bark 请求放在服务层。
- Windows 服务与 WPF 应共享核心逻辑，避免维护两套通知策略。
- 面向 Windows Server 2019，避免使用更高版本 Windows 独占 API。
