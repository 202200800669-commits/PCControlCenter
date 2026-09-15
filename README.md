# PC Control Center

多品牌 Windows 电脑控制中心。当前版本为 **0.1.0-alpha.2 架构与只读诊断里程碑**，不是完整硬件控制发行版。

## 当前可用

- Windows 设备识别、内存占用、电池电量、内屏亮度读取，读取不到时明确标记不可用。
- ThinkBook 16p G6 IAX / 21R0 / R2CN57WW 的双风扇只读检测，接口不可用时降级。
- 华硕、机械革命品牌分流与扩展位置；尚无这两个品牌的专用控制功能。
- 可选一次性 UAC 权限代理，仅处理已匹配 ThinkBook 的风扇读取，完成后退出。
- 本地诊断预览和 ZIP 导出，不自动上传，不采集序列号、主机名和网络标识。
- 所有真实适配器均禁用写入；现有独立 ThinkBook 控制程序继续单独使用。

## 构建和运行

当前使用已安装的 .NET SDK 9.0.316。公开发行前迁移到 .NET 10 LTS 并重新验证。

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
