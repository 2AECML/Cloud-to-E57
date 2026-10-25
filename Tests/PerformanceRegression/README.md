# 转换性能回归验证

运行前构建 x64 Release 主程序（输出到启动脚本使用的 `EasyTry/bin/x64/Release/net8.0-windows-tasklist/`）。
本测试需要 `Native/build-e57-native.ps1` 生成的原生写入器及 `Native/.build-e57/bridge-build/Release/EasyTryE57Native.dll`。
后者以 `EasyTryE57Readback.dll` 名称复制到测试目录，用于逐点回读；不会替换启动目录的原生组件。

```powershell
dotnet run --project Tests/PerformanceRegression/PerformanceRegression.csproj -c Release
```

测试使用生成的小型点云，不打开或改动原始扫描：

- 校验 1、2、4 个任务的并发上限，独立任务补位，以及连续输入重叠时的串行保护。
- 校验取消、异常、停止启动待处理项，以及等待所有活动任务清理完成。
- 校验 FARO（含自动识别）从创建到释放均在同一个专用 STA 线程运行，ZFS 保留 MTA 线程池；校验预取消、中途取消和线程内异常的传播与资源清理。
- 并行运行单文件与跨批次边界的分段转换，逐点核对坐标、两种强度和行列索引；验证每个源独立释放。
- 连续写入 64 个大小不同的批次（含较小末批），完整回读约 168 万个点，检查旧缓冲区尾部数据不会被写出。
- 记录仅 `WriteBatch` 调用的托管内存分配；固定数据集应少于 4 MiB，避免恢复为逐批分配七个大数组。
- 校验取消、空批次和超容量批次，并检查临时输出清理。
- 使用独立测试进程验证 Unicode 路径、进度和报告传输、NaN 空范围、连续合并及分段结果；模拟工作进程以 `0xC0000374` 退出，验证主进程和其他任务继续运行。
- 验证普通失败、没有最终报告的零退出码、正常取消、不响应取消、父进程退出后的子进程终止，以及只删除新临时文件、保留原有输出。

默认 `PointCloudConversionService()` 使用生产进程隔离路径。显式传入源和写入器工厂的重载用于本地单元夹具与自定义管线，仍在调用进程内执行，不能用它单独证明厂商 SDK 多实例在主窗口内稳定。

单独记录写入基准：

```powershell
dotnet run --project Tests/PerformanceRegression/PerformanceRegression.csproj -c Release -- --writer-benchmark C:/absolute/path/result.json
```

基准只覆盖点字段打包与 E57 写入，不代表含厂商 SDK 读取、磁盘竞争的实际整批提速。

可选的真实 SDK 小样本验证（需要已安装并授权厂商 SDK，输入必须为两个独立逻辑扫描）：

```powershell
dotnet run --project Tests/PerformanceRegression/PerformanceRegression.csproj -c Release -- --sdk-smoke C:/absolute/path/results.json C:/scans/first.zfs C:/scans/second.zfs
```

此模式分别以 1、2 个任务并发运行，各输入仅读取前 128 条扫描线；完整回读生成的 E57，比较七个字段的解码 SHA-256，核对串行与并行内容相同，随后删除样本点云。FARO `.fls` 文件或扫描包同样可用。
