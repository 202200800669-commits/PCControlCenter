# 阶段二跨品牌硬件协议调研报告：华硕与机械革命

## 一、 调研背景与边界准则

依据《多品牌电脑控制中心开源项目书》阶段二规划，在完成联想 ThinkBook 16p 参考样机稳定性固化（阶段一）后，正式启动华硕（ASUS）与机械革命（MECHREVO）的跨品牌硬件控制协议调研。

### 核心设计原则与安全红线
1. **适配单位是平台而非品牌**：设备识别组合必须包含制造商（Manufacturer）、产品型号（Product/Model）、主板标识（Board）、BIOS 版本及驱动依赖版本。严禁因型号字符串相似或共用代工模具而盲目开放底层写入。
2. **安全红线**：严禁采用未受保护的任意 EC 端口读写（I/O 0x68/0x6C）、严禁内置自研非签名内核驱动、严禁提供无边界的超频降压接口。
3. **分阶段验收准则**：未获得实机借测与恢复验证前，跨品牌适配器严格停留在“品牌发现与通用监控（Discovery & Generic Monitor）”阶段；不支持的控制功能在 UI 与 Broker 层显式禁用。

---

## 二、 华硕（ASUS）官方架构与协议调研

### 1. 官网支持与底层驱动渠道
* **官方检索路径**：华硕官方服务支持中心（`https://www.asus.com/support/`），输入具体笔记本型号（如 ROG Strix、Zephyrus、TUF Gaming 等）进入“驱动程序和软件”专区。
* **核心驱动依赖**：**ASUS System Control Interface v3 (ASUS SCI v3)**
  - 官方安装程序通常命名为 `ASUSSystemControlInterfaceV3.exe`。
  - 安装后在 Windows 设备管理器中位于：`系统设备 -> ASUS System Control Interface`。
  - 官方定位：作为 MyASUS、Armoury Crate 与系统底层固件（BIOS/EC）通信的桥梁驱动。
* **BIOS 前置依赖**：BIOS 设置中“Advanced”菜单项通常包含 `Armoury Crate Control Interface Support`，必须保持为 `Enabled`（开启后 BIOS ACPI 表才会向操作系统暴露 WMI 设备）。

### 2. Windows 接口与通信机制
* **WMI 命名空间与类**：
  - 命名空间：`root\wmi`
  - 核心接口类：`AsusAtkWmi_WMNB`
* **核心控制与状态方法**：
  - `DSTS (Device Status Query)`：
    - 输入参数：32 位整型 `Device ID`（功能标识码）。
    - 返回值：32 位整型状态字（包含支持位标志与当前配置值）。
  - `DEVS (Device Status Set)`：
    - 输入参数：32 位整型 `Device ID` + 32 位整型 `Control Value`（目标控制值）。
    - 返回值：执行结果状态码（通常 `0x00000001` 表示成功）。
* **已确认的关键 Device ID 清单**：
  - **性能/散热模式（Thermal Policy）**：`0x00120075`
    - `0`：标准 / 平衡（Standard / Balanced）
    - `1`：增强 / 狂暴（Turbo）
    - `2`：静音 / 节能（Silent）
  - **电池保养充电上限（Battery Health Charging）**：`0x00120057`
    - 支持受控阈值：`60`（长寿模式）、`80`（平衡模式）、`100`（充满模式）
  - **GPU 工作模式（MUX Switch / Optimus）**：`0x00090020`
    - `0`：标准双显模式（Standard / Optimus）
    - `1`：独显直连模式（Ultimate / dGPU Only，需重启生效）
    - `2`：核显省电模式（Eco / iGPU Only）
  - **风扇控制与转速查询**：`0x00110013`（支持查询风扇转速，部分高端机型支持写入风扇离散曲线节点）

### 3. 开源先验与许可证契合度
* **先验证据**：开源社区项目 G-Helper（基于 C# 开发，采用 GPL-3.0 开源许可）已在华硕全系列游戏本与轻薄本上实证：
  - 仅需依赖系统预装的 ASUS SCI v3 驱动暴露的 `AsusAtkWmi_WMNB` 即可完成性能模式、充电阈值与风扇读取。
  - 完全无需常驻官方庞大的后台服务（Armoury Crate SE / MyASUS）。
* **项目契合度**：本项目已于路径 1 正式确立 **GPL-3.0-or-later** 许可证，二者许可证完全同构，技术实现均基于 .NET / C#，迁移参考具备高度法律与技术可行性。

### 4. 华硕平台技术评估
* **可行性评级**：**高（High）**。
* **优势**：官方底层驱动接口高度标准化，跨 ROG、TUF、天选、灵耀等多代产品保持统一的 Device ID 规范；无需第三方内核驱动。
* **限制与缺口**：需实机验证在非特权进程（普通用户）下读取 WMI 的权限表现，以及受控 Broker 下调用 `DEVS` 的成功率与硬件响应时间。

---

## 三、 机械革命（MECHREVO）官方架构与协议调研

### 1. 官网支持与驱动渠道
* **官方检索路径**：机械革命服务支持官网（`https://www.mechrevo.com/service/` 或 `mechrevo.com` 驱动下载页面）。
* **驱动获取机制**：支持按机身底壳序列号（SN 码）或产品系列（旷世、极光、蛟龙、无界等）检索驱动包。
* **官方控制软件**：**MECHREVO Control Center（电竞控制台）**
  - 提供预设模式调节（办公、游戏、狂暴）、风扇强冷开关、键盘 RGB 灯效以及系统状态监控。

### 2. 硬件平台与代工（ODM）背景
* **ODM 根源**：机械革命笔记本主要基于**同方（Tongfang / Uniwill）**公模与定制模具。
* **嵌入式控制器（EC）**：通常搭载联阳半导体（ITE）系列 EC（如 IT5570E、IT8291 等）。
* **官方控制台服务体系**：
  - 驱动包中包含专用后台常驻服务：`UniwillService`、`OemServiceWinApp`、`TccService` 等。
  - 内核通信依赖专用过滤驱动（如 `GCU.sys`、`UniwillDriver.sys`），控制台前端通过私有命名管道或 IOCTL 与驱动通信。

### 3. 上游 Linux 与跨平台研究证据
* **Linux 内核上游驱动（`uniwill-laptop` / `uniwill-wmi`，Linux 6.x+）**：
  - **WMI GUID 非唯一性陷阱**：内核维护文档（`Documentation/wmi/devices/uniwill-laptop.rst`）指出，Uniwill 固件早期使用的 WMI GUID 直接复制自 Windows 驱动开发样例，不同代工厂或模具可能复用相同 GUID。
  - **DMI 白名单强制要求**：驱动不能仅依赖 WMI 探测自动加载，必须基于严格的 DMI 白名单（Vendor, Product, Board）进行匹配，否则可能在非目标机型上引发死机或超时。
  - **功能覆盖**：在适配机型上，通过 WMI 可读写 CPU/GPU 温度、风扇转速（hwmon）、充电保护阈值以及键盘背光。
* **轻薄本与旧款型号差异**：
  - 部分无界轻薄本并未暴露标准 WMI 写入接口，官方控制台通过私有内核驱动直接写入 EC RAM（I/O 0x68/0x6C 端口）。

### 4. 机械革命平台技术评估
* **可行性评级**：**中低（Medium-Low，碎片化严重）**。
* **关键限制与准则执行**：
  - **坚持项目书红线**：“机械革命如在调查阶段仍没有可确认的写入接口，先交付监控适配和调查记录，写入控制保留为未完成目标，不能以通用界面通过代替三品牌控制验收。”
  - 本项目坚决拒绝为了适配而引入无签名的直接 EC 读写内核驱动（存在烧毁硬件或触发蓝屏风险）。
  - 后续阶段必须先明确借测机型的具体模具（如蛟龙 16 Pro 或旷世 16），并在安装官方驱动的前提下，分析是否能安全复用其标准 WMI 通道或过滤驱动通道。

---

## 四、 本地宿主机初步测试与代码隔离验证

### 1. 本机环境真实负向探测（Negative Probe）
在当前开发宿主机（联想 ThinkBook 16p G6 IAX）上执行 WMI 命名空间检索：
```powershell
Get-CimClass -Namespace root\wmi | Where-Object { $_.CimClassName -match 'Asus|ATK|Uniwill|Tongfang|Mechrevo' }
```
* **实测结果**：输出为空（**Zero Match**）。
* **事实结论**：证实开发机硬件环境纯净，不存在任何华硕或同方/机械革命的专有 WMI 驱动类，避免开发过程中的硬件交叉污染。

### 2. 现有代码库品牌发现机制核验
当前核心库中 `PCControlCenter.Providers.Windows.BrandDiscoveryProvider` 已对华硕与机械革命进行了纯只读的发现适配：
```csharp
public sealed class BrandDiscoveryProvider(IReadOnlyProbe probe, string brand) : GenericProvider(probe)
{
    public override string Id => brand + ".discovery";
    public override bool Matches(DeviceIdentity d) => brand switch
    {
        "asus" => d.Manufacturer.Equals("ASUSTeK COMPUTER INC.", StringComparison.OrdinalIgnoreCase) 
               || d.Manufacturer.Equals("ASUS", StringComparison.OrdinalIgnoreCase),
        "mechrevo" => d.Manufacturer.Equals("MECHREVO", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
```

* **测试套件（`PCControlCenter.Tests`）断言实测**：
  - `registry.Resolve(device with { Manufacturer = "ASUSTeK COMPUTER INC." }).Id == "asus.discovery"` -> **PASS**
  - `registry.Resolve(device with { Manufacturer = "MECHREVO" }).Id == "mechrevo.discovery"` -> **PASS**
  - `registry.Resolve(device with { Manufacturer = "Unknown" }).Id == "windows.generic"` -> **PASS**
  - 跨品牌写入拦截：由于 `BrandDiscoveryProvider` 继承自 `GenericProvider`，其所有能力定义 `CanWrite` 强制为 `false`，写入请求直接返回 `ResultCode.Unsupported` -> **PASS**。

---

## 五、 阶段二下一步推进方案与决策建议

| 阶段任务 | 目标品牌与机型 | 预估工作内容 | 验收门槛 |
| :--- | :--- | :--- | :--- |
| **方案 2A（推荐）：华硕 WMI 原型验证** | 华硕天选（TUF）或 ROG 系列样机（1 台） | 1. 编写基于 `AsusAtkWmi_WMNB` 的只读探针 `AsusProbe`<br/>2. 提取性能模式、电池养护上限只读状态<br/>3. 受控 Broker 写入限时试运行 | 普通用户免提权读取成功；限时试运行与自动恢复回滚实测闭环 |
| **方案 2B：机械革命模具与 WMI 探针深挖** | 机械革命蛟龙/旷世系列样机（1 台） | 1. 只读采集该模具的 DMI 详细字段与已安装驱动列表<br/>2. 探测是否存在 Uniwill 衍生 WMI 类<br/>3. 输出单型号可行性记录 | 若无安全 WMI 接口，按项目书交付只读监控与调研结论，不强行开放写入 |
