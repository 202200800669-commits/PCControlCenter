# PC Control Center

多品牌 Windows 电脑控制中心。当前版本为 **0.1.0-alpha.9 架构与限时风扇试运行里程碑**，不是完整硬件控制发行版。

## 当前可用

- Windows 设备识别、内存占用、电池电量、内屏亮度读取，读取不到时明确标记不可用。
- ThinkBook 16p G6 IAX / 21R0 / R2CN57WW 的双风扇只读检测，接口不可用时降级。
- 华硕、机械革命品牌分流与扩展位置；尚无这两个品牌的专用控制功能。
- 可选一次性 UAC 权限代理，处理已匹配 ThinkBook 的风扇读取与显式限时试运行，完成后退出。
- 诊断包含 Windows 版本、主板产品、CPU 与显卡驱动版本，不包含设备序列号。
- 本地诊断预览和 ZIP 导出，不自动上传，不采集序列号、主机名和网络标识。
- 连续只读监控 `watch 3`、反馈包检查、带冲突检测与备份恢复的非硬件配置核心。
- 已验证参考 ThinkBook 可显式执行 5–30 秒风扇试运行；结束后发送恢复自动命令。其他型号没有写入入口，现有独立控制台继续保留。

## 构建和运行

使用 .NET 10 LTS，SDK 版本固定在 global.json。本机便携 SDK 位于 local/dotnet10，运行 scripts/verify.ps1 和 scripts/package.ps1 会优先使用它。自包含测试包不要求另装运行时。

```powershell
dotnet build PCControlCenter.sln -c Release
dotnet run --project tests/PCControlCenter.Tests -c Release
dotnet run --project src/PCControlCenter.Cli -c Release -- probe
dotnet run --project src/PCControlCenter.Cli -c Release -- export feedback.zip
dotnet run --project src/PCControlCenter.Cli -c Release -- import-preferences old.json new.json
```

诊断会读取系统信息和已明确匹配的只读接口，不改变硬件设置。普通命令不提权，打包版可用 `pc-control.exe probe --elevated` 请求一次性风扇读取授权。单次探测失败不等于硬件不存在。导出的型号/固件字符串来自设备，分享前仍需自行查看。

## 项目结构

| 路径 | 内容 |
| --- | --- |
| src/PCControlCenter.Core | 能力模型、适配器接口、命令校验与串行执行、诊断格式 |
| src/PCControlCenter.Providers.Windows | 固定只读探测、品牌分流、ThinkBook 型号约束 |
| src/PCControlCenter.Ipc 与 Broker | 有长度限制的命名管道协议与一次性权限代理 |
| src/PCControlCenter.Cli | 可运行的探测与诊断入口 |
| tests/PCControlCenter.Tests | 模拟硬件测试，不访问真实硬件 |
| design | Figma 设计交接位置；图标和新版式未制作 |
| docs | 架构、兼容、迁移和发布门槛 |

## 参与试点

先运行 `probe` 查看，再导出诊断包。通过“新型号适配”模板提供设备型号、BIOS 和读取结果。兼容按型号、固件和功能分别验证；品牌识别不代表可控制风扇。

本地原型和测试原始记录位于被 Git 忽略的 `local/`，不进入仓库或发行物。当前未设置远程仓库、未发布到 GitHub。许可证仍在来源核对阶段，见 [发布门槛](docs/release-gates.md)。

权限代理必须从打包目录中的 `pc-control.exe` 启动；`dotnet run -- ... --elevated` 不支持。系统 UAC 使用当前账户提权；输入其他管理员账户凭据的跨账户模式尚不支持。

## 参考机风扇试运行

在打包目录打开终端，命令会请求一次 UAC：

```powershell
./pc-control.exe fans manual 3500 4500 12
./pc-control.exe fans full 8
./pc-control.exe fans auto
```

手动转速范围为 1500–5500 RPM，试运行时限 5–30 秒；到期、Ctrl+C 或前端断开均触发恢复路径。不要在试验期间用其他软件或热键同时改散热模式。输出区分实际转速、目标是否到达、以及恢复命令发送结果。`AUTO_COMMANDS_SENT_OVERRIDE_OFF` 表示三条恢复命令无异常且全速开关读回为关，不是从 RPM 推断出的自动模式确认。参考固件未提供独立的自动/手动目标读取。

若恢复结果为 `UNCONFIRMED`，先执行 `fans auto` 并核对；程序不能保证系统崩溃或整个进程树被结束后的恢复。断开后原客户端无法接收最终恢复回执，需重新读取。此功能仍标记为实验，常驻控制及温控曲线尚未启用。

收到其他人的报告后，可运行 `pc-control.exe inspect-report feedback.zip` 安全检查，不会启用任何控制功能。详见 [试点反馈流程](docs/feedback-workflow.md)。

连续监控和配置接口见 [监控接口](docs/monitoring.md)、[配置生命周期](docs/preferences.md)。最新回归证据与待办见 [阶段进度](docs/stage-one-status.md)；此前版本的实机测试不能替代当前版本验证。
