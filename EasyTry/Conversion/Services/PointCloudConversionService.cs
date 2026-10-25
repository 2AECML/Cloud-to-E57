using System.Diagnostics;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Sources;
using EasyTry.Conversion.Writers;

namespace EasyTry.Conversion.Services;

/// <summary>
/// 全文件流式转换服务。默认通过 NativeE57Writer 写入并校验真实 E57。
/// </summary>
public sealed class PointCloudConversionService : IPointCloudConversionService
{
    public const int DefaultScanLinesPerBatch = 64;

    private readonly Func<ScannerVendor, IScanSource> sourceFactory;
    private readonly Func<IPointCloudWriter> writerFactory;
    private readonly ConversionWorkerClient? workerClient;

    public PointCloudConversionService()
        : this(CreateSource, static () => new NativeE57Writer())
    {
        workerClient = new ConversionWorkerClient();
    }

    public PointCloudConversionService(
        Func<ScannerVendor, IScanSource> sourceFactory,
        Func<IPointCloudWriter> writerFactory)
    {
        this.sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        this.writerFactory = writerFactory ?? throw new ArgumentNullException(nameof(writerFactory));
    }

    public Task<ConversionReport> ConvertAsync(
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ConversionReport>(cancellationToken);
        }
        if (workerClient is not null)
        {
            return workerClient.ConvertAsync(options, progress, cancellationToken);
        }
        var vendor = options.Vendor == ScannerVendor.Auto
            ? ConversionPrecheckService.DetectVendor(options.InputFilePath)
            : options.Vendor;
        if (vendor == ScannerVendor.Faro)
        {
            return ConvertOnFaroThread(options, progress, cancellationToken);
        }
        return Task.Run(
            () => ConvertCore(options, progress, cancellationToken),
            cancellationToken);
    }

    internal static ConversionReport ConvertInWorker(
        ConversionOptions options,
        IProgress<ConversionProgress> progress,
        CancellationToken token)
    {
        var service = new PointCloudConversionService(CreateSource, static () => new NativeE57Writer());
        var vendor = options.Vendor == ScannerVendor.Auto
            ? ConversionPrecheckService.DetectVendor(options.InputFilePath)
            : options.Vendor;
        return vendor == ScannerVendor.Faro
            ? service.ConvertCore(options, progress, token)
            : Task.Run(() => service.ConvertCore(options, progress, token), token).GetAwaiter().GetResult();
    }

    private Task<ConversionReport> ConvertOnFaroThread(
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        // FARO's in-process COM server is registered as Apartment. Creating it
        // on an MTA pool thread routes calls through COM's shared host STA:
        // loading the next scan can then block other tasks' point reads.
        // Keep construction, reads and disposal on this task's own STA.
        var completion = new TaskCompletionSource<ConversionReport>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(ConvertCore(options, progress, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "FARO conversion"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    private ConversionReport ConvertCore(
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var vendor = options.Vendor == ScannerVendor.Auto
            ? ConversionPrecheckService.DetectVendor(options.InputFilePath)
            : options.Vendor;
        if (vendor == ScannerVendor.Auto)
        {
            throw new NotSupportedException("无法根据输入文件识别扫描仪厂商。");
        }

        using var source = options.ContinuousInputPaths.Count > 0
            ? CreateContinuousSource(vendor)
            : sourceFactory(vendor);
        var stopwatch = Stopwatch.StartNew();

        progress?.Report(new ConversionProgress(0, "正在打开原始扫描文件并创建 E57…"));
        source.Open(options, cancellationToken);
        var metadata = source.Metadata;
        var resolvedOutputMode = ConversionOutputLayout.ResolveOutputMode(
            options,
            metadata.ScanColumns);
        var effectiveOptions = CloneWithOutputMode(options, resolvedOutputMode);
        var outputStage = options.OutputMode switch
        {
            E57OutputMode.AutoByScanColumnThreshold
                when resolvedOutputMode == E57OutputMode.SegmentedDataset =>
                $"总扫描列 {metadata.ScanColumns:N0} 超过阈值 {options.SegmentScanColumns:N0}，正在创建连续分段 E57 数据集…",
            E57OutputMode.AutoByScanColumnThreshold =>
                $"总扫描列 {metadata.ScanColumns:N0} 未超过阈值 {options.SegmentScanColumns:N0}，正在创建单个 E57…",
            E57OutputMode.SegmentedDataset => "正在创建连续分段 E57 数据集…",
            _ => "正在创建单个 E57…"
        };
        progress?.Report(new ConversionProgress(0, outputStage));
        using var writer = resolvedOutputMode == E57OutputMode.SegmentedDataset
            ? new SegmentedE57Writer(writerFactory)
            : writerFactory();
        writer.Open(metadata, effectiveOptions);

        long processedLines = 0;
        long validPoints = 0;
        long invalidPoints = 0;
        var xRange = new RangeAccumulator();
        var yRange = new RangeAccumulator();
        var zRange = new RangeAccumulator();
        var rawIntensityRange = new RangeAccumulator();
        var processedIntensityRange = new RangeAccumulator();

        foreach (var batch in source.ReadBatches(DefaultScanLinesPerBatch, cancellationToken))
        {
            using (batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteBatch(batch, cancellationToken);

                processedLines += batch.ScanLineCount;
                validPoints += batch.ValidPointCount;
                invalidPoints += batch.InvalidPointCount;

                foreach (var point in batch.Points)
                {
                    xRange.Add(point.X);
                    yRange.Add(point.Y);
                    zRange.Add(point.Z);
                    rawIntensityRange.Add(point.RawIntensity);
                    processedIntensityRange.Add(point.ProcessedIntensity);
                }
            }

            var percent = metadata.ScanColumns == 0
                ? 0
                : (int)Math.Clamp(processedLines * 100L / metadata.ScanColumns, 0, 100);
            progress?.Report(new ConversionProgress(
                percent,
                $"正在转换并写入 E57（每批 {DefaultScanLinesPerBatch} 条扫描线）…",
                processedLines,
                metadata.ScanColumns,
                validPoints,
                invalidPoints));
        }

        stopwatch.Stop();
        var report = new ConversionReport(
            metadata,
            processedLines,
            validPoints,
            invalidPoints,
            xRange.ToRange(),
            yRange.ToRange(),
            zRange.ToRange(),
            rawIntensityRange.ToRange(),
            processedIntensityRange.ToRange(),
            stopwatch.Elapsed,
            resolvedOutputMode);
        writer.Complete(report);
        var metadataDocumentPath = ScanMetadataDocumentWriter.Write(
            report,
            effectiveOptions);
        report = report with { MetadataDocumentPath = metadataDocumentPath };

        progress?.Report(new ConversionProgress(
            100,
            "全文件转换及 E57 回读校验完成",
            processedLines,
            metadata.ScanColumns,
            validPoints,
            invalidPoints));
        return report;
    }

    private static IScanSource CreateSource(ScannerVendor vendor) => vendor switch
    {
        ScannerVendor.ZPlusF => new ZfsScanSource(),
        ScannerVendor.Faro => new FaroScanSource(),
        _ => throw new NotSupportedException($"不支持的扫描仪厂商：{vendor}")
    };

    private IScanSource CreateContinuousSource(ScannerVendor vendor) => vendor switch
    {
        ScannerVendor.Faro => new ContinuousFaroScanSource(
            () => sourceFactory(ScannerVendor.Faro)),
        _ => throw new NotSupportedException(
            "当前连续多输入合并仅支持 FARO .fls 文件或扫描包。")
    };

    private static ConversionOptions CloneWithOutputMode(
        ConversionOptions source,
        E57OutputMode outputMode) => new()
    {
        InputFilePath = source.InputFilePath,
        TlsdFilePath = source.TlsdFilePath,
        ContinuousInputPaths = source.ContinuousInputPaths,
        OutputFilePath = source.OutputFilePath,
        Vendor = source.Vendor,
        MileageDirection = source.MileageDirection,
        ScanDirection = source.ScanDirection,
        PreserveRawIntensity = source.PreserveRawIntensity,
        PreserveProcessedIntensity = source.PreserveProcessedIntensity,
        OutputMode = outputMode,
        SegmentScanColumns = source.SegmentScanColumns
    };

    private sealed class RangeAccumulator
    {
        private double minimum = double.PositiveInfinity;
        private double maximum = double.NegativeInfinity;

        public void Add(double value)
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        public NumericRange ToRange() =>
            double.IsPositiveInfinity(minimum)
                ? new NumericRange(double.NaN, double.NaN)
                : new NumericRange(minimum, maximum);
    }
}
