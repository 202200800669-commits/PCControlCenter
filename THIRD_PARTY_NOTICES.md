# 来源记录

本阶段新增代码未引入第三方 NuGet 包。使用 .NET SDK 和 Windows 已安装 PowerShell/CIM 能力，未分发官方驱动或社区项目二进制。

ThinkBook 只读功能编号与身份约束延续本地原型调查，参考 https://github.com/lhzlhz419/ThinkBookFanControl 。公开发布前核对该来源的许可和具体参考范围。原型 EnergyDrv、性能模式、显卡写入实现未纳入新项目。

联想官方 BIOS WMI 文档：https://docs.lenovocdrt.com/ref/bios/wmi/wmi_guide/ 。该文档不是跨系列风扇 API 保证。

.NET 生命周期：https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core 。现已使用 SDK 10.0.401 / Runtime 10.0.12（Microsoft 官方 ZIP 下载并核对 SHA-512）；运行时随自包含测试包分发。

未使用联想、华硕、机械革命的标志或旧版 Y 图标。设计文件等待 Figma 交接。

自包含发行物内含 Microsoft.NETCore.App Runtime。打包脚本从对应版本 NuGet 运行时包复制 DOTNET-LICENSE.txt 和 DOTNET-THIRD-PARTY-NOTICES.txt；不使用 SDK 中无关组件的通知代替运行时通知。BUILD.json 记录构建的源码提交、脏状态、SDK 和运行时版本。
