# 第三方来源、版权与开源许可说明

## 1. 本项目开源许可

本项目（PC Control Center）采用 **GNU General Public License v3.0 (GPL-3.0-or-later)** 开源许可证。完整许可证文本参见仓库根目录 [LICENSE](file:///F:/PCControlCenter/LICENSE)。

## 2. 社区逆向工程与硬件接口参考

- **参考来源**：[ThinkBookFanControl](https://github.com/lhzlhz419/ThinkBookFanControl) (Author: lhzlhz419)
- **参考范围**：仅参考了联想 ThinkBook 笔记本机型底层 ACPI / WMI 方法签名、风扇转速读取及手动调速的功能编号（Function ID）。
- **实现差异与独立性**：
  1. 本项目未复制、链接或分发 ThinkBookFanControl 项目的源代码或编译二进制成品。
  2. 本项目全部代码基于 C# 13 / .NET 10 独立重写，并设计了全新的分层架构（Core 抽象层、Windows 适配器层、asInvoker 普通权限 WPF 桌面客户端、受控提权 Broker 与安全守护机制）。
  3. 引入了严格的硬件型号白名单核验、双向进程令牌核验、单次显式授权、限时试运行（强制看门狗超时回退）及自动回滚机制，确保系统与硬件稳定性。
- **免责声明**：硬件底层读写控制具有潜在物理风险。本项目基于社区公开技术成果独立实现，不对因硬件参数调整导致的任何硬件损伤、固件故障或数据损失承担任何连带法律责任。

## 3. 厂商官方技术文档

- **联想官方 BIOS WMI 参考指南**：[Lenovo BIOS WMI Guide](https://docs.lenovocdrt.com/ref/bios/wmi/wmi_guide/)。仅用于标准只读系统信息枚举参考，不作为跨机型风扇底层控制的官方 API 承诺。
- **NVIDIA System Management Interface (nvidia-smi)**：本项目通过固定只读命令行查询瞬时 GPU 温度、利用率及功耗指标，未链接其私有 API 动态库。

## 4. 依赖库与运行时分发

- **Microsoft .NET 10 Runtime**：采用 MIT License。自包含（Self-Contained）打包分发时包含官方运行库，并附带官方发布的 [DOTNET-LICENSE.txt](file:///F:/PCControlCenter/DOTNET-LICENSE.txt) 与 [DOTNET-THIRD-PARTY-NOTICES.txt](file:///F:/PCControlCenter/DOTNET-THIRD-PARTY-NOTICES.txt)。
- **外部包依赖**：截至当前版本，本项目核心层与适配器层均未引入任何非官方第三方 NuGet 扩展包，直接使用 .NET 基础类库与 Windows 系统原生组件。

## 5. 商标与产品标识

- 本项目未使用任何联想（Lenovo）、ThinkBook、拯救者（Legion）、华硕（ASUS）、玩家国度（ROG）、机械革命（MECHREVO）或 NVIDIA 等厂商受保护的品牌商标、徽标或官方软件图标。
- 桌面客户端与安装包内置图标及 UI 资源均为独立设计或采用通用系统开源矢量符号。

