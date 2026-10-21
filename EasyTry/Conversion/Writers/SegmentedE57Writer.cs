using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;

namespace EasyTry.Conversion.Writers;

/// <summary>
/// 将一个逻辑移动二维扫描连续写成多个真实 E57。各分段的 columnIndex 从零开始，
/// cartesianY 保留有效全局扫描列号；dataset_manifest.json 记录全局列偏移。
/// </summary>
public sealed class SegmentedE57Writer : IPointCloudWriter
{
    private const string ManifestSchema = "easytry-segmented-e57-dataset-v2";
    private const string StateSchema = "easytry-segmented-e57-state-v2";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Func<IPointCloudWriter> writerFactory;
    private readonly List<SegmentPartEntry> completedParts = [];
    private readonly HashSet<int> completedPartIndices = [];
    private ScanMetadata? metadata;
    private ConversionOptions? options;
    private string? datasetDirectory;
    private string? partsDirectory;
    private string? statePath;
    private DatasetFingerprint? fingerprint;
    private IPointCloudWriter? currentWriter;
    private PartAccumulator? currentAccumulator;
    private int currentPartIndex = -1;
    private bool completed;

    public SegmentedE57Writer(Func<IPointCloudWriter> writerFactory)
    {
        this.writerFactory = writerFactory
            ?? throw new ArgumentNullException(nameof(writerFactory));
    }

    public void Open(ScanMetadata metadata, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        if (this.metadata is not null)
        {
            throw new InvalidOperationException("分段 E57 写入器已经打开。");
        }
        if (options.OutputMode != E57OutputMode.SegmentedDataset)
        {
            throw new InvalidOperationException("分段写入器需要连续分段输出模式。");
        }
        if (options.SegmentScanColumns < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options.SegmentScanColumns));
        }

        this.metadata = metadata;
        this.options = options;
        datasetDirectory = ConversionOutputLayout.GetDatasetDirectory(
            options.OutputFilePath);
        partsDirectory = Path.Combine(datasetDirectory, "parts");
        statePath = Path.Combine(datasetDirectory, "conversion_state.json");
        fingerprint = DatasetFingerprint.Create(metadata, options);

        if (Directory.Exists(datasetDirectory) && !File.Exists(statePath))
        {
            if (Directory.EnumerateFileSystemEntries(datasetDirectory).Any())
            {
                throw new InvalidOperationException(
                    $"分段数据集目录已存在但没有可恢复状态，请选择新的输出名称：{datasetDirectory}");
            }
        }

        Directory.CreateDirectory(partsDirectory);
        LoadCompatibleState();
        SaveState("running");
    }

    public void WriteBatch(PointBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        EnsureOpen();
        cancellationToken.ThrowIfCancellationRequested();

        var slices = BuildSlices(batch);
        var pointsByPart = batch.Points
            .GroupBy(point => GetPartIndex(point.ColumnIndex))
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var slice in slices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completedPartIndices.Contains(slice.PartIndex))
            {
                continue;
            }

            EnsurePartOpen(slice.PartIndex);
            var partStart = GetPartStart(slice.PartIndex);
            var sourcePoints = pointsByPart.TryGetValue(
                slice.PartIndex,
                out var values)
                ? values
                : [];
            var localPoints = new List<UnifiedScanPoint>(sourcePoints.Length);
            foreach (var point in sourcePoints)
            {
                localPoints.Add(new UnifiedScanPoint
                {
                    X = point.X,
                    Y = point.Y,
                    Z = point.Z,
                    RawIntensity = point.RawIntensity,
                    ProcessedIntensity = point.ProcessedIntensity,
                    RowIndex = point.RowIndex,
                    ColumnIndex = checked((uint)(point.ColumnIndex - partStart))
                });
            }

            var sourcePointCount = checked(
                (long)slice.ScanLineCount * metadata!.ScanRows);
            var invalidPointCount = sourcePointCount - localPoints.Count;
            var localFirstColumn = checked((uint)(slice.FirstGlobalColumn - partStart));
            using var localBatch = new PointBatch(
                localFirstColumn,
                slice.ScanLineCount,
                sourcePointCount,
                invalidPointCount,
                localPoints);
            currentWriter!.WriteBatch(localBatch, cancellationToken);
            currentAccumulator!.Add(localBatch);

            if (slice.ReachesPartBoundary)
            {
                CompleteCurrentPart();
            }
        }
    }

    public void Complete(ConversionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        EnsureOpen();
        CompleteCurrentPart();

        var expectedPartCount = checked((int)Math.Ceiling(
            metadata!.ScanColumns / (double)options!.SegmentScanColumns));
        if (completedPartIndices.Count != expectedPartCount)
        {
            throw new InvalidOperationException(
                $"分段写入不完整：预期 {expectedPartCount} 段，已完成 {completedPartIndices.Count} 段。");
        }
        var segmentedValidPoints = completedParts.Sum(part => part.ValidPointCount);
        var segmentedInvalidPoints = completedParts.Sum(part => part.InvalidPointCount);
        var segmentedColumns = completedParts.Sum(part => (long)part.LocalColumnCount);
        if (segmentedValidPoints != report.ValidPointCount ||
            segmentedInvalidPoints != report.InvalidPointCount ||
            segmentedColumns != metadata.ScanColumns)
        {
            throw new InvalidDataException(
                "分段汇总统计与逻辑扫描统计不一致，未生成完成清单。");
        }

        var manifest = new SegmentedDatasetManifest(
            ManifestSchema,
            "completed",
            Path.GetFileNameWithoutExtension(options.OutputFilePath),
            metadata.SourceFilePath,
            metadata.Vendor.ToString(),
            new SourceScannerMetadata(
                metadata.ScannerModel,
                metadata.ScannerSerialNumber,
                metadata.ScannerFirmware,
                metadata.AcquisitionDate,
                metadata.ProjectName,
                metadata.Location),
            metadata.SourceParts?.Select(part => new SourcePartEntry(
                part.SourceIndex,
                part.SourcePath,
                part.LocalScanColumns,
                part.GlobalPhysicalColumnStart,
                part.GlobalPhysicalColumnEnd,
                part.EffectiveGlobalColumnStart,
                part.EffectiveGlobalColumnEnd,
                part.ScannerModel,
                part.ScannerSerialNumber,
                part.AcquisitionDate)).ToArray() ?? [],
            metadata.ScanRows,
            metadata.ScanColumns,
            options.SegmentScanColumns,
            new CoordinateConvention(
                "tunnel_cross_section_horizontal",
                "effective_global_scan_column_index",
                "tunnel_cross_section_vertical",
                "globalColumnIndex = globalColumnStart + columnIndex",
                "cartesianY = effectiveGlobalColumnIndex; metric distance is assigned during orthophoto generation"),
            completedParts.OrderBy(part => part.PartIndex).ToArray(),
            report.ValidPointCount,
            report.InvalidPointCount,
            DateTimeOffset.UtcNow);
        AtomicWriteJson(
            Path.Combine(datasetDirectory!, "dataset_manifest.json"),
            manifest);
        completed = true;
        SaveState("completed");
    }

    public void Dispose()
    {
        currentWriter?.Dispose();
        currentWriter = null;
        currentAccumulator = null;
        currentPartIndex = -1;
        if (metadata is not null && !completed)
        {
            try
            {
                SaveState("interrupted");
            }
            catch
            {
                // 保留原始异常；已完成分段及上一次原子状态文件仍可恢复。
            }
        }
    }

    private void EnsurePartOpen(int partIndex)
    {
        if (currentWriter is not null && currentPartIndex == partIndex)
        {
            return;
        }
        CompleteCurrentPart();

        var partStart = GetPartStart(partIndex);
        var partColumns = GetPartColumnCount(partIndex);
        var partPath = GetPartPath(partIndex);
        var partMetadata = metadata! with
        {
            ScanColumns = partColumns,
            GlobalColumnOffset = partStart,
            LogicalTotalScanColumns = metadata.ScanColumns
        };
        var partOptions = CloneForPart(options!, partPath);
        currentWriter = writerFactory();
        currentWriter.Open(partMetadata, partOptions);
        currentPartIndex = partIndex;
        currentAccumulator = new PartAccumulator(
            partIndex,
            partStart,
            partColumns,
            partPath,
            metadata.ScanRows);
    }

    private void CompleteCurrentPart()
    {
        if (currentWriter is null || currentAccumulator is null)
        {
            return;
        }

        var report = currentAccumulator.CreateReport(metadata!);
        try
        {
            currentWriter.Complete(report);
        }
        finally
        {
            currentWriter.Dispose();
            currentWriter = null;
        }

        var entry = currentAccumulator.CreateEntry(datasetDirectory!);
        completedParts.RemoveAll(value => value.PartIndex == entry.PartIndex);
        completedParts.Add(entry);
        completedPartIndices.Add(entry.PartIndex);
        currentAccumulator = null;
        currentPartIndex = -1;
        SaveState("running");
    }

    private IReadOnlyList<BatchSlice> BuildSlices(PointBatch batch)
    {
        var result = new List<BatchSlice>();
        BatchSlice? current = null;
        for (var offset = 0; offset < batch.ScanLineCount; offset++)
        {
            var globalColumn = options!.MileageDirection == MileageDirection.Ascending
                ? checked((long)batch.FirstColumnIndex + offset)
                : checked((long)batch.FirstColumnIndex - offset);
            if (globalColumn < 0 || globalColumn >= metadata!.ScanColumns)
            {
                throw new InvalidDataException(
                    $"扫描列索引超出逻辑数据集范围：{globalColumn}。");
            }
            var partIndex = checked((int)(globalColumn / options.SegmentScanColumns));
            if (current is null || current.PartIndex != partIndex)
            {
                if (current is not null)
                {
                    result.Add(current);
                }
                current = new BatchSlice(partIndex, globalColumn, globalColumn, 1, false);
            }
            else
            {
                current = current with
                {
                    LastGlobalColumn = globalColumn,
                    ScanLineCount = current.ScanLineCount + 1
                };
            }

            var partStart = GetPartStart(partIndex);
            var partEnd = partStart + GetPartColumnCount(partIndex) - 1;
            var reachesBoundary = options.MileageDirection == MileageDirection.Ascending
                ? globalColumn == partEnd
                : globalColumn == partStart;
            if (reachesBoundary)
            {
                current = current with { ReachesPartBoundary = true };
            }
        }
        if (current is not null)
        {
            result.Add(current);
        }
        return result;
    }

    private int GetPartIndex(uint globalColumnIndex) => checked(
        (int)(globalColumnIndex / options!.SegmentScanColumns));

    private long GetPartStart(int partIndex) => checked(
        (long)partIndex * options!.SegmentScanColumns);

    private int GetPartColumnCount(int partIndex) => checked((int)Math.Min(
        options!.SegmentScanColumns,
        metadata!.ScanColumns - GetPartStart(partIndex)));

    private string GetPartPath(int partIndex)
    {
        var name = Path.GetFileNameWithoutExtension(options!.OutputFilePath);
        return Path.Combine(partsDirectory!, $"{name}_part_{partIndex:D4}.e57");
    }

    private void LoadCompatibleState()
    {
        if (!File.Exists(statePath))
        {
            return;
        }
        var state = JsonSerializer.Deserialize<SegmentedDatasetState>(
            File.ReadAllText(statePath),
            JsonOptions)
            ?? throw new InvalidDataException("分段转换状态文件为空。");
        if (state.SchemaVersion != StateSchema || state.Fingerprint != fingerprint)
        {
            throw new InvalidOperationException(
                "现有分段数据集的输入文件或转换参数已经改变，请选择新的输出名称。");
        }
        foreach (var part in state.CompletedParts)
        {
            var fullPath = Path.Combine(
                datasetDirectory!,
                part.File.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }
            completedParts.Add(part);
            completedPartIndices.Add(part.PartIndex);
        }
    }

    private void SaveState(string status)
    {
        AtomicWriteJson(
            statePath!,
            new SegmentedDatasetState(
                StateSchema,
                status,
                fingerprint!,
                completedParts.OrderBy(part => part.PartIndex).ToArray(),
                DateTimeOffset.UtcNow));
    }

    private void EnsureOpen()
    {
        if (metadata is null || options is null || completed)
        {
            throw new InvalidOperationException("分段 E57 写入器尚未打开或已经完成。");
        }
    }

    private static ConversionOptions CloneForPart(
        ConversionOptions source,
        string partPath) => new()
    {
        InputFilePath = source.InputFilePath,
        TlsdFilePath = source.TlsdFilePath,
        ContinuousInputPaths = source.ContinuousInputPaths,
        OutputFilePath = partPath,
        Vendor = source.Vendor,
        MileageDirection = source.MileageDirection,
        ScanDirection = source.ScanDirection,
        PreserveRawIntensity = source.PreserveRawIntensity,
        PreserveProcessedIntensity = source.PreserveProcessedIntensity,
        OutputMode = E57OutputMode.SingleFile,
        SegmentScanColumns = source.SegmentScanColumns
    };

    private static void AtomicWriteJson<T>(string path, T value)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private sealed class PartAccumulator
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private readonly RangeAccumulator x = new();
        private readonly RangeAccumulator y = new();
        private readonly RangeAccumulator z = new();
        private readonly RangeAccumulator raw = new();
        private readonly RangeAccumulator processed = new();
        private long processedLines;
        private long validPoints;
        private long invalidPoints;

        public PartAccumulator(
            int partIndex,
            long globalColumnStart,
            int columnCount,
            string filePath,
            int scanRows)
        {
            PartIndex = partIndex;
            GlobalColumnStart = globalColumnStart;
            ColumnCount = columnCount;
            FilePath = filePath;
            ScanRows = scanRows;
        }

        public int PartIndex { get; }
        public long GlobalColumnStart { get; }
        public int ColumnCount { get; }
        public string FilePath { get; }
        public int ScanRows { get; }

        public void Add(PointBatch batch)
        {
            processedLines += batch.ScanLineCount;
            validPoints += batch.ValidPointCount;
            invalidPoints += batch.InvalidPointCount;
            foreach (var point in batch.Points)
            {
                x.Add(point.X);
                y.Add(point.Y);
                z.Add(point.Z);
                raw.Add(point.RawIntensity);
                processed.Add(point.ProcessedIntensity);
            }
        }

        public ConversionReport CreateReport(ScanMetadata logicalMetadata)
        {
            stopwatch.Stop();
            if (processedLines != ColumnCount ||
                validPoints + invalidPoints != (long)ColumnCount * ScanRows)
            {
                throw new InvalidDataException(
                    $"E57 分段 {PartIndex:D4} 的扫描列或点数统计不完整。");
            }
            return new ConversionReport(
                logicalMetadata with
                {
                    ScanColumns = ColumnCount,
                    GlobalColumnOffset = GlobalColumnStart,
                    LogicalTotalScanColumns = logicalMetadata.ScanColumns
                },
                processedLines,
                validPoints,
                invalidPoints,
                x.Value,
                y.Value,
                z.Value,
                raw.Value,
                processed.Value,
                stopwatch.Elapsed);
        }

        public SegmentPartEntry CreateEntry(string datasetRoot) => new(
            PartIndex,
            Path.GetRelativePath(datasetRoot, FilePath).Replace('\\', '/'),
            GlobalColumnStart,
            GlobalColumnStart + ColumnCount - 1,
            ColumnCount,
            ScanRows,
            validPoints,
            invalidPoints,
            new FileInfo(FilePath).Length,
            x.Value,
            y.Value,
            z.Value,
            raw.Value,
            processed.Value);
    }

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

        public NumericRange Value => double.IsPositiveInfinity(minimum)
            ? new NumericRange(double.NaN, double.NaN)
            : new NumericRange(minimum, maximum);
    }

    private sealed record BatchSlice(
        int PartIndex,
        long FirstGlobalColumn,
        long LastGlobalColumn,
        int ScanLineCount,
        bool ReachesPartBoundary);

    private sealed record DatasetFingerprint(
        string SourcePath,
        long SourceLength,
        long SourceModifiedUtcTicks,
        string? SourceSetFingerprint,
        string Vendor,
        long ScanColumns,
        int ScanRows,
        string MileageDirection,
        string ScanDirection,
        int SegmentScanColumns)
    {
        public static DatasetFingerprint Create(
            ScanMetadata metadata,
            ConversionOptions options)
        {
            var fullPath = Path.GetFullPath(metadata.SourceFilePath);
            var file = File.Exists(fullPath) ? new FileInfo(fullPath) : null;
            var directory = Directory.Exists(fullPath)
                ? new DirectoryInfo(fullPath)
                : null;
            var sourceSetFingerprint = CreateSourceSetFingerprint(
                metadata.SourceParts);
            return new DatasetFingerprint(
                fullPath,
                file?.Length ?? 0,
                file?.LastWriteTimeUtc.Ticks
                    ?? directory?.LastWriteTimeUtc.Ticks
                    ?? 0,
                sourceSetFingerprint,
                metadata.Vendor.ToString(),
                metadata.ScanColumns,
                metadata.ScanRows,
                options.MileageDirection.ToString(),
                options.ScanDirection.ToString(),
                options.SegmentScanColumns);
        }

        private static string? CreateSourceSetFingerprint(
            IReadOnlyList<ScanSourcePartMetadata>? sourceParts)
        {
            if (sourceParts is null || sourceParts.Count == 0)
            {
                return null;
            }

            var value = string.Join(
                "\n",
                sourceParts.OrderBy(part => part.SourceIndex).Select(part =>
                {
                    var fullPath = Path.GetFullPath(part.SourcePath);
                    var file = File.Exists(fullPath) ? new FileInfo(fullPath) : null;
                    var directory = Directory.Exists(fullPath)
                        ? new DirectoryInfo(fullPath)
                        : null;
                    return string.Join(
                        "|",
                        part.SourceIndex,
                        fullPath,
                        file?.Length ?? 0,
                        file?.LastWriteTimeUtc.Ticks
                            ?? directory?.LastWriteTimeUtc.Ticks
                            ?? 0,
                        part.LocalScanColumns,
                        part.GlobalPhysicalColumnStart);
                }));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }

    private sealed record SegmentedDatasetState(
        string SchemaVersion,
        string Status,
        DatasetFingerprint Fingerprint,
        SegmentPartEntry[] CompletedParts,
        DateTimeOffset UpdatedUtc);

    private sealed record SegmentedDatasetManifest(
        string SchemaVersion,
        string Status,
        string DatasetName,
        string SourcePath,
        string Vendor,
        SourceScannerMetadata ScannerMetadata,
        SourcePartEntry[] Sources,
        int ScanRows,
        long TotalScanColumns,
        int PartColumnCount,
        CoordinateConvention CoordinateConvention,
        SegmentPartEntry[] Parts,
        long ValidPointCount,
        long InvalidPointCount,
        DateTimeOffset CompletedUtc);

    private sealed record SourceScannerMetadata(
        string? Model,
        string? SerialNumber,
        string? Firmware,
        DateTime? AcquisitionDate,
        string? ProjectName,
        string? Location);

    private sealed record SourcePartEntry(
        int SourceIndex,
        string SourcePath,
        long LocalScanColumns,
        long GlobalPhysicalColumnStart,
        long GlobalPhysicalColumnEnd,
        long EffectiveGlobalColumnStart,
        long EffectiveGlobalColumnEnd,
        string? ScannerModel,
        string? ScannerSerialNumber,
        DateTime? AcquisitionDate);

    private sealed record CoordinateConvention(
        string X,
        string Y,
        string Z,
        string GlobalColumnIndex,
        string CartesianY);

    private sealed record SegmentPartEntry(
        int PartIndex,
        string File,
        long GlobalColumnStart,
        long GlobalColumnEnd,
        int LocalColumnCount,
        int ScanRows,
        long ValidPointCount,
        long InvalidPointCount,
        long FileSizeBytes,
        NumericRange XRange,
        NumericRange YRange,
        NumericRange ZRange,
        NumericRange RawIntensityRange,
        NumericRange ProcessedIntensityRange);
}
