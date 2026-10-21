using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;
using System.Text.RegularExpressions;
using ZfsSdkRef;

namespace EasyTry.Conversion.Sources;

public sealed class ZfsScanSource : IScanSource
{
    // Z+F SDK 公布的 ZFS 文件头字段；实际 PROFILER 9012 数据已验证。
    private const int ScannerModelHeaderIndex = 11214;
    private const int ScannerCommandHeaderIndex = 598;
    private const int PixelDataRateHeaderIndex = 11015;

    private static readonly object LicenseLock = new();
    private static bool licenseInitialized;

    private readonly ISdkCredentialProvider credentialProvider;
    private ZfsLineByLineRef? activeReader;
    private IReadOnlyList<VolumeSegment> volumeSegments = [];
    private ConversionOptions? options;

    public ZfsScanSource()
        : this(new SdkCredentialProvider())
    {
    }

    internal ZfsScanSource(ISdkCredentialProvider credentialProvider)
    {
        this.credentialProvider = credentialProvider;
    }

    public ScanMetadata Metadata { get; private set; } =
        new(string.Empty, ScannerVendor.ZPlusF, 0, 0);

    public void Open(ConversionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        DisposeReader();
        EnsureLicense();

        var volumeSet = ZfsVolumeSet.Resolve(options.InputFilePath);
        var primaryHeader = ReadVolumeHeader(
            volumeSet.PrimaryPath,
            cancellationToken,
            volumeSet.IsPartialVolumeChain);
        var sourceMetadata = ReadSourceMetadata(
            volumeSet.PrimaryPath,
            primaryHeader.Rows);
        var headers = new List<VolumeHeader>(2)
        {
            primaryHeader
        };

        if (volumeSet.IsMultipart)
        {
            // Z+F continuation files are cumulative tails: opening part2 lets
            // the SDK traverse part2 through the final continuation volume.
            // Reading part3, part4, ... separately would duplicate those tails.
            headers.Add(ReadVolumeHeader(
                volumeSet.VolumePaths[1],
                cancellationToken,
                allowMissingLinkedVolume: false));
        }

        var rows = headers[0].Rows;
        if (headers.Any(header => header.Rows != rows))
        {
            throw new InvalidDataException(
                "Z+F 分卷的每条扫描线点数不一致，不能安全合并为一个逻辑扫描。");
        }

        var segments = headers
            .Select(header => new VolumeSegment(
                header.FilePath,
                header.ReportedLineCount,
                header.AllowMissingLinkedVolume))
            .ToArray();
        var totalColumns = segments.Aggregate(
            0L,
            (total, segment) => checked(total + segment.UniqueLineCount));
        if (totalColumns <= 0 || totalColumns > uint.MaxValue)
        {
            throw new InvalidDataException(
                $"Z+F 逻辑扫描线总数超出支持范围：{totalColumns:N0}。");
        }

        this.options = options;
        volumeSegments = segments;
        Metadata = new ScanMetadata(
            volumeSet.PrimaryPath,
            ScannerVendor.ZPlusF,
            totalColumns,
            rows,
            ScannerModel: sourceMetadata.ScannerModel,
            ScannerSerialNumber: sourceMetadata.ScannerSerialNumber,
            ScannerFirmware: sourceMetadata.ScannerFirmware,
            AcquisitionDate: sourceMetadata.AcquisitionDate,
            ScanDataName: sourceMetadata.ScanDataName,
            ProjectName: sourceMetadata.ProjectName,
            Location: sourceMetadata.Location,
            AcquisitionParameters: sourceMetadata.AcquisitionParameters);
    }

    public IEnumerable<PointBatch> ReadBatches(
        int scanLinesPerBatch,
        CancellationToken cancellationToken)
    {
        if (options is null || volumeSegments.Count == 0)
        {
            throw new InvalidOperationException("必须先打开 ZFS 文件。");
        }
        if (scanLinesPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scanLinesPerBatch));
        }

        var totalColumns = Metadata.ScanColumns;
        long globalColumnOffset = 0;
        foreach (var segment in volumeSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.UniqueLineCount == 0)
            {
                continue;
            }

            activeReader = OpenReader(
                segment.FilePath,
                segment.AllowMissingLinkedVolume);
            try
            {
                for (var batchStart = 0;
                     batchStart < segment.UniqueLineCount;
                     batchStart += scanLinesPerBatch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batchLineCount = Math.Min(
                        scanLinesPerBatch,
                        segment.UniqueLineCount - batchStart);
                    var estimatedCapacity = Math.Min(
                        1_000_000,
                        checked(Metadata.ScanRows * batchLineCount));
                    var points = new List<UnifiedScanPoint>(estimatedCapacity);
                    long invalidCount = 0;

                    for (var localColumn = batchStart;
                         localColumn < batchStart + batchLineCount;
                         localColumn++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        activeReader.SkipTo(localColumn);
                        activeReader.Filter();

                        var physicalColumn = globalColumnOffset + localColumn;
                        var effectiveColumn = options.MileageDirection == MileageDirection.Ascending
                            ? physicalColumn
                            : totalColumns - physicalColumn - 1;
                        var scanColumnY = (double)effectiveColumn;

                        for (var sourceRow = 0; sourceRow < Metadata.ScanRows; sourceRow++)
                        {
                            activeReader.Set(sourceRow);
                            var isSet = activeReader.IsSet(sourceRow);
                            var rawIntensity = Convert.ToDouble(activeReader.Rf());
                            var processed = activeReader.RfNormProcessed();

                            if (!isSet || !double.IsFinite(processed))
                            {
                                invalidCount++;
                                continue;
                            }

                            var (sdkX, sdkY, sdkZ) = activeReader.XYZ();
                            if (!CoordinatesAreValid(sdkX, sdkY, sdkZ))
                            {
                                invalidCount++;
                                continue;
                            }

                            var rowIndex = options.ScanDirection == MobileScanDirection.Forward
                                ? sourceRow
                                : Metadata.ScanRows - sourceRow - 1;

                            points.Add(new UnifiedScanPoint
                            {
                                // 与现有 ZFS 导出约定一致：SDK Y 为隧道断面横向坐标。
                                X = sdkY,
                                Y = scanColumnY,
                                Z = sdkZ,
                                RawIntensity = rawIntensity,
                                ProcessedIntensity = (float)Math.Clamp(processed, 0.0, 1.0),
                                RowIndex = checked((uint)rowIndex),
                                ColumnIndex = checked((uint)effectiveColumn)
                            });
                        }
                    }

                    var firstPhysicalColumn = globalColumnOffset + batchStart;
                    var firstEffectiveColumn = options.MileageDirection == MileageDirection.Ascending
                        ? firstPhysicalColumn
                        : totalColumns - firstPhysicalColumn - 1;
                    yield return new PointBatch(
                        checked((uint)firstEffectiveColumn),
                        batchLineCount,
                        (long)batchLineCount * Metadata.ScanRows,
                        invalidCount,
                        points);
                }
            }
            finally
            {
                DisposeActiveReader();
            }

            globalColumnOffset += segment.UniqueLineCount;
        }
    }

    private static VolumeHeader ReadVolumeHeader(
        string filePath,
        CancellationToken cancellationToken,
        bool allowMissingLinkedVolume)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reader = OpenReader(filePath, allowMissingLinkedVolume);
        try
        {
            var columns = reader.Lines();
            var rows = reader.Pixel();
            if (columns <= 0 || rows <= 0)
            {
                throw new InvalidDataException(
                    $"ZFS 文件未返回有效的扫描线或扫描行数量：{filePath}");
            }

            return new VolumeHeader(
                filePath,
                columns,
                rows,
                allowMissingLinkedVolume);
        }
        finally
        {
            DisposeReaderInstance(reader);
        }
    }

    private static ZfsLineByLineRef OpenReader(
        string filePath,
        bool allowMissingLinkedVolume)
    {
        var flags = OpenFlags.OPEN_IGNOR_LEVEL |
                    OpenFlags.OPEN_IGNOR_COMPENSATOR;
        if (allowMissingLinkedVolume)
        {
            flags |= OpenFlags.OPEN_UNTIL_LAST;
        }
        return ZfsLineByLineRef.OpenLineByLineRef(filePath, flags)
            ?? throw new InvalidOperationException(
                $"Z+F SDK 无法打开 ZFS 文件：{filePath}");
    }

    private static SourceMetadata ReadSourceMetadata(
        string filePath,
        int pixelsPerProfile)
    {
        var header = new ZfsHeaderRef();
        try
        {
            if (!header.Read(filePath) || !header.IsValid())
            {
                return SourceMetadata.Empty;
            }

            var scannerModel = CleanMetadataValue(header.GetStringW(
                ScannerModelHeaderIndex,
                string.Empty));
            var scannerCommand = CleanMetadataValue(header.GetStringW(
                ScannerCommandHeaderIndex,
                string.Empty));
            var resolutionRaw = GetCommandArgument(scannerCommand, "resolution");
            var qualityRaw = GetCommandArgument(scannerCommand, "quality");
            var scanDataName = FirstNonEmpty(
                GetCommandArgument(scannerCommand, "file"),
                GetFallbackScanDataName(filePath));
            var projectName = GetCommandArgument(scannerCommand, "path");
            var pointRate = header.GetLong(PixelDataRateHeaderIndex, 0);

            return new SourceMetadata(
                scannerModel,
                null,
                null,
                null,
                scanDataName,
                projectName,
                null,
                ScannerParameterCatalog.ResolveZf(
                    scannerModel,
                    resolutionRaw,
                    qualityRaw,
                    pointRate > 0 ? pointRate : null,
                    pixelsPerProfile));
        }
        catch
        {
            // 元数据是可选信息；文件头版本不支持时仍继续转换点云。
            return SourceMetadata.Empty;
        }
        finally
        {
            if ((header as object) is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static string? CleanMetadataValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(CleanMetadataValue).FirstOrDefault(value => value is not null);

    private static string? GetCommandArgument(string? command, string name)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var matches = Regex.Matches(
            command,
            $"(?:^|\\s)-{Regex.Escape(name)}\\s+(?:\"(?<quoted>[^\"]*)\"|(?<plain>\\S+))",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return matches
            .Cast<Match>()
            .Reverse()
            .Select(match => CleanMetadataValue(
                match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["plain"].Value))
            .FirstOrDefault(value => value is not null && !value.StartsWith('-'));
    }

    private static string GetFallbackScanDataName(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        return Regex.Replace(
            stem,
            @"\.part[0-9]+$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private void EnsureLicense()
    {
        lock (LicenseLock)
        {
            if (licenseInitialized)
            {
                return;
            }

            var credential = credentialProvider.GetZfsCredential();
            licenseInitialized = ZfsRef.SetLicense(
                credential.Licensee,
                credential.Edition,
                credential.Key);
            if (!licenseInitialized)
            {
                throw new InvalidOperationException("Z+F SDK 拒绝了本机私有许可证配置。");
            }
        }
    }

    private static bool CoordinatesAreValid(double x, double y, double z) =>
        double.IsFinite(x) &&
        double.IsFinite(y) &&
        double.IsFinite(z) &&
        !(x == 0.0 && y == 0.0 && z == 0.0);

    public void Dispose() => DisposeReader();

    private void DisposeReader()
    {
        DisposeActiveReader();
        volumeSegments = [];
        options = null;
    }

    private void DisposeActiveReader()
    {
        if (activeReader is not null)
        {
            DisposeReaderInstance(activeReader);
            activeReader = null;
        }
    }

    private static void DisposeReaderInstance(ZfsLineByLineRef reader)
    {
        if ((reader as object) is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private sealed record VolumeHeader(
        string FilePath,
        int ReportedLineCount,
        int Rows,
        bool AllowMissingLinkedVolume);

    private sealed record VolumeSegment(
        string FilePath,
        int UniqueLineCount,
        bool AllowMissingLinkedVolume);

    private sealed record SourceMetadata(
        string? ScannerModel,
        string? ScannerSerialNumber,
        string? ScannerFirmware,
        DateTime? AcquisitionDate,
        string? ScanDataName,
        string? ProjectName,
        string? Location,
        ScanAcquisitionMetadata? AcquisitionParameters)
    {
        public static SourceMetadata Empty { get; } =
            new(null, null, null, null, null, null, null, null);
    }
}
