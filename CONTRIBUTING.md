# 参与开发

提交前执行 `dotnet build PCControlCenter.sln -c Release` 和 `dotnet run --project tests/PCControlCenter.Tests -c Release`。测试应使用模拟提供器，不能在 CI 中探测或改写真实硬件。

使用 global.json 指定的 SDK；本机也可直接运行 `./scripts/verify.ps1`。C# 格式遵循 `.editorconfig`，执行 `dotnet format whitespace PCControlCenter.sln --no-restore` 整理，追加 `--verify-no-changes` 检查。

适配器需要逐项声明读取与写入能力。品牌发现不得升级为写入许可；报告导入不得修改型号白名单。硬件 API 必须使用固定操作和类型化参数，不能接受脚本、寄存器地址或任意驱动命令作为用户输入。

新增控制路径必须覆盖：型号或 BIOS 不匹配时零写入、范围和时限拒绝、部分成功后的恢复、恢复失败可见、取消与并发占用。模拟测试中所有硬件调用必须被替身覆盖；共享资源名称要单独隔离，避免影响正在运行的真实应用。

本地测试证据放在被忽略的 `local/`。PR 只提交经过人工核对的脱敏结论，明确区分模拟、旧版实机和当前版本实机。`scripts/package.ps1` 校验清单，`scripts/smoke-cli.ps1` 需要 PowerShell 7，仅运行帮助和无效参数，不请求硬件操作。

新增型号先提交脱敏只读信息，不提交序列号、个人日志、驱动二进制、密钥或厂商软件安装包。硬件写入 PR 需包含型号/固件范围、接口来源、边界条件、读回与恢复证据，并接受维护者审查；不接受任意 EC 写入工具作为通用适配方案。

UI 与图标由项目负责人通过 Figma 提供，本阶段不制作替代设计。
