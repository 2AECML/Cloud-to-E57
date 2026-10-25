# 完整 FLS 四任务稳定性验证

此测试需要本机已安装并授权 FARO SDK。先将主程序构建到启动脚本使用的 x64 Release 目录，再构建本项目。

```powershell
dotnet run --project Tests/FaroStabilityRegression/FaroStabilityRegression.csproj -c Release -- C:/test/report E:/test/output C:/scans/one.fls C:/scans/two.fls C:/scans/three.fls C:/scans/four.fls C:/scans/known-invalid.fls
```

最后一个输入应为预先确认无法打开的扫描，用来验证坏输入不会终止其他任务。输出盘应有足够空间（建议至少 20 GiB 安全余量以外再预留 20 GiB）。不应复用真实项目输出目录。

- 创建与正式程序相同的原主窗口和转换窗口，并在 STA 的 Windows 消息循环中运行默认转换服务；窗口保持隐藏。
- 并发上限为 4；对四个完整扫描各执行两轮，首轮单文件、次轮分段，每段 1,000 条扫描线。没有扫描线采样上限。
- 穿插一个已知坏输入，确认其仅作为单项失败，其他任务继续。
- 完整回读所有输出字段，分段列索引还原为全局索引后，比较每个输入两轮的字段摘要。
- 检查窗口存活、UI 心跳、成功/失败数量、原文件 SHA-256，逐项保存报告。
- 输出 `outputs_to_delete.json`，列出已验证的测试点云。测试进程退出后才删除这些文件，避免系统索引或读取器句柄尚未释放造成的清理干扰；保留扫描说明及测试结果。

模拟工作进程原生崩溃、强制终止、不响应取消和父进程退出等故障边界，由 `PerformanceRegression` 的独立子进程夹具验证。
