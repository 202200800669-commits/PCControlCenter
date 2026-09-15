# 参与开发

提交前执行 `dotnet build PCControlCenter.sln -c Release` 和 `dotnet run --project tests/PCControlCenter.Tests -c Release`。测试应使用模拟提供器，不能在 CI 中探测或改写真实硬件。

新增型号先提交脱敏只读信息，不提交序列号、个人日志、驱动二进制、密钥或厂商软件安装包。硬件写入 PR 需包含型号/固件范围、接口来源、边界条件、读回与恢复证据，并接受维护者审查；不接受任意 EC 写入工具作为通用适配方案。

UI 与图标由项目负责人通过 Figma 提供，本阶段不制作替代设计。
