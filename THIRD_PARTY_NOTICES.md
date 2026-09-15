# 来源记录

本阶段新增代码未引入第三方 NuGet 包。使用 .NET SDK 和 Windows 已安装 PowerShell/CIM 能力，未分发官方驱动或社区项目二进制。

ThinkBook 只读功能编号与身份约束延续本地原型调查，参考 https://github.com/lhzlhz419/ThinkBookFanControl 。公开发布前核对该来源的许可和具体参考范围。原型 EnergyDrv、性能模式、显卡写入实现未纳入新项目。

联想官方 BIOS WMI 文档：https://docs.lenovocdrt.com/ref/bios/wmi/wmi_guide/ 。该文档不是跨系列风扇 API 保证。

.NET 生命周期：https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core 。当前本机使用 SDK 9.0.316；计划公开发行前升级 LTS。

未使用联想、华硕、机械革命的标志或旧版 Y 图标。设计文件等待 Figma 交接。
