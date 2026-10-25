using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;
using EasyTry.Conversion.Writers;

internal static class WorkerIsolationChecks
{
    internal static int RunFixture(string scenario) => ConversionWorkerHost.Run((options, progress, token) =>
    {
        var directory = Path.GetDirectoryName(options.OutputFilePath)!;
        Directory.CreateDirectory(directory);
        if (scenario is "crash" or "hang" or "cancel")
        {
            File.WriteAllText(Path.Combine(directory, $".{Path.GetFileName(options.OutputFilePath)}.{Guid.NewGuid():N}.writing"), "unfinished");
            progress.Report(new ConversionProgress(1, "fixture ready"));
            if (scenario == "crash") Environment.Exit(unchecked((int)0xc0000374));
            if (scenario == "hang") Thread.Sleep(Timeout.Infinite);
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
        }
        if (scenario == "error") throw new InvalidOperationException("fixture conversion failed");
        if (scenario == "nan")
        {
            var empty = new NumericRange(double.NaN, double.NaN);
            return new ConversionReport(new(options.InputFilePath, ScannerVendor.Faro, 1, 1), 1, 0, 1,
                empty, empty, empty, empty, empty, TimeSpan.Zero);
        }
        if (scenario == "no-result") Environment.Exit(0);
        var service = new PointCloudConversionService(_ => new FixtureSource(), () => new NativeE57Writer());
        return service.ConvertAsync(options, progress, token).GetAwaiter().GetResult();
    });

    internal static async Task<int> Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        ConversionOptions Options(int index, string name) => new()
        {
            InputFilePath = Path.Combine(root, $"source-{index}.fls"),
            OutputFilePath = Path.Combine(root, "子进程验证", name + ".e57"), Vendor = ScannerVendor.Faro
        };
        ConversionWorkerClient Client(string scenario) => new(() => StartInfo(scenario), TimeSpan.FromMilliseconds(250));
        try
        {
            await new PointCloudConversionService().ConvertAsync(new ConversionOptions
            {
                InputFilePath = Path.Combine(root, "unsupported.invalid"),
                OutputFilePath = Path.Combine(root, "unsupported.e57")
            }, null, CancellationToken.None);
            throw new Exception("Unsupported input was accepted by the production worker.");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("无法根据输入文件识别扫描仪厂商"),
                "The real WinExe worker must exchange a JSON error over redirected pipes without a console.");
        }
        var progressValues = new List<ConversionProgress>();
        var goodOptions = Options(0, "正常输出");
        var report = await Client("success").ConvertAsync(goodOptions, new ImmediateProgress(progressValues.Add), CancellationToken.None);
        Check(report.ValidPointCount == 1040 && report.Metadata.Vendor == ScannerVendor.Faro && File.Exists(report.MetadataDocumentPath),
            "Worker report and Unicode output paths must round-trip.");
        Check(progressValues.Any(value => value.ProcessedScanLines > 0) && progressValues[^1].Percent == 100,
            "The GUI process must receive live progress through the worker pipe.");
        NativeReadback.CheckPoints(goodOptions.OutputFilePath, 130, 8, (column, row) => FixtureSource.Point(0, column, row));
        checks++;
        File.Delete(goodOptions.OutputFilePath);
        var nan = await Client("nan").ConvertAsync(Options(0, "空范围"), null, CancellationToken.None);
        Check(!nan.XRange.HasValue && nan.InvalidPointCount == 1, "Empty numeric ranges must survive JSON transport.");

        var sourceA = Path.Combine(root, "source-0.fls");
        var sourceB = Path.Combine(root, "source-1.fls");
        File.WriteAllText(sourceA, "fixture");
        File.WriteAllText(sourceB, "fixture");
        var continuous = new ConversionOptions
        {
            InputFilePath = sourceA, ContinuousInputPaths = [sourceA, sourceB],
            OutputFilePath = Path.Combine(root, "子进程验证", "连续分段.e57"),
            Vendor = ScannerVendor.Faro, OutputMode = E57OutputMode.AutoByScanColumnThreshold,
            SegmentScanColumns = 65
        };
        var continuousReport = await Client("success").ConvertAsync(continuous, null, CancellationToken.None);
        Check(continuousReport.Metadata.SourceParts?.Count == 2 && continuousReport.ProcessedScanLines == 260 &&
              continuousReport.ResolvedOutputMode == E57OutputMode.SegmentedDataset && File.Exists(continuousReport.MetadataDocumentPath),
            "Continuous source metadata and auto-segmented output must round-trip across the worker boundary.");
        var parts = Directory.GetFiles(ConversionOutputLayout.GetDatasetDirectory(continuous.OutputFilePath), "*.e57", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
        Check(parts.Length == 4, "A continuous worker must produce every expected segment.");
        for (var part = 0; part < parts.Length; part++)
        {
            var start = part * 65;
            NativeReadback.CheckPoints(parts[part], 65, 8, (column, row) =>
            {
                var global = start + column;
                return FixtureSource.Point(global / 130, global % 130, row) with { Y = global, ColumnIndex = (uint)column };
            });
            File.Delete(parts[part]);
        }
        checks++;

        var crashOptions = Options(1, "保留旧输出");
        Directory.CreateDirectory(Path.GetDirectoryName(crashOptions.OutputFilePath)!);
        File.WriteAllText(crashOptions.OutputFilePath, "previous completed output");
        var oldTemporary = Path.Combine(Path.GetDirectoryName(crashOptions.OutputFilePath)!, $".{Path.GetFileName(crashOptions.OutputFilePath)}.older.writing");
        File.WriteAllText(oldTemporary, "previous incomplete output");
        try { await Client("crash").ConvertAsync(crashOptions, null, CancellationToken.None); throw new Exception("Worker crash was ignored."); }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("C0000374"), "Native process termination must be reported as one task's failure.");
        }
        Check(File.ReadAllText(crashOptions.OutputFilePath) == "previous completed output" && File.ReadAllText(oldTemporary) == "previous incomplete output",
            "A worker crash must not delete a completed output or earlier temporary file.");
        Check(Directory.GetFiles(Path.GetDirectoryName(crashOptions.OutputFilePath)!, $".{Path.GetFileName(crashOptions.OutputFilePath)}.*.writing").SequenceEqual(new[] { oldTemporary }),
            "Only the crashed worker's new temporary files may be removed.");
        File.Delete(crashOptions.OutputFilePath);
        File.Delete(oldTemporary);
        try { await Client("error").ConvertAsync(Options(0, "普通错误"), null, CancellationToken.None); throw new Exception("Worker conversion error was ignored."); }
        catch (InvalidOperationException error) { Check(error.Message.Contains("fixture conversion failed"), "Normal worker errors must retain their diagnostic."); }
        try { await Client("no-result").ConvertAsync(Options(0, "缺少结果"), null, CancellationToken.None); throw new Exception("Missing report was accepted."); }
        catch (InvalidOperationException error) { Check(error.Message.Contains("未收到完整转换结果"), "Exit code zero without a final report must not mark a task successful."); }

        foreach (var scenario in new[] { "cancel", "hang" })
        {
            using var cancellation = new CancellationTokenSource();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var options = Options(0, scenario);
            var task = Client(scenario).ConvertAsync(options,
                new ImmediateProgress(_ => ready.TrySetResult()), cancellation.Token);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            try { await task.WaitAsync(TimeSpan.FromSeconds(10)); throw new Exception("Worker cancellation was ignored."); }
            catch (OperationCanceledException) { checks++; }
            Check(!Directory.GetFiles(Path.GetDirectoryName(options.OutputFilePath)!, $".{Path.GetFileName(options.OutputFilePath)}.*.writing").Any(),
                "Cooperative and unresponsive workers must finish cleanup before cancellation returns.");
        }

        var optionsBatch = Enumerable.Range(0, 8).Select(index => Options(index, $"mixed-{index}")).ToArray();
        var success = 0; var failures = 0;
        await new BatchConversionScheduler().RunAsync(optionsBatch, 4, async (index, options, token) =>
        {
            try
            {
                var scenario = index == 0 ? "crash" : index == 1 ? "error" : "success";
                await Client(scenario).ConvertAsync(options, null, token);
                Interlocked.Increment(ref success);
            }
            catch (InvalidOperationException) { Interlocked.Increment(ref failures); }
        }, CancellationToken.None);
        Check(success == 6 && failures == 2, "A native worker termination must leave the parent and independent tasks running.");
        for (var index = 2; index < optionsBatch.Length; index++)
        {
            var id = index;
            NativeReadback.CheckPoints(optionsBatch[index].OutputFilePath, 130, 8, (column, row) => FixtureSource.Point(id, column, row));
            File.Delete(optionsBatch[index].OutputFilePath);
        }
        checks++;

        using (var process = new Process { StartInfo = PreparedStartInfo("hang") })
        {
            var lifetime = new WorkerProcessLifetime();
            process.Start();
            lifetime.Attach(process);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(Options(0, "parent-exit"), ConversionWorkerProtocol.Json));
            await process.StandardInput.FlushAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(line?.StartsWith(ConversionWorkerProtocol.Prefix) == true, "Parent-exit fixture must have started its worker.");
            lifetime.Dispose();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(process.HasExited && !File.Exists(Options(0, "parent-exit").OutputFilePath),
                "Closing the parent's job handle must terminate an unresponsive worker before it produces a result.");
            foreach (var temporary in Directory.GetFiles(Path.GetDirectoryName(Options(0, "parent-exit").OutputFilePath)!, ".parent-exit.e57.*.writing"))
                File.Delete(temporary);
        }
        using var parent = Process.GetCurrentProcess();
        Check(!parent.Modules.Cast<ProcessModule>().Any(module => module.ModuleName.Equals("iQOpen.dll", StringComparison.OrdinalIgnoreCase) || module.ModuleName.Equals("iQLib.dll", StringComparison.OrdinalIgnoreCase)),
            "The parent process must not load the FARO native SDK while managing worker conversions.");
        return checks;
    }

    private static ProcessStartInfo StartInfo(string scenario)
    {
        var assembly = Assembly.GetExecutingAssembly().Location;
        var start = new ProcessStartInfo(Path.ChangeExtension(assembly, ".exe"));
        start.ArgumentList.Add("--worker-fixture");
        start.ArgumentList.Add(scenario);
        return start;
    }

    private static ProcessStartInfo PreparedStartInfo(string scenario)
    {
        var start = StartInfo(scenario);
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        return start;
    }

    private sealed class ImmediateProgress(Action<ConversionProgress> report) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => report(value);
    }
}
