# 构建验证

工作流在 Windows 托管 runner 上执行构建、格式检查、模拟测试、自包含打包及命令行参数验证。不会运行有效硬件控制请求，不使用发布凭据，也不会上传 Release。

2026-09-15 核对官方发布记录后，将外部 Actions 固定到完整提交：

- [actions/checkout v7.0.1](https://github.com/actions/checkout/releases/tag/v7.0.1)：`3d3c42e5aac5ba805825da76410c181273ba90b1`。
- [actions/setup-dotnet v6.0.0](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0)：`a98b56852c35b8e3190ac28c8c2271da59106c68`。

更新时先审查官方发行说明和目标提交，再修改固定 SHA。工作流使用只读 contents 权限，checkout 不保留凭据；并发推送会取消同一 ref 的旧构建，单次运行上限 15 分钟。

目前仅完成本地等效命令验证。远程仓库未配置，不能宣称 GitHub Actions 已通过。签名与正式发布仍按发布门槛单独处理。
