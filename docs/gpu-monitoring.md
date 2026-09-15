# NVIDIA 只读监控

使用本机 Windows System32 中已有的 nvidia-smi.exe，以固定参数查询显卡序号、温度、负载和功耗。不从 PATH 搜索，不下载或分发 NVIDIA 二进制，不使用频率、电源限制或重置命令。

接口依据 [NVIDIA 官方 nvidia-smi 文档](https://docs.nvidia.com/deploy/nvidia-smi/index.html) 的 selective query 和 CSV 输出选项。官方说明输出格式不承诺跨版本兼容，因此当前解析器遇到格式变化会降级；长期可考虑按官方 NVML ABI 接入。

查询最多等待 3 秒，输出限制 8 KB、8 张显卡。N/A、非有限数和超出校验范围的观测值保留为 null；有效的 0% 负载保留为 0。序号只用于当前采样通道，不作为跨重启的硬件唯一身份。

缺少工具或查询失败时，其他通用监控仍可用。当前不采集 GPU UUID、序列号、进程列表或原始错误输出。AMD、Intel 的温度与功耗接口尚未实现；显示驱动列表中的设备不等于这些指标都可读取。
