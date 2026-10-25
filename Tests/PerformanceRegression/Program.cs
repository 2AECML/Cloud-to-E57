using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;
using EasyTry.Conversion.Sources;
using EasyTry.Conversion.Writers;

var root = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
if (args.Length >= 4 && args[0] == "--sdk-smoke")
{
    await SdkSmoke.Run(root, Path.GetFullPath(args[1]), args.Skip(2).ToArray());
    return;
}
var result = WriterChecks.Benchmark(root);
Console.WriteLine(JsonSerializer.Serialize(result));
if (args.Length == 2 && args[0] == "--writer-benchmark")
{
    File.WriteAllText(Path.GetFullPath(args[1]), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    return;
}
if (result.WriterAllocatedBytes > 4 * 1024 * 1024) throw new Exception("Writer allocations grow with batch count instead of reusing buffers.");
var checks = await SchedulerChecks.Run(root);
checks += await ThreadAffinityChecks.Run(root);
checks += await WriterChecks.ConcurrentConversions(root);
checks += WriterChecks.CancellationAndCapacity(root);
Console.WriteLine($"PASS: {checks + 2} performance regression checks; every decoded field of {result.Points:N0} benchmark points matched. Only generated fixtures were converted.");

static class WriterChecks
{
    internal static WriterBenchmarkResult Benchmark(string root)
    {
        const int rows = 512, batches = 64;
        static int Lines(int batch) => batch == batches - 1 ? 3 : batch % 4 == 1 ? 17 : 64;
        var columns = Enumerable.Range(0, batches).Sum(Lines);
        var path = Path.Combine(root, "writer-benchmark.e57");
        var options = new ConversionOptions { InputFilePath = Path.Combine(root, "fixture.zfs"), OutputFilePath = path, Vendor = ScannerVendor.ZPlusF };
        var metadata = new ScanMetadata(options.InputFilePath, options.Vendor, columns, rows);
        using var writer = new NativeE57Writer();
        writer.Open(metadata, options);
        long allocated = 0, ticks = 0, valid = 0;
        var first = 0;
        var gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        for (var index = 0; index < batches; index++)
        {
            var lineCount = Lines(index);
            var points = new List<UnifiedScanPoint>(rows * lineCount);
            for (var column = first; column < first + lineCount; column++)
                for (var row = 0; row < rows; row++) points.Add(Point(column, row));
            using var batch = new PointBatch((uint)first, lineCount, points.Count, 0, points);
            var startAllocation = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            writer.WriteBatch(batch, CancellationToken.None);
            ticks += Stopwatch.GetTimestamp() - start;
            allocated += GC.GetAllocatedBytesForCurrentThread() - startAllocation;
            valid += batch.ValidPointCount;
            first += lineCount;
        }
        var range = new NumericRange(0, columns);
        writer.Complete(new ConversionReport(metadata, columns, valid, 0, range, range, range, range, range, TimeSpan.Zero));
        var gcCounts = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcBefore[i]).ToArray();
        var validation = NativeE57Writer.ValidateFile(path, true);
        if (!validation.HasAllRequiredFields || validation.PointCount != (ulong)valid) throw new Exception("Writer full readback validation failed.");
        NativeReadback.CheckPoints(path, columns, rows, Point);
        var outputBytes = new FileInfo(path).Length;
        File.Delete(path);
        return new(batches, rows, columns, valid, ticks / (double)Stopwatch.Frequency, allocated, gcCounts, outputBytes, true);
    }

    internal static UnifiedScanPoint Point(int column, int row) => new()
    {
        X = (column + 1) * 0.125 + row * 0.00001, Y = column, Z = row * 0.01 + 1,
        RawIntensity = column * 1000.0 + row,
        ProcessedIntensity = row == 0 ? -1 : row == 1 ? 2 : (row % 256) / 255f,
        RowIndex = (uint)row, ColumnIndex = (uint)column
    };

    internal static async Task<int> ConcurrentConversions(string root)
    {
        var sources = new ConcurrentBag<FixtureSource>();
        var service = new PointCloudConversionService(_ =>
        {
            var source = new FixtureSource();
            sources.Add(source);
            return source;
        }, () => new NativeE57Writer());
        var checks = 0;
        foreach (var concurrency in new[] { 2, 4 })
        {
            var options = Enumerable.Range(0, 4).Select(index => new ConversionOptions
            {
                InputFilePath = Path.Combine(root, $"source-{index}.zfs"),
                OutputFilePath = Path.Combine(root, $"concurrent-{concurrency}-{index}.e57"), Vendor = ScannerVendor.ZPlusF,
                OutputMode = index % 2 == 0 ? E57OutputMode.SingleFile : E57OutputMode.SegmentedDataset,
                SegmentScanColumns = 65
            }).ToArray();
            var reports = new ConversionReport?[options.Length];
            await new BatchConversionScheduler().RunAsync(options, concurrency, async (index, item, token) =>
            {
                reports[index] = await service.ConvertAsync(item, null, token);
            }, CancellationToken.None);
            for (var index = 0; index < options.Length; index++)
            {
                var report = reports[index] ?? throw new Exception("Missing parallel conversion report.");
                if (report.ValidPointCount != 130 * 8 || report.InvalidPointCount != 0 || report.ProcessedScanLines != 130 || !File.Exists(report.MetadataDocumentPath))
                    throw new Exception("Parallel conversion statistics or metadata document mismatch.");
                checks++;
                var partPaths = options[index].OutputMode == E57OutputMode.SingleFile
                    ? new[] { options[index].OutputFilePath }
                    : Directory.GetFiles(ConversionOutputLayout.GetDatasetDirectory(options[index].OutputFilePath), "*.e57", SearchOption.AllDirectories).OrderBy(path => path).ToArray();
                for (var part = 0; part < partPaths.Length; part++)
                {
                    var offset = part * 65;
                    var job = index;
                    NativeReadback.CheckPoints(partPaths[part], partPaths.Length == 1 ? 130 : 65, 8,
                        (column, row) => FixtureSource.Point(job, offset + column, row) with { ColumnIndex = (uint)column });
                    checks++;
                    File.Delete(partPaths[part]);
                }
            }
        }
        if (sources.Count != 8 || sources.Any(source => !source.Disposed)) throw new Exception("Parallel tasks did not dispose their independent source instances.");
        return checks + 1;
    }

    internal static int CancellationAndCapacity(string root)
    {
        var path = Path.Combine(root, "cancelled.e57");
        var options = new ConversionOptions { InputFilePath = Path.Combine(root, "cancel.zfs"), OutputFilePath = path };
        using (var writer = new NativeE57Writer())
        {
            writer.Open(new ScanMetadata(options.InputFilePath, ScannerVendor.ZPlusF, 128, 1), options);
            using var batch = new PointBatch(0, 64, 64, 0, Enumerable.Range(0, 64).Select(column => Point(column, 0)).ToList());
            writer.WriteBatch(batch, CancellationToken.None);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { writer.WriteBatch(batch, cancelled.Token); throw new Exception("Cancelled write was accepted."); }
            catch (OperationCanceledException) { }
        }
        if (File.Exists(path) || Directory.GetFiles(root, "*.writing").Length != 0) throw new Exception("Cancelled writer left output or temporary files behind.");
        using (var writer = new NativeE57Writer())
        {
            writer.Open(new ScanMetadata(options.InputFilePath, ScannerVendor.ZPlusF, 65, 1), options);
            using var empty = new PointBatch(0, 0, 0, 0, []);
            writer.WriteBatch(empty, CancellationToken.None);
            using var oversized = new PointBatch(0, 65, 65, 0, Enumerable.Range(0, 65).Select(column => Point(column, 0)).ToList());
            try { writer.WriteBatch(oversized, CancellationToken.None); throw new Exception("Oversized batch was accepted."); }
            catch (ArgumentException) { }
        }
        if (File.Exists(path) || Directory.GetFiles(root, "*.writing").Length != 0) throw new Exception("Failed writer left output or temporary files behind.");
        return 4;
    }
}

sealed record WriterBenchmarkResult(int Batches, int Rows, int Columns, long Points, double WriteSeconds, long WriterAllocatedBytes, int[] GcCollections, long OutputBytes, bool AllDecodedFieldsMatched);

static class ThreadAffinityChecks
{
    internal static async Task<int> Run(string root)
    {
        var checks = 0;
        foreach (var vendor in new[] { ScannerVendor.Faro, ScannerVendor.Auto, ScannerVendor.ZPlusF })
        {
            var calls = new List<(string Stage, int Thread, ApartmentState Apartment, bool Pool)>();
            void Record(string stage) => calls.Add((stage, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState(), Thread.CurrentThread.IsThreadPoolThread));
            var service = new PointCloudConversionService(_ => new AffinitySource(Record), () => new AffinityWriter(Record));
            await service.ConvertAsync(new ConversionOptions
            {
                InputFilePath = Path.Combine(root, vendor + (vendor == ScannerVendor.ZPlusF ? ".zfs" : ".fls")),
                OutputFilePath = Path.Combine(root, $"affinity-{vendor}.e57"), Vendor = vendor
            }, null, CancellationToken.None);
            if (!calls.Select(call => call.Stage).SequenceEqual(new[] { "source-open", "writer-open", "read", "write", "complete", "writer-dispose", "source-dispose" }))
                throw new Exception("Source and writer lifecycle was not completed in order.");
            checks++;
            var expectedApartment = vendor == ScannerVendor.ZPlusF ? ApartmentState.MTA : ApartmentState.STA;
            if (calls.Any(call => call.Apartment != expectedApartment) || calls.Select(call => call.Thread).Distinct().Count() != 1)
                throw new Exception("SDK construction, reads and disposal must stay on one thread with the correct apartment.");
            checks++;
            if (calls.Any(call => call.Pool != (vendor == ScannerVendor.ZPlusF))) throw new Exception("FARO must use a dedicated thread; ZFS must retain its pool worker.");
            checks++;
        }
        var created = false;
        var cancelledService = new PointCloudConversionService(_ => { created = true; throw new Exception("Should not construct a source."); }, () => throw new Exception());
        var cancellationOptions = new ConversionOptions { InputFilePath = Path.Combine(root, "cancel-affinity.fls"), OutputFilePath = Path.Combine(root, "cancel-affinity.e57"), Vendor = ScannerVendor.Faro };
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var task = cancelledService.ConvertAsync(cancellationOptions, null, cancelled.Token);
            try { await task; throw new Exception("Pre-cancellation was ignored."); }
            catch (OperationCanceledException) { if (created || !task.IsCanceled) throw new Exception("Pre-cancelled conversions must not create SDK objects."); checks++; }
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new ConcurrentBag<string>();
        using (var cancellation = new CancellationTokenSource())
        {
            var service = new PointCloudConversionService(_ => new AffinitySource(disposed.Add, token =>
            {
                entered.TrySetResult();
                token.WaitHandle.WaitOne();
                token.ThrowIfCancellationRequested();
            }), () => new AffinityWriter(disposed.Add));
            var task = service.ConvertAsync(cancellationOptions, null, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try { await task; throw new Exception("In-flight cancellation was ignored."); }
            catch (OperationCanceledException)
            {
                if (!task.IsCanceled || !disposed.Contains("source-dispose") || !disposed.Contains("writer-dispose"))
                    throw new Exception("Cancellation returned before source and writer cleanup.");
                checks++;
            }
        }
        var failure = new InvalidOperationException("STA failure fixture");
        var failing = new PointCloudConversionService(_ => throw failure, () => throw new Exception());
        try { await failing.ConvertAsync(cancellationOptions, null, CancellationToken.None); throw new Exception("STA failure was swallowed."); }
        catch (InvalidOperationException error) { if (!ReferenceEquals(error, failure)) throw new Exception("STA failure type/identity changed."); checks++; }
        return checks;
    }

    private sealed class AffinitySource(Action<string> record, Action<CancellationToken>? beforeRead = null) : IScanSource
    {
        public ScanMetadata Metadata { get; private set; } = new("", ScannerVendor.Faro, 1, 1);
        public void Open(ConversionOptions options, CancellationToken token) { record("source-open"); Metadata = Metadata with { SourceFilePath = options.InputFilePath }; }
        public IEnumerable<PointBatch> ReadBatches(int size, CancellationToken token)
        {
            record("read"); beforeRead?.Invoke(token);
            yield return new PointBatch(0, 1, 1, 0, [WriterChecks.Point(0, 0)]);
        }
        public void Dispose() => record("source-dispose");
    }
    private sealed class AffinityWriter(Action<string> record) : IPointCloudWriter
    {
        public void Open(ScanMetadata metadata, ConversionOptions options) => record("writer-open");
        public void WriteBatch(PointBatch batch, CancellationToken token) => record("write");
        public void Complete(ConversionReport report) => record("complete");
        public void Dispose() => record("writer-dispose");
    }
}

sealed class FixtureSource : IScanSource
{
    private int job;
    public bool Disposed { get; private set; }
    public ScanMetadata Metadata { get; private set; } = new("", ScannerVendor.ZPlusF, 130, 8);
    public void Open(ConversionOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        job = int.Parse(Path.GetFileNameWithoutExtension(options.InputFilePath).Split('-')[1]);
        Metadata = Metadata with { SourceFilePath = options.InputFilePath };
    }
    public IEnumerable<PointBatch> ReadBatches(int scanLinesPerBatch, CancellationToken cancellationToken)
    {
        for (var column = 0; column < 130; column += scanLinesPerBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = Math.Min(scanLinesPerBatch, 130 - column);
            var points = Enumerable.Range(column, lines).SelectMany(c => Enumerable.Range(0, 8).Select(row => Point(job, c, row))).ToList();
            yield return new PointBatch((uint)column, lines, points.Count, 0, points);
        }
    }
    public static UnifiedScanPoint Point(int job, int column, int row) => WriterChecks.Point(column, row) with { X = 10000 * job + column + row * 0.01, RawIntensity = job * 100000 + column * 8 + row };
    public void Dispose() => Disposed = true;
}

static class SchedulerChecks
{
    internal static async Task<int> Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var options = Enumerable.Range(0, 8).Select(index => new ConversionOptions
        {
            InputFilePath = Path.Combine(root, $"scheduler-{index}.fls"), OutputFilePath = Path.Combine(root, $"scheduler-{index}.e57")
        }).ToArray();
        var scheduler = new BatchConversionScheduler();
        foreach (var limit in new[] { 1, 2, 4 })
        {
            using var gate = new SemaphoreSlim(0);
            var firstWave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = 0; var active = 0; var peak = 0;
            var visits = new int[options.Length];
            var run = scheduler.RunAsync(options, limit, async (index, _, token) =>
            {
                Interlocked.Increment(ref visits[index]);
                var current = Interlocked.Increment(ref active);
                int previous;
                do { previous = peak; } while (current > previous && Interlocked.CompareExchange(ref peak, current, previous) != previous);
                if (Interlocked.Increment(ref started) == limit) firstWave.TrySetResult();
                try { await gate.WaitAsync(token); }
                finally { Interlocked.Decrement(ref active); }
            }, CancellationToken.None);
            await firstWave.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(started == limit && active == limit, "Scheduler started more than the configured number of tasks.");
            gate.Release(options.Length);
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            Check(peak == limit && active == 0 && visits.All(count => count == 1), "Tasks must execute exactly once and obey the concurrency limit.");
        }

        var shared = options[0].InputFilePath;
        var conflicts = new[]
        {
            new ConversionOptions { InputFilePath = shared, ContinuousInputPaths = [shared, options[1].InputFilePath], OutputFilePath = options[0].OutputFilePath },
            options[1], options[2]
        };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedIndices = new ConcurrentQueue<int>();
        var conflictRun = scheduler.RunAsync(conflicts, 2, async (index, _, token) =>
        {
            startedIndices.Enqueue(index);
            await release.Task.WaitAsync(token);
        }, CancellationToken.None);
        Check(startedIndices.ToArray().SequenceEqual(new[] { 0, 2 }), "Overlapping continuous inputs must serialize while independent inputs may proceed.");
        release.SetResult();
        await conflictRun;
        Check(startedIndices.ToArray().SequenceEqual(new[] { 0, 2, 1 }), "A blocked input must execute after its dependency completes.");

        foreach (var limit in new[] { 0, 5 })
        {
            try { await scheduler.RunAsync(options, limit, (_, _, _) => Task.CompletedTask, CancellationToken.None); throw new Exception("Invalid concurrency was accepted."); }
            catch (ArgumentOutOfRangeException) { checks++; }
        }

        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            var started = false;
            try { await scheduler.RunAsync(options, 2, (_, _, _) => { started = true; return Task.CompletedTask; }, cancel.Token); throw new Exception("Cancellation was ignored."); }
            catch (OperationCanceledException) { Check(!started, "Pre-cancelled runs must not start any conversion."); }
        }
        using (var cancel = new CancellationTokenSource())
        {
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = 0; var cleaning = 0; var disposed = 0;
            var run = scheduler.RunAsync(options, 2, async (_, _, token) =>
            {
                Interlocked.Increment(ref started);
                try { await Task.Delay(Timeout.Infinite, token); }
                finally
                {
                    if (Interlocked.Increment(ref cleaning) == 2) cancelled.TrySetResult();
                    await cleanup.Task;
                    Interlocked.Increment(ref disposed);
                }
            }, cancel.Token);
            Check(started == 2, "Cancellation fixture must start exactly two tasks.");
            cancel.Cancel();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!run.IsCompleted, "Scheduler returned before active writers had finished cleanup.");
            cleanup.SetResult();
            try { await run; throw new Exception("Cancellation did not propagate."); }
            catch (OperationCanceledException) { Check(started == 2 && disposed == 2, "Cancellation must clean every active writer without starting pending tasks."); }
        }
        var fatalGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fatalCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fatalObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fatalStarted = 0;
        var fatalRun = scheduler.RunAsync(options, 2, async (index, _, token) =>
        {
            Interlocked.Increment(ref fatalStarted);
            if (index == 0) { await fatalGate.Task; throw new InvalidOperationException("fixture fatal error"); }
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { fatalObserved.TrySetResult(); await fatalCleanup.Task; }
        }, CancellationToken.None);
        fatalGate.SetResult();
        await fatalObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!fatalRun.IsCompleted, "Failure must wait for other writers to clean up.");
        fatalCleanup.SetResult();
        try { await fatalRun; throw new Exception("Fatal scheduler failure was swallowed."); }
        catch (InvalidOperationException error) { Check(error.Message == "fixture fatal error" && fatalStarted == 2, "Original failure must propagate after stopping active tasks."); }
        return checks;
    }
}

static class NativeReadback
{
    [DllImport("EasyTryE57Readback.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr easytry_e57_reader_open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nuint capacity, out uint rows, out uint columns, out ulong count, out uint fields);
    [DllImport("EasyTryE57Readback.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int easytry_e57_reader_read(IntPtr handle, [Out] double[] x, [Out] double[] y, [Out] double[] z, [Out] float[] intensity, [Out] double[] raw, [Out] uint[] rows, [Out] uint[] columns, nuint capacity, out nuint read);
    [DllImport("EasyTryE57Readback.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void easytry_e57_reader_destroy(IntPtr handle);

    internal static string Fingerprint(string path)
    {
        const int capacity = 65536;
        var validation = NativeE57Writer.ValidateFile(path, true);
        var handle = easytry_e57_reader_open(path, capacity, out _, out _, out var count, out var fields);
        if (handle == IntPtr.Zero || fields != 0x7f || count != validation.PointCount) throw new Exception("E57 fingerprint metadata mismatch.");
        var x = new double[capacity]; var y = new double[capacity]; var z = new double[capacity];
        var intensity = new float[capacity]; var raw = new double[capacity]; var rows = new uint[capacity]; var columns = new uint[capacity];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ulong total = 0;
        try
        {
            while (true)
            {
                if (easytry_e57_reader_read(handle, x, y, z, intensity, raw, rows, columns, capacity, out var read) == 0) throw new Exception("E57 fingerprint read failed.");
                if (read == 0) break;
                var n = checked((int)read);
                hash.AppendData(MemoryMarshal.AsBytes(x.AsSpan(0, n))); hash.AppendData(MemoryMarshal.AsBytes(y.AsSpan(0, n))); hash.AppendData(MemoryMarshal.AsBytes(z.AsSpan(0, n)));
                hash.AppendData(MemoryMarshal.AsBytes(intensity.AsSpan(0, n))); hash.AppendData(MemoryMarshal.AsBytes(raw.AsSpan(0, n)));
                hash.AppendData(MemoryMarshal.AsBytes(rows.AsSpan(0, n))); hash.AppendData(MemoryMarshal.AsBytes(columns.AsSpan(0, n)));
                total += (ulong)read;
            }
            if (total != count || !validation.HasAllRequiredFields) throw new Exception("E57 fingerprint point count mismatch.");
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { easytry_e57_reader_destroy(handle); }
    }

    internal static void CheckPoints(string path, int columns, int rows, Func<int, int, UnifiedScanPoint> expectedPoint)
    {
        const int capacity = 65536;
        var handle = easytry_e57_reader_open(path, capacity, out var actualRows, out var actualColumns, out var count, out var fields);
        if (handle == IntPtr.Zero || actualRows != rows || actualColumns != columns || count != (ulong)rows * (ulong)columns || fields != 0x7f)
            throw new Exception("Native E57 metadata mismatch.");
        var x = new double[capacity]; var y = new double[capacity]; var z = new double[capacity];
        var intensity = new float[capacity]; var raw = new double[capacity]; var rowIndex = new uint[capacity]; var columnIndex = new uint[capacity];
        long index = 0;
        try
        {
            while (true)
            {
                if (easytry_e57_reader_read(handle, x, y, z, intensity, raw, rowIndex, columnIndex, capacity, out var read) == 0) throw new Exception("Native E57 read failed.");
                if (read == 0) break;
                for (var i = 0; i < (int)read; i++, index++)
                {
                    var expected = expectedPoint((int)(index / rows), (int)(index % rows));
                    if (x[i] != expected.X || y[i] != expected.Y || z[i] != expected.Z || raw[i] != expected.RawIntensity ||
                        intensity[i] != Math.Clamp(expected.ProcessedIntensity, 0f, 1f) || rowIndex[i] != expected.RowIndex || columnIndex[i] != expected.ColumnIndex)
                        throw new Exception($"E57 point {index} differs from source fields.");
                }
            }
            if ((ulong)index != count) throw new Exception("Native E57 point count mismatch.");
        }
        finally { easytry_e57_reader_destroy(handle); }
    }
}

static class SdkSmoke
{
    internal static async Task Run(string root, string resultPath, string[] inputs)
    {
        var results = new List<object>();
        var hashes = new string?[inputs.Length];
        var service = new PointCloudConversionService(vendor => new LimitedSource(vendor switch
        {
            ScannerVendor.ZPlusF => new ZfsScanSource(),
            ScannerVendor.Faro => new FaroScanSource(),
            _ => throw new NotSupportedException()
        }), () => new NativeE57Writer());
        foreach (var concurrency in new[] { 1, 2 })
        {
            var options = inputs.Select((input, index) => new ConversionOptions
            {
                InputFilePath = Path.GetFullPath(input),
                OutputFilePath = Path.Combine(root, $"sdk-{concurrency}-{index}.e57"),
                Vendor = ConversionPrecheckService.DetectVendor(input)
            }).ToArray();
            var reports = new ConversionReport?[inputs.Length];
            var timer = Stopwatch.StartNew();
            Console.WriteLine($"SDK BEGIN concurrency={concurrency}; inputs={inputs.Length}; sample=128 scan lines per input");
            await new BatchConversionScheduler().RunAsync(options, concurrency, async (index, item, token) =>
            {
                reports[index] = await service.ConvertAsync(item, null, token);
                Console.WriteLine($"SDK DONE {index + 1}/{inputs.Length}; points={reports[index]!.ValidPointCount}; elapsed={timer.Elapsed.TotalSeconds:F3}s");
            }, CancellationToken.None);
            timer.Stop();
            for (var index = 0; index < inputs.Length; index++)
            {
                var hash = NativeReadback.Fingerprint(options[index].OutputFilePath);
                hashes[index] ??= hash;
                if (hash != hashes[index]) throw new Exception("Sequential and parallel SDK outputs differ in decoded point fields.");
                results.Add(new { Concurrency = concurrency, Input = inputs[index], SecondsForWholeBatch = timer.Elapsed.TotalSeconds, reports[index]!.ValidPointCount, reports[index]!.InvalidPointCount, reports[index]!.ProcessedScanLines, DecodedSha256 = hash });
                File.Delete(options[index].OutputFilePath);
            }
        }
        File.WriteAllText(resultPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: sequential and parallel real SDK sample outputs match in all decoded point fields.");
    }

    private sealed class LimitedSource(IScanSource inner) : IScanSource
    {
        public ScanMetadata Metadata { get; private set; } = new("", ScannerVendor.Auto, 0, 0);
        public void Open(ConversionOptions options, CancellationToken token)
        {
            inner.Open(options, token);
            if (inner.Metadata.ScanColumns < 128) throw new Exception("SDK smoke input must contain at least 128 scan lines.");
            Metadata = inner.Metadata with { ScanColumns = 128 };
        }
        public IEnumerable<PointBatch> ReadBatches(int linesPerBatch, CancellationToken token)
        {
            long lines = 0;
            foreach (var batch in inner.ReadBatches(linesPerBatch, token))
            {
                lines += batch.ScanLineCount;
                yield return batch;
                if (lines >= Metadata.ScanColumns) yield break;
            }
        }
        public void Dispose() => inner.Dispose();
    }
}
