# 试点反馈流程

用户先用 `probe` 查看，再用 `export feedback.zip` 导出；核对 diagnostics.json 后提交 GitHub Issue。需要权限的读取由 --elevated 明确触发，普通诊断不要求管理员。

维护者使用 `pc-control.exe inspect-report feedback.zip` 检查收到的文件。检查器限制 ZIP 和 JSON 大小、只接收 diagnostics.json、拒绝多余文件和重复字段，不解压文件，也不运行报告携带的任何内容。

输出中的 CompatibilityKey 仅用于归组同一型号与固件组合，不是物理电脑唯一 ID，也不证明两份报告来自不同用户。所有结果固定标记 UserReportedUnverified，报告声称的能力不会写入适配器或打开硬件权限。

收集后按型号与固件安排只读试点，再做人工审查与实机控制验证。未经验证的 BIOS 更新、代工同平台或用户自行声称支持，都不能自动转为写入兼容名单。

显卡驱动列表来自注册表时标记 InstalledDriverRegistry，可能包含虚拟或非当前活动设备。需要通过后续实机验证判定可用控制接口。
