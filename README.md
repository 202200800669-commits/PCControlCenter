# PC Control Center (多品牌电脑控制中心)

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-0078D6.svg)](docs/compatibility.md)
[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4.svg)](global.json)
[![Tests](https://img.shields.io/badge/Tests-193%20Passed-success.svg)](tests/PCControlCenter.Tests)

**PC Control Center** 是一款面向多品牌 Windows 笔记本电脑的现代化、轻量级、开源控制中心。项目旨在替代厂商臃肿的官方后台全家桶，提供普通权限桌面客户端、细粒度硬件状态监控、最小权限受控代理（Broker）与规范的社区适配框架。

> [!IMPORTANT]
> **开源发布状态与安全原则**：
> 1. **权限最小化**：桌面端与 CLI 默认以普通用户权限（`asInvoker`）运行，零常驻后台特权服务；仅在用户显式触发硬件控制时，通过一次性 Broker 请求 UAC 提权并在操作完成后自动退出。
> 2. **未知型号零写入**：严格执行“适配单位是平台而非品牌”原则，未获社区或样机实测验证的设备一律关闭硬件写入，绝不盲目尝试。
> 3. **脱敏与隐私底线**：严禁采集序列号（SN）、UUID、用户名、文件路径或网络标识；所有诊断与反馈数据全程透明可读。

---

## 💻 品牌与硬件支持矩阵

| 品牌 / 模具 | 驱动与通信通道 | 当前项目状态 | 控制写入支持能力 |
| :--- | :--- | :--- | :--- |
| **联想 ThinkBook 16p G6 IAX**<br/>`(21R0 / R2CN57WW)` | 联想官方 ITS / ACPI 驱动 | ✅ **参考样机完整支持** | • 双风扇转速读取与限时试运行 (自动回滚)<br/>• 性能模式切换 (0 均衡 / 1 野兽 / 3 安静)<br/>• 电池养护模式 / 键盘背光调节 |
| **华硕 ASUS / ROG / TUF** | ASUS System Control Interface v3<br/>(`root\wmi: AsusAtkWmi_WMNB`) | 🚀 **概念适配就绪**<br/>(待 GitHub 用户反馈) | • 识别 `asus.discovery`<br/>• 性能模式 (`0x00120075`)、充电上限 (`0x00120057`)、风扇 (`0x00110013`) 概念就绪<br/>• 硬件写入受控锁定，等待社区实测 |
| **机械革命 MECHREVO**<br/>(同方 / Uniwill 模具) | 官方控制台驱动 / 服务架构<br/>(`UniwillService` / `GCU.sys`) | 🚀 **概念适配就绪**<br/>(待 GitHub 用户反馈) | • 识别 `mechrevo.discovery`<br/>• 模具 WMI 过滤探测与概念映射就绪<br/>• 硬件写入受控锁定，严禁无签名裸写 EC |
| **通用 Windows PC**<br/>(任意品牌与台式机/笔记本) | Windows 标准 CIM / WMI API<br/>+ NVIDIA 官方遥测 | 🌐 **通用只读监控** | • 内存占用率、电池电量、屏幕亮度<br/>• **供电状态** (AC 适配器 / 电池放电)<br/>• **Windows 活动电源计划** (平衡/高性能等)<br/>• **NVIDIA 显卡遥测** (温度、负载、功耗) |

---

## 🎨 UI/UX 与 Figma 设计插槽（设计余地保留）

为了确保最终产品的视觉美感与极佳用户体验，**当前桌面客户端的通用组件、对话框、图标与高阶动效已充分预留设计余地，留空等待 Figma 原创设计稿交付**：

* **前后端彻底解耦**：底层 `PCControlCenter.Core`、`PCControlCenter.Desktop.ViewModels` 与业务服务层已经过 100% 自动化测试覆盖，数据绑定与状态流转完备。
* **插槽规范与交接指引**：详见 [`design/README.md`](design/README.md)。Figma 设计稿完成后，仅需在 `Views/` 注入矢量资源或 XAML 样式模板，即可无缝完成现代化视觉焕新，无需变动任何底层硬件代码。

---

## 🚀 交付形态与安装指南

针对不同用户与运维场景，提供三种互补的安装与交付方式：

### 1. 便携绿色版 (Portable ZIP)
* 发布目录：`artifacts/pc-control-alpha-*.zip`
* 特性：271 个自包含文件的单层平铺结构，解压即用，无需安装任何 .NET 运行时或环境依赖。

### 2. Inno Setup 现代化安装包 (GUI 向导)
* 安装脚本：[`installer/setup.iss`](installer/setup.iss)
* 特性：符合开源标准的安装引导，请求最低权限（`PrivilegesRequired=lowest`），展示 GPL-3.0 许可协议，支持自定义安装路径并可选创建桌面与开始菜单快捷方式。

### 3. 单用户免提权脚本安装与卸载
* 安装脚本：`pwsh scripts/install-peruser.ps1`
  - 自动同步文件至 `%LocalAppData%\Programs\PCControlCenter`，建立快捷方式并在“Windows 设置 -> 已安装的应用”中注册标准卸载入口。
* 干净卸载脚本：`pwsh scripts/uninstall-peruser.ps1`
  - 自动终止运行进程、安全移除快捷方式与注册表卸载项，实现零垃圾残留。

### 4. Authenticode 代码签名与验签工具链
* 自动签名：[`scripts/sign-package.ps1`](scripts/sign-package.ps1)（支持探测 Windows 11 SDK `signtool.exe`，集成 DigiCert RFC 3161 时间戳服务，签名后自动重算哈希清单）。
* 安全验签：[`scripts/verify-signatures.ps1`](scripts/verify-signatures.ps1)（核验发布包二进制数字签名状态）。

---

## 📋 社区参与与 GitHub 适配反馈

我们热烈欢迎华硕、机械革命、联想及其他品牌电脑的用户参与适配！为了消除跨机型反馈门槛，我们提供了**一键 Issue 模板生成功能**：

### 方式 A：桌面客户端一键复制
1. 启动桌面端 `pc-control-desktop.exe`，进入“设置”页面。
2. 在“程序维护”卡片中，点击 **“复制 GitHub 反馈模板”**。
3. 系统将自动将完整的硬件环境、底层接口探查结果与传感器快照格式化为 Markdown 并写入剪贴板。
4. 前往本仓库 [New Issue](https://github.com/issues) 页面，选择“新型号适配”并 `Ctrl+V` 粘贴即可。

### 方式 B：CLI 命令行导出
```powershell
# 直接在终端输出格式化反馈 Markdown
./pc-control.exe feedback-template

# 或直接导出到文件
./pc-control.exe feedback-template my-device.md

# 导出脱敏诊断压缩包（用于深度故障排查）
./pc-control.exe export feedback.zip
```

> [!NOTE]
> 导出的数据仅包含硬件型号名称、BIOS 版本、系统构建号、接口探查名称及当前传感器读数，**绝无序列号与个人隐私**。

---

## 🛠️ 本地构建与开发者指南

### 环境依赖
* Windows 11 / Windows 10 (x64)
* .NET 10.0 SDK（项目自带便携版位于 `local/dotnet10`）

### 常用命令
```powershell
# 编译全解决方案 (Debug / Release)
dotnet build PCControlCenter.sln -c Release

# 运行自动化测试套件 (193 项断言)
dotnet run --project tests/PCControlCenter.Tests -c Release --no-build

# 执行源代码安全边界检查 (验证零私钥、零敏感硬编码)
pwsh scripts/verify-source.ps1

# 代码空白与格式校验
dotnet format whitespace PCControlCenter.sln --no-restore --verify-no-changes

# 执行端到端构建、测试与平铺自包含打包
pwsh scripts/package.ps1

# 执行发布包 CLI 冒烟验证 (15 项指令检查)
pwsh scripts/smoke-cli.ps1 -PackageDirectory artifacts/pc-control-alpha-*
```

---

## 📄 开源许可证与技术边界声明

* **开源许可证**：本项目采用 [GNU General Public License v3.0 or later (GPL-3.0-or-later)](LICENSE)。
* **第三方来源与逆向工程边界**：详见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。本项目基于公开文档、上游 Linux 内核模块（如 `uniwill-laptop`）及合规开源参考（如 G-Helper）进行净室逆向与概念抽象，不包含任何厂商受版权保护的未授权专有二进制驱动。
* **安全漏洞响应**：详见 [`SECURITY.md`](SECURITY.md)。
