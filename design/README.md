# Figma UI/UX 设计资产与交接插槽规范

本项目采用逻辑与视觉彻底解耦的架构设计。**对话框、通用组件、图标与版式主题等视觉元素已预留充分的设计余地，暂不进行硬编码，留空等待 Figma 设计稿完成后无缝接入。**

---

## 🎨 一、 预留设计插槽与规范

| 设计要素 | 当前状态 | 预留插槽位置 | 接入要求 |
| :--- | :--- | :--- | :--- |
| **应用与模块图标** | 字符与纯文字占位 | `design/assets/icons/` | 建议提供 SVG 或矢量 XAML Path，包含：概览、散热、性能、外设、设置及传感器状态图标 |
| **卡片与容器组件** | 标准圆角矩形 (`UIFactory.Card`) | `Views/UIFactory.cs` | 卡片背景色、边框发光、阴影模糊度 (DropShadowEffect)、内外边距 |
| **对话框与提示模态** | 系统级原生轻量弹窗 | 预留 `Views/Components/ModalDialog.cs` | 成功/警告/故障模态框设计、UAC 授权引导插画、确认对话框按钮排版 |
| **滑块与开关控件** | 原生 Slider 与 CheckBox | `Views/Components/StyledControls.cs` | 散热风扇滑块轨道、拖动 Thumb 样式、自启动 Switch 开关样式 |
| **设计令牌 (Tokens)** | 动态资源 `Application.Current.Resources` | `App.xaml` 或 `Styles/Colors.xaml` | 主色/强调色 (Accent)、背景色梯级 (Background/Surface)、文字层级色系 (Text Primary/Secondary/Muted) |

---

## 🧩 二、 核心数据模型与 ViewModel 绑定支持

底层 ViewModel 已完备就绪并经过 100% 自动化测试覆盖，未来 Figma 视觉组件落地时直接绑定对应属性即可：

* **硬件概览**：`vm.DeviceTitle`、`vm.CpuName`、`vm.CpuLoad`、`vm.GpuLoad`、`vm.GpuTempText`、`vm.MemoryText`、`vm.BatteryText`
* **散热与风扇**：`vm.Fan1Rpm`、`vm.Fan2Rpm`、`vm.FanStateText`、`vm.IsBusy`
* **性能与能源**：`vm.PerformanceMode`、`vm.PerformanceModeName`、`vm.EnergyCharge`、`vm.EnergyNight`、`vm.EnergyKey`
* **个性化偏好**：`vm.AccentColor`、`vm.MinimizeToTray`、`vm.AutoStart`、`vm.PollIntervalSeconds`
* **审计与遥测**：`vm.LogText`、`vm.StatusText`、`vm.CurrentSnapshot`

---

## 🛠️ 三、 接入指引

1. 提交 Figma 设计稿链接或导出矢量资源至 `design/assets/`。
2. 在 `Views/UIFactory.cs` 或 XAML 资源字典中注入对应组件的模板样式。
3. 执行 `scripts/verify.ps1` 确保界面样式接入后，全套 193 项底层逻辑断言与代码签名流水线依然 100% 畅通。
