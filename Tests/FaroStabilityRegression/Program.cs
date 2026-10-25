using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;
using EasyTry.Conversion.UI;
using EasyTry.Conversion.Writers;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 7)
        {
            Console.Error.WriteLine("Usage: report-directory output-directory scan1.fls scan2.fls scan3.fls scan4.fls known-invalid.fls");
            return 64;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        using var context = new ApplicationContext();
        using var mainForm = (Form)Activator.CreateInstance(typeof(PointCloudConversionForm).Assembly.GetType("EasyTry.Form1")!)!;
        PointCloudConversionEntry.Attach(mainForm);
        using var converter = new PointCloudConversionForm();
        _ = mainForm.Handle;
        _ = converter.Handle;
        var exitCode = 1;
        converter.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Run(args, converter, mainForm);
                exitCode = 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); }
            finally { context.ExitThread(); }
        }));
        Application.Run(context);
        return exitCode;
    }

    private static async Task Run(string[] args, PointCloudConversionForm form, Form originalWindow)
    {
        var reportRoot = Path.GetFullPath(args[0]);
        var outputRoot = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(reportRoot);
        Directory.CreateDirectory(outputRoot);
        var inputs = args.Skip(2).Take(4).Select(Path.GetFullPath).ToArray();
        var invalid = Path.GetFullPath(args[6]);
        var before = await Task.Run(() => Inventory(inputs.Append(invalid)));
        File.WriteAllText(Path.Combine(reportRoot, "inputs_before.json"), JsonSerializer.Serialize(before, Json));
        var plans = new List<Plan>();
        for (var round = 0; round < 2; round++)
        {
            for (var index = 0; index < inputs.Length; index++)
                plans.Add(new Plan(inputs[index], round, round == 0 ? E57OutputMode.SingleFile : E57OutputMode.SegmentedDataset, false));
        }
        plans.Insert(2, new Plan(invalid, 0, E57OutputMode.SingleFile, true));
        var options = plans.Select((plan, index) => new ConversionOptions
        {
            InputFilePath = plan.Input, OutputFilePath = Path.Combine(outputRoot, $"job-{index:D2}.e57"),
            Vendor = ScannerVendor.Faro, OutputMode = plan.Mode, SegmentScanColumns = 1000
        }).ToArray();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var service = (IPointCloudConversionService)form.GetType().GetField("conversionService", flags)!.GetValue(form)!;
        var update = form.GetType().GetMethod("UpdateProgress", flags)!;
        var statuses = Enumerable.Repeat("pending", plans.Count).ToArray();
        var results = new List<Result>();
        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var watch = Stopwatch.StartNew();
        var heartbeatCount = 0;
        using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            heartbeatCount++;
            if (heartbeatCount % 10 == 0)
                Console.WriteLine($"GUI ALIVE elapsed={watch.Elapsed.TotalSeconds:F1}s; " + string.Join("; ", statuses.Select((stage, i) => $"{i}:{stage}")));
        };
        timer.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        Console.WriteLine($"BEGIN full-file stability: {plans.Count} tasks, 4 workers, original Form1 + conversion form, no scan-line limit; pid={Environment.ProcessId}");
        await new BatchConversionScheduler().RunAsync(options, 4, async (index, item, token) =>
        {
            if (!DiskSpaceGuard.HasSafeFreeSpace(item.OutputFilePath, out _)) throw new IOException("Test output disk is below the production safety reserve.");
            var started = watch.Elapsed.TotalSeconds;
            var returnedReport = false;
            statuses[index] = "starting";
            try
            {
                var progressCount = 0;
                var progress = new Progress<ConversionProgress>(value =>
                {
                    if (form.IsDisposed) return;
                    progressCount++;
                    statuses[index] = value.ProcessedScanLines == 0 ? "opening" : $"lines={value.ProcessedScanLines}/{value.TotalScanLines}";
                    update.Invoke(form, new object[] { value });
                });
                var report = await service.ConvertAsync(item, progress, token);
                returnedReport = true;
                statuses[index] = "test readback";
                var fingerprint = await Task.Run(() => FullReadback.Check(item, report));
                if (sourceHashes.TryGetValue(item.InputFilePath, out var expected) && expected != fingerprint)
                    throw new Exception("Single and segmented full-file outputs differ in decoded point fields.");
                sourceHashes[item.InputFilePath] = fingerprint;
                if (plans[index].ExpectFailure) throw new Exception("The known invalid scan unexpectedly succeeded.");
                if (form.IsDisposed || originalWindow.IsDisposed) throw new Exception("The GUI was disposed during conversion.");
                results.Add(new(index, item.InputFilePath, plans[index].Round, item.OutputMode.ToString(), true,
                    watch.Elapsed.TotalSeconds - started, report.ProcessedScanLines, report.ValidPointCount,
                    report.InvalidPointCount, progressCount, fingerprint, null));
                statuses[index] = "complete";
                Console.WriteLine($"PASS job={index}; fullLines={report.ProcessedScanLines}; points={report.ValidPointCount}; seconds={watch.Elapsed.TotalSeconds - started:F2}");
            }
            catch (Exception error) when (plans[index].ExpectFailure && !returnedReport && error is not OperationCanceledException)
            {
                results.Add(new(index, item.InputFilePath, plans[index].Round, item.OutputMode.ToString(), false,
                    watch.Elapsed.TotalSeconds - started, 0, 0, 0, 0, null, error.Message));
                statuses[index] = "expected failure";
                Console.WriteLine($"EXPECTED FAILURE job={index}; parent remains alive: {error.Message}");
            }
            File.WriteAllText(Path.Combine(reportRoot, "results.json"), JsonSerializer.Serialize(results.OrderBy(result => result.Index), Json));
        }, cancellation.Token);
        timer.Stop();
        if (results.Count(result => result.Success) != 8 || results.Count(result => !result.Success) != 1 || heartbeatCount < 2)
            throw new Exception("Full-file stability case counts or GUI heartbeat do not match.");
        var after = await Task.Run(() => Inventory(inputs.Append(invalid)));
        File.WriteAllText(Path.Combine(reportRoot, "inputs_after.json"), JsonSerializer.Serialize(after, Json));
        if (before.Length != after.Length || before.Zip(after).Any(pair => pair.First.Path != pair.Second.Path || pair.First.Length != pair.Second.Length || pair.First.Sha256 != pair.Second.Sha256))
            throw new Exception("Input file bytes changed.");
        File.WriteAllText(Path.Combine(reportRoot, "summary.json"), JsonSerializer.Serialize(new
        {
            ParentPid = Environment.ProcessId, FullFileSuccesses = 8, ExpectedFailures = 1,
            GuiHeartbeatCount = heartbeatCount, WallSeconds = watch.Elapsed.TotalSeconds,
            TotalValidPoints = results.Sum(result => result.ValidPoints),
            BothFormsAlive = !form.IsDisposed && !originalWindow.IsDisposed, InputHashesUnchanged = true,
            SourceFieldHashesMatchedAcrossOutputModes = true
        }, Json));
        var cleanup = options.SelectMany(option => option.OutputMode == E57OutputMode.SingleFile
            ? File.Exists(option.OutputFilePath) ? new[] { option.OutputFilePath } : Array.Empty<string>()
            : Directory.Exists(ConversionOutputLayout.GetDatasetDirectory(option.OutputFilePath))
                ? Directory.GetFiles(ConversionOutputLayout.GetDatasetDirectory(option.OutputFilePath), "*.e57", SearchOption.AllDirectories)
                : Array.Empty<string>()).ToArray();
        File.WriteAllText(Path.Combine(reportRoot, "outputs_to_delete.json"), JsonSerializer.Serialize(cleanup, Json));
        Console.WriteLine($"PASS: 8 full-file conversions, 1 expected failure, {heartbeatCount} GUI heartbeats; both windows remain alive; source bytes unchanged.");
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static InputState[] Inventory(IEnumerable<string> inputs) => inputs
        .SelectMany(input => Directory.Exists(input) ? Directory.GetFiles(input, "*", SearchOption.AllDirectories) : new[] { input })
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .Select(path => { using var file = File.OpenRead(path); return new InputState(path, file.Length, Convert.ToHexString(SHA256.HashData(file))); }).ToArray();
    private sealed record InputState(string Path, long Length, string Sha256);
    private sealed record Plan(string Input, int Round, E57OutputMode Mode, bool ExpectFailure);
    private sealed record Result(int Index, string Input, int Round, string Mode, bool Success,
        double Seconds, long Lines, long ValidPoints, long InvalidPoints, int ProgressEvents, string? DecodedSha256, string? Error);
}

internal static class FullReadback
{
    private static readonly object Gate = new();
    internal static string Check(ConversionOptions options, ConversionReport report)
    {
        lock (Gate)
        {
            var paths = report.ResolvedOutputMode == E57OutputMode.SingleFile
                ? new[] { options.OutputFilePath }
                : Directory.GetFiles(Path.Combine(ConversionOutputLayout.GetDatasetDirectory(options.OutputFilePath), "parts"), "*.e57").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            var hashes = Enumerable.Range(0, 7).Select(_ => IncrementalHash.CreateHash(HashAlgorithmName.SHA256)).ToArray();
            ulong total = 0;
            try
            {
                for (var part = 0; part < paths.Length; part++)
                {
                    var validation = NativeE57Writer.ValidateFile(paths[part], true);
                    if (!validation.HasAllRequiredFields) throw new Exception("Missing E57 fields.");
                    total += Append(paths[part], report.ResolvedOutputMode == E57OutputMode.SingleFile ? 0u : (uint)(part * options.SegmentScanColumns), hashes);
                }
                if (total != (ulong)report.ValidPointCount) throw new Exception("Full readback point count mismatch.");
                using var combined = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var hash in hashes) combined.AppendData(hash.GetHashAndReset());
                var result = Convert.ToHexString(combined.GetHashAndReset());
                return result;
            }
            finally { foreach (var hash in hashes) hash.Dispose(); }
        }
    }

    private static ulong Append(string path, uint offset, IncrementalHash[] hashes)
    {
        const int capacity = 65536;
        var handle = Open(path, capacity, out _, out _, out var total, out var fields);
        if (handle == IntPtr.Zero || fields != 0x7f) throw new Exception("Cannot open E57 readback.");
        var x = new double[capacity]; var y = new double[capacity]; var z = new double[capacity];
        var intensity = new float[capacity]; var raw = new double[capacity]; var rows = new uint[capacity]; var columns = new uint[capacity];
        ulong seen = 0;
        try
        {
            while (true)
            {
                if (Read(handle, x, y, z, intensity, raw, rows, columns, capacity, out var count) == 0) throw new Exception("E57 decode failed.");
                if (count == 0) break;
                var n = (int)count;
                for (var index = 0; index < n; index++) columns[index] = checked(columns[index] + offset);
                hashes[0].AppendData(MemoryMarshal.AsBytes(x.AsSpan(0, n))); hashes[1].AppendData(MemoryMarshal.AsBytes(y.AsSpan(0, n))); hashes[2].AppendData(MemoryMarshal.AsBytes(z.AsSpan(0, n)));
                hashes[3].AppendData(MemoryMarshal.AsBytes(intensity.AsSpan(0, n))); hashes[4].AppendData(MemoryMarshal.AsBytes(raw.AsSpan(0, n)));
                hashes[5].AppendData(MemoryMarshal.AsBytes(rows.AsSpan(0, n))); hashes[6].AppendData(MemoryMarshal.AsBytes(columns.AsSpan(0, n)));
                seen += (ulong)count;
            }
            if (seen != total) throw new Exception("E57 reader returned incomplete data.");
            return total;
        }
        finally { Close(handle); }
    }

    [DllImport("EasyTryE57Readback.dll", EntryPoint = "easytry_e57_reader_open", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nuint capacity, out uint rows, out uint columns, out ulong count, out uint fields);
    [DllImport("EasyTryE57Readback.dll", EntryPoint = "easytry_e57_reader_read", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Read(IntPtr handle, [Out] double[] x, [Out] double[] y, [Out] double[] z, [Out] float[] intensity, [Out] double[] raw, [Out] uint[] rows, [Out] uint[] columns, nuint capacity, out nuint count);
    [DllImport("EasyTryE57Readback.dll", EntryPoint = "easytry_e57_reader_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Close(IntPtr handle);
}
