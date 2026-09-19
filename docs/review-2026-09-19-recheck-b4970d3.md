# 第四轮复查与修复说明

提交：`b4970d3`。日期：2026-09-19。

结论：构建成功，完整验证输出 `260 tests passed`。本轮解决了若干前次问题，但以下缺陷仍在，不宜认定功能验收完成。本次只新增复查资料，未修改生产源代码，没有执行真实硬件写入。

## 已确认推进

- 亮度脚本提取为嵌入资源，PowerShell 5.1 解析与 mock CIM 测试通过。
- 非零 WMI ReturnValue 会报告失败；脚本能够单独返回无读回状态。
- 场景风扇步骤现在返回结果；失败、取消和明确恢复未确认时不会宣布场景成功，新增了相关测试。
- 亮度队列新增可注入执行器，测试了合并与取消。
- 配置加载改为异步初始化，移除了之前同步等待异步 I/O 的包装。

## P1：恢复进程仍会在固定等待后被强杀

位置：`src/PCControlCenter.Providers.Windows/ModeSessionRunner.cs:55-59`、`EnergySessionRunner.cs:55-59`。

上轮 15 秒现改为 60 秒，且 Kill 改为 Kill(true)。如果驱动或恢复超过这个时间，仍会终止负责恢复的工作者，finally 没有保证执行完。这是延长等待，未解决此前提出的恢复所有权问题。本轮没有验证超过 60 秒的恢复路径，不将其描述为已发生的真机故障。

修复要求：为目标操作、底层调用、恢复和最终超时建立明确协议，限定可中断的调用时长；不能仅依靠杀掉拥有恢复责任的进程来结束恢复。超时且结果未知时保留未确认和适当控制限制，明确后台恢复、锁和进程的回收方式。不能简单删 Kill 留下无人管理的任务。

验收：假驱动恢复耗时超过 60 秒的用例，不截断恢复或提前释放写入约束；无法保证恢复时返回明确未确认，并验证后续操作受到限制。

## P2：亮度未确认仍被界面转换成已确认

位置：`src/PCControlCenter.Desktop/Services/BrightnessService.cs:215-217`；`ViewModels/MainViewModel.cs:498-502`；`ApplyProfileAsync` 的亮度步骤。

服务把 SuccessUnconfirmed 映射为 Success=true、ConfirmedValue=null，视图模型仍执行 `Brightness = res.ConfirmedValue ?? percent`，随后记录“屏幕亮度已确认”。脚本区分了未确认，消费端却丢弃了这个区别。

补充模拟使用真实 MainViewModel 和注入的亮度执行器，返回 SuccessUnconfirmed/null，设置目标 73，实际得到：

```text
UnconfirmedBrightness=73
UnconfirmedStatus=屏幕亮度已调整为 73%
ClaimsConfirmed=True
```

修复要求：分开目标值、已发送状态、确认值。未确认时保留最后一次确认值或显示未知，文案只能表示请求已发送；场景不得把未确认当成全部已确认成功。初始 GetBrightnessAsync 已有实现，但没有接入视图模型初始化，当前仍不能把初始固定亮度视为真实读数。

验收：对服务到视图模型再到场景的完整消费链注入 SuccessUnconfirmed/null，不能出现“已确认”或伪造的确认值；测试无初始读回与有读回两种初始化状态。

## P2：读回实例匹配失败会借用另一块显示器

位置：`src/PCControlCenter.Desktop/Services/BrightnessSession.ps1:25-32`。

按 InstanceName 匹配失败后，脚本回退到任意 Active 亮度实例。这意味着写入 TARGET 后，可把 OTHER 的亮度报告为 TARGET 的确认结果。当前多实例测试只覆盖目标存在的情况，没有覆盖目标缺失。

补充 mock：目标实例 TARGET，目标值 85，读回列表只有 OTHER=12。生产脚本返回：

```json
{"Confirmed":12,"Status":"Success"}
```

修复要求：已选择目标实例后，读回必须匹配同一实例；缺失时返回未确认，不能回退为别的面板。目标标识缺失或多设备存在歧义时也需明确处理。

验收：目标存在但读回顺序变化时正确匹配；目标缺失、目标不活动、标识缺失等情况不得借用其他显示器的值。

## P2：恢复等待超时的真实异常路径未处理

位置：`src/PCControlCenter.Desktop/Services/BrokerExecutor.cs:45`；`MainViewModel.cs:928-969`。

RealBrokerExecutor 在 `await Task.Delay(200, ct)` 期间取消会抛 OperationCanceledException，不会稳定返回 false。调用处没有捕获恢复等待阶段的异常，finally 又无条件设置 IsBusy=false，留下“取消中（恢复中）”状态并向 UI 事件处理函数抛错。当前 fake waiter 返回 false 的测试无法覆盖这一行为。

补充模拟将恢复等待设为取消任务，实际结果：

```text
RecoveryWaitCancellationEscapes=True
BusyAfterRecoveryWaitException=False
FanStateAfterRecoveryWaitException=取消中 (恢复中)
```

修复要求：在恢复等待服务和调用层约定明确的超时/取消结果，保证终态映射与清理不会被异常跳过；未确认时不能一边显示恢复中一边无条件开放冲突操作。补上真实 Task.Delay 令牌取消语义的测试，而不只测返回 false。

## P2：锁释放查询仍不等于原操作的恢复终态

位置：`BrokerExecutor.cs:19-45`；`MainViewModel.cs:930-949`。

实现只检查固定全局 mutex，无法关联到原请求；锁不存在、可获取或 abandoned 都返回 true。工作者尚未创建锁、异常退出和正常恢复完成可能被混为同一结果。UI 目前没有宣称已独立确认自动，但仍据此解除控制锁定；超时返回 false 时 finally 也解除锁定。尚未实现原操作的最终回执跟踪。

修复要求：用请求标识关联工作者状态/最终回执，区分未启动、运行、恢复、终止但未确认和恢复完成；mutex 只能辅助判断互斥，不能充当恢复成功证明。AbandonedMutexException 必须作为异常结束证据处理，并正确释放本次获得的互斥所有权。若无法取得终态，应明确保持未确认及控制约束。

验收：取消发生在工作者创建锁之前、持锁恢复中、异常退出及正常恢复结束四种场景，都能准确映射状态，不因瞬时无锁提前判定结束。

## 复验材料与下一步

- 完整测试日志：`local/review-20260919/verify-b4970d3.log`。
- 补充视图模型模拟：`local/review-b4970d3/Probe.csproj`、`Program.cs`、`probe-results.log`，不控制硬件。
- local 目录被 Git 忽略，上述本机材料不会自动进入提交。

下一轮先修复这些实际消费链与异常路径；保留本轮新增测试，并补上本文反例。测试总数增加并不表示真实恢复协议已验收。安装、签名和多型号真机兼容范围不在本轮复验中，仍按此前报告单独验证。
