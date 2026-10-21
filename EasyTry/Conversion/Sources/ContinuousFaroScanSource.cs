using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;

namespace EasyTry.Conversion.Sources;

/// <summary>
/// Streams several separately stored FARO scans as one logical scan. Source
/// packages are opened one at a time, so memory use is independent of the
/// number of packages.
/// </summary>
public sealed class ContinuousFaroScanSource : IScanSource
{
    private readonly Func<IScanSource> sourceFactory;
    private IReadOnlyList<SourceSegment> segments = [];
    private ConversionOptions? options;

    public ContinuousFaroScanSource()
        : this(static () => new FaroScanSource())
    {
    }

    internal ContinuousFaroScanSource(Func<IScanSource> sourceFactory)
    {
        this.sourceFactory = sourceFactory
            ?? throw new ArgumentNullException(nameof(sourceFactory));
    }

    public ScanMetadata Metadata { get; private set; } =
        new(string.Empty, ScannerVendor.Faro, 0, 0);

    public void Open(ConversionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        Dispose();

        var paths = ContinuousFaroInputSet.NormalizeAndValidate(
            options.ContinuousInputPaths.Count > 0
                ? options.ContinuousInputPaths
                : [options.InputFilePath]);
        var inspected = new List<(string Path, ScanMetadata Metadata)>(paths.Count);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = sourceFactory();
            source.Open(CloneForSource(options, path), cancellationToken);
            inspected.Add((path, source.Metadata));
        }

        var first = inspected[0].Metadata;
        foreach (var item in inspected.Skip(1))
        {
            ValidateCompatible(first, item.Metadata, item.Path);
        }

        var totalColumns = inspected.Aggregate(
            0L,
            (total, item) => checked(total + item.Metadata.ScanColumns));
        if (totalColumns <= 0 || totalColumns > uint.MaxValue)
        {
            throw new InvalidDataException(
                $"连续 FARO 扫描组总列数超出 E57 索引支持范围：{totalColumns:N0}。");
        }

        var builtSegments = new List<SourceSegment>(inspected.Count);
        long offset = 0;
        for (var index = 0; index < inspected.Count; index++)
        {
            var item = inspected[index];
            builtSegments.Add(new SourceSegment(
                index,
                item.Path,
                offset,
                item.Metadata));
            offset += item.Metadata.ScanColumns;
        }

        var sourceParts = builtSegments.Select(segment =>
        {
            var physicalStart = segment.GlobalPhysicalColumnOffset;
            var physicalEnd = physicalStart + segment.Metadata.ScanColumns - 1;
            var effectiveStart = options.MileageDirection == MileageDirection.Ascending
                ? physicalStart
                : totalColumns - physicalEnd - 1;
            var effectiveEnd = options.MileageDirection == MileageDirection.Ascending
                ? physicalEnd
                : totalColumns - physicalStart - 1;
            return new ScanSourcePartMetadata(
                segment.SourceIndex,
                segment.Path,
                segment.Metadata.ScanColumns,
                physicalStart,
                physicalEnd,
                Math.Min(effectiveStart, effectiveEnd),
                Math.Max(effectiveStart, effectiveEnd),
                segment.Metadata.ScannerModel,
                segment.Metadata.ScannerSerialNumber,
                segment.Metadata.AcquisitionDate);
        }).ToArray();

        this.options = options;
        segments = builtSegments;
        Metadata = first with
        {
            SourceFilePath = paths[0],
            ScanColumns = totalColumns,
            GlobalColumnOffset = 0,
            LogicalTotalScanColumns = totalColumns,
            SourceParts = sourceParts
        };
    }

    public IEnumerable<PointBatch> ReadBatches(
        int scanLinesPerBatch,
        CancellationToken cancellationToken)
    {
        if (options is null || segments.Count == 0)
        {
            throw new InvalidOperationException("必须先打开连续 FARO 扫描组。");
        }

        var totalColumns = Metadata.ScanColumns;
        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = sourceFactory();
            source.Open(CloneForSource(options, segment.Path), cancellationToken);
            if (source.Metadata.ScanColumns != segment.Metadata.ScanColumns ||
                source.Metadata.ScanRows != Metadata.ScanRows)
            {
                throw new InvalidDataException(
                    $"FARO 扫描结构在预检查后发生变化：{segment.Path}");
            }

            foreach (var batch in source.ReadBatches(scanLinesPerBatch, cancellationToken))
            {
                using (batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var remappedPoints = new List<UnifiedScanPoint>(batch.Points.Count);
                    foreach (var point in batch.Points)
                    {
                        var physicalColumn = checked(
                            segment.GlobalPhysicalColumnOffset + point.ColumnIndex);
                        var effectiveColumn = options.MileageDirection == MileageDirection.Ascending
                            ? physicalColumn
                            : totalColumns - physicalColumn - 1;
                        remappedPoints.Add(new UnifiedScanPoint
                        {
                            X = point.X,
                            Y = effectiveColumn,
                            Z = point.Z,
                            RawIntensity = point.RawIntensity,
                            ProcessedIntensity = point.ProcessedIntensity,
                            RowIndex = point.RowIndex,
                            ColumnIndex = checked((uint)effectiveColumn)
                        });
                    }

                    var firstPhysicalColumn = checked(
                        segment.GlobalPhysicalColumnOffset + batch.FirstColumnIndex);
                    var firstEffectiveColumn = options.MileageDirection == MileageDirection.Ascending
                        ? firstPhysicalColumn
                        : totalColumns - firstPhysicalColumn - 1;
                    yield return new PointBatch(
                        checked((uint)firstEffectiveColumn),
                        batch.ScanLineCount,
                        batch.SourcePointCount,
                        batch.InvalidPointCount,
                        remappedPoints);
                }
            }
        }
    }

    public void Dispose()
    {
        segments = [];
        options = null;
    }

    private static ConversionOptions CloneForSource(
        ConversionOptions source,
        string inputPath) => new()
    {
        InputFilePath = inputPath,
        TlsdFilePath = source.TlsdFilePath,
        OutputFilePath = source.OutputFilePath,
        Vendor = ScannerVendor.Faro,
        MileageDirection = MileageDirection.Ascending,
        ScanDirection = source.ScanDirection,
        PreserveRawIntensity = source.PreserveRawIntensity,
        PreserveProcessedIntensity = source.PreserveProcessedIntensity,
        OutputMode = E57OutputMode.SingleFile,
        SegmentScanColumns = source.SegmentScanColumns
    };

    private static void ValidateCompatible(
        ScanMetadata first,
        ScanMetadata current,
        string currentPath)
    {
        if (current.Vendor != ScannerVendor.Faro || current.ScanRows != first.ScanRows)
        {
            throw new InvalidDataException(
                $"连续 FARO 扫描组的扫描行数或厂商不一致：{currentPath}");
        }

        EnsureSameValue("扫描仪型号", first.ScannerModel, current.ScannerModel, currentPath);
        EnsureSameValue(
            "扫描仪序列号",
            first.ScannerSerialNumber,
            current.ScannerSerialNumber,
            currentPath);
        EnsureSameValue("扫描项目", first.ProjectName, current.ProjectName, currentPath);
    }

    private static void EnsureSameValue(
        string fieldName,
        string? first,
        string? current,
        string currentPath)
    {
        if (!string.IsNullOrWhiteSpace(first) &&
            !string.IsNullOrWhiteSpace(current) &&
            !string.Equals(first, current, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"连续 FARO 扫描组的{fieldName}不一致：{currentPath}");
        }
    }

    private sealed record SourceSegment(
        int SourceIndex,
        string Path,
        long GlobalPhysicalColumnOffset,
        ScanMetadata Metadata);
}
