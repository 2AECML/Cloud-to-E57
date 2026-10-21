using System.Globalization;
using System.Runtime.InteropServices;
using EasyTry.Conversion.Models;
using EasyTry.Conversion.Services;
using IQOPENLib;

namespace EasyTry.Conversion.Sources;

public sealed class FaroScanSource : IScanSource
{
    private readonly ISdkCredentialProvider credentialProvider;
    private IiQLibIf? library;
    private ConversionOptions? options;
    private int halfScanRows;

    public FaroScanSource()
        : this(new SdkCredentialProvider())
    {
    }

    internal FaroScanSource(ISdkCredentialProvider credentialProvider)
    {
        this.credentialProvider = credentialProvider;
    }

    public ScanMetadata Metadata { get; private set; } =
        new(string.Empty, ScannerVendor.Faro, 0, 0);

    public void Open(ConversionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        DisposeLibrary();

        var licensedInterface = new iQLibIfClass();
        licensedInterface.License = credentialProvider.GetFaroLicenseText();
        library = (IiQLibIf)licensedInterface;
        library.load(options.InputFilePath);

        var sourceMetadata = ReadSourceMetadata(library, options.InputFilePath);

        var sdkColumns = library.getScanNumCols(0);
        halfScanRows = library.getScanNumRows(0);
        var logicalColumns = sdkColumns / 2;
        if (logicalColumns <= 0 || halfScanRows <= 0 || sdkColumns % 2 != 0)
        {
            DisposeLibrary();
            throw new InvalidDataException(
                "FLS 文件未返回有效的移动二维扫描结构（列数应可分成左右两半）。");
        }

        this.options = options;
        Metadata = new ScanMetadata(
            options.InputFilePath,
            ScannerVendor.Faro,
            logicalColumns,
            checked(halfScanRows * 2),
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
        if (library is null || options is null)
        {
            throw new InvalidOperationException("必须先打开 FLS 文件。");
        }
        if (scanLinesPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scanLinesPerBatch));
        }

        var totalColumns = checked((int)Metadata.ScanColumns);
        for (var batchStart = 0; batchStart < totalColumns; batchStart += scanLinesPerBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchLineCount = Math.Min(scanLinesPerBatch, totalColumns - batchStart);
            var estimatedCapacity = Math.Min(1_000_000, checked(Metadata.ScanRows * batchLineCount));
            var points = new List<UnifiedScanPoint>(estimatedCapacity);
            long invalidCount = 0;

            for (var physicalColumn = batchStart;
                 physicalColumn < batchStart + batchLineCount;
                 physicalColumn++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var effectiveColumn = options.MileageDirection == MileageDirection.Ascending
                    ? physicalColumn
                    : totalColumns - physicalColumn - 1;
                var scanColumnY = (double)effectiveColumn;

                // 两种反射模式按相同 scan/row/column 参数读取，并逐点核对坐标。
                library.scanReflectionMode = 0;
                var rawLeft = ReadHalf(physicalColumn);
                var rawRight = ReadHalf(physicalColumn + totalColumns);

                library.scanReflectionMode = 1;
                var processedLeft = ReadHalf(physicalColumn);
                var processedRight = ReadHalf(physicalColumn + totalColumns);

                invalidCount += AppendHalf(
                    rawLeft,
                    processedLeft,
                    isLeftHalf: true,
                    effectiveColumn,
                    scanColumnY,
                    points);
                invalidCount += AppendHalf(
                    rawRight,
                    processedRight,
                    isLeftHalf: false,
                    effectiveColumn,
                    scanColumnY,
                    points);
            }

            var firstEffectiveColumn = options.MileageDirection == MileageDirection.Ascending
                ? batchStart
                : totalColumns - batchStart - 1;
            yield return new PointBatch(
                checked((uint)firstEffectiveColumn),
                batchLineCount,
                (long)batchLineCount * Metadata.ScanRows,
                invalidCount,
                points);
        }
    }

    private HalfScanData ReadHalf(int sdkColumn)
    {
        library!.getXYZScanPoints2(
            0,
            0,
            sdkColumn,
            halfScanRows,
            out Array positions,
            out Array reflections);

        if (reflections.Length != halfScanRows || positions.Length != halfScanRows * 3)
        {
            throw new InvalidDataException(
                $"FARO SDK 返回的数据长度异常：column={sdkColumn}。");
        }

        return new HalfScanData(positions, reflections);
    }

    private long AppendHalf(
        HalfScanData raw,
        HalfScanData processed,
        bool isLeftHalf,
        int effectiveColumn,
        double scanColumnY,
        List<UnifiedScanPoint> destination)
    {
        if (raw.Reflections.Length != processed.Reflections.Length ||
            raw.Positions.Length != processed.Positions.Length)
        {
            throw new InvalidDataException("FARO 两种强度模式的点数不一致，无法严格对应。");
        }

        long invalidCount = 0;
        var forward = options!.ScanDirection == MobileScanDirection.Forward;
        var xSign = forward ? -1.0 : 1.0;

        for (var sourceRow = 0; sourceRow < halfScanRows; sourceRow++)
        {
            var rawX = GetPosition(raw.Positions, sourceRow, 0);
            var rawY = GetPosition(raw.Positions, sourceRow, 1);
            var rawZ = GetPosition(raw.Positions, sourceRow, 2);
            var processedX = GetPosition(processed.Positions, sourceRow, 0);
            var processedY = GetPosition(processed.Positions, sourceRow, 1);
            var processedZ = GetPosition(processed.Positions, sourceRow, 2);

            if (!CoordinatesAreValid(processedX, processedY, processedZ) ||
                !CoordinatesMatch(
                    rawX, rawY, rawZ,
                    processedX, processedY, processedZ))
            {
                invalidCount++;
                continue;
            }

            var rowIndex = MapRowIndex(sourceRow, isLeftHalf, forward);
            var rawIntensity = GetReflection(raw.Reflections, sourceRow);
            var processedIntensity = GetReflection(processed.Reflections, sourceRow);

            destination.Add(new UnifiedScanPoint
            {
                X = processedX * xSign,
                Y = scanColumnY,
                Z = processedZ,
                RawIntensity = rawIntensity,
                ProcessedIntensity = (float)Math.Clamp(processedIntensity / 255.0, 0.0, 1.0),
                RowIndex = checked((uint)rowIndex),
                ColumnIndex = checked((uint)effectiveColumn)
            });
        }

        return invalidCount;
    }

    private int MapRowIndex(int sourceRow, bool isLeftHalf, bool forward)
    {
        if (isLeftHalf)
        {
            return forward
                ? halfScanRows + sourceRow
                : halfScanRows - sourceRow - 1;
        }

        return forward
            ? halfScanRows - sourceRow - 1
            : halfScanRows + sourceRow;
    }

    private static double GetPosition(Array values, int row, int component) =>
        Convert.ToDouble(values.GetValue(row * 3 + component), CultureInfo.InvariantCulture);

    private static double GetReflection(Array values, int row) =>
        Convert.ToDouble(values.GetValue(row), CultureInfo.InvariantCulture);

    private static bool CoordinatesAreValid(double x, double y, double z) =>
        double.IsFinite(x) &&
        double.IsFinite(y) &&
        double.IsFinite(z) &&
        !(x == 0.0 && y == 0.0 && z == 0.0);

    private static bool CoordinatesMatch(
        double rawX,
        double rawY,
        double rawZ,
        double processedX,
        double processedY,
        double processedZ)
    {
        const double tolerance = 1e-6;
        return Math.Abs(rawX - processedX) <= tolerance &&
               Math.Abs(rawY - processedY) <= tolerance &&
               Math.Abs(rawZ - processedZ) <= tolerance;
    }

    private static SourceMetadata ReadSourceMetadata(
        IiQLibIf sdk,
        string sourcePath)
    {
        var packageValues = ReadPackageMetadata(sourcePath);
        var scannerModel = FirstNonEmpty(
            packageValues.GetValueOrDefault("ScannerType"),
            packageValues.GetValueOrDefault("ScannerModel"));
        var scannerSerialNumber = CleanMetadataValue(
            packageValues.GetValueOrDefault("ScannerSerNo"));
        var scannerFirmware = FirstNonEmpty(
            packageValues.GetValueOrDefault("FirmwareVersion"),
            packageValues.GetValueOrDefault("ScannerFirmware"));
        var projectName = FirstNonEmpty(
            packageValues.GetValueOrDefault("ScanFileName"),
            packageValues.GetValueOrDefault("ScanProject"));
        var scanDataName = FirstNonEmpty(
            packageValues.GetValueOrDefault("ScanFileName"),
            Path.GetFileNameWithoutExtension(sourcePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)));
        var location = FirstNonEmpty(
            packageValues.GetValueOrDefault("Location"),
            packageValues.GetValueOrDefault("SiteName"),
            packageValues.GetValueOrDefault("Place"));
        DateTime? acquisitionDate = ParsePackageAcquisitionDate(
            packageValues.GetValueOrDefault("Time"));

        IiQObjectIf? scanObject = null;
        try
        {
            scanObject = sdk.getScanObject(0);
            if (scanObject is IiQScanObjIf scanInfo)
            {
                scanInfo.getHardwareInfo(
                    out _,
                    out var hardwareType,
                    out var hardwareSerial,
                    out _,
                    out _);
                scannerModel = FirstNonEmpty(hardwareType, scannerModel);
                scannerSerialNumber = FirstNonEmpty(
                    hardwareSerial,
                    scannerSerialNumber);

                scanInfo.getDate(
                    out var year,
                    out var month,
                    out var day,
                    out var hour,
                    out var minute,
                    out var second,
                    out var millisecond);
                if (TryCreateAcquisitionDate(
                        year,
                        month,
                        day,
                        hour,
                        minute,
                        second,
                        millisecond,
                        out var parsedDate))
                {
                    acquisitionDate = parsedDate;
                }
            }
        }
        catch (COMException)
        {
            // 元数据是可选信息；SDK 不支持时不能阻断点云转换。
        }
        catch (InvalidCastException)
        {
            // 某些旧 FLS 只暴露基础扫描对象，继续使用包内参数。
        }
        finally
        {
            if (scanObject is not null && Marshal.IsComObject(scanObject))
            {
                Marshal.FinalReleaseComObject(scanObject);
            }
        }

        return new SourceMetadata(
            CleanMetadataValue(scannerModel),
            CleanMetadataValue(scannerSerialNumber),
            CleanMetadataValue(scannerFirmware),
            acquisitionDate,
            CleanMetadataValue(scanDataName),
            CleanMetadataValue(projectName),
            CleanMetadataValue(location),
            ScannerParameterCatalog.ResolveFaro(scannerModel, packageValues));
    }

    private static Dictionary<string, string> ReadPackageMetadata(string sourcePath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(sourcePath))
        {
            return values;
        }

        var candidates = new[]
        {
            Path.Combine(sourcePath, "Scans", "1", "Params"),
            Path.Combine(sourcePath, "Main")
        };
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                ReadParameterFile(candidate, values);
            }
            catch (IOException)
            {
                // SDK 元数据仍可能可用。
            }
            catch (UnauthorizedAccessException)
            {
                // 元数据读取失败不能影响扫描点读取。
            }
        }

        return values;
    }

    private static void ReadParameterFile(
        string path,
        IDictionary<string, string> destination)
    {
        var lines = File.ReadAllLines(path);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!TryReadQuotedToken(line, "Name", out var key) ||
                destination.ContainsKey(key))
            {
                continue;
            }

            for (var valueIndex = index + 1;
                 valueIndex < lines.Length && valueIndex <= index + 6;
                 valueIndex++)
            {
                var valueLine = lines[valueIndex].Trim();
                if (valueLine.StartsWith("Name ", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                if (TryReadQuotedToken(valueLine, "Value", out var value))
                {
                    destination[key] = value;
                    break;
                }
                if (TryReadUnquotedValue(valueLine, out value))
                {
                    destination[key] = value;
                    break;
                }
            }
        }
    }

    private static bool TryReadUnquotedValue(string line, out string value)
    {
        value = string.Empty;
        const string prefix = "Value ";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = line[prefix.Length..].Trim();
        if (candidate.Length == 0 || candidate.StartsWith('{'))
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryReadQuotedToken(
        string line,
        string token,
        out string value)
    {
        value = string.Empty;
        var prefix = token + " \"";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !line.EndsWith('"') ||
            line.Length < prefix.Length + 1)
        {
            return false;
        }

        value = line[prefix.Length..^1];
        return true;
    }

    private static bool TryCreateAcquisitionDate(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int millisecond,
        out DateTime value)
    {
        value = default;
        try
        {
            value = new DateTime(
                year,
                month,
                day,
                hour,
                minute,
                second,
                millisecond,
                DateTimeKind.Unspecified);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static DateTime? ParsePackageAcquisitionDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats =
        [
            "d.M.yyyy H:mm:ss:fff",
            "dd.MM.yyyy HH:mm:ss:fff",
            "d.M.yyyy H:mm:ss",
            "dd.MM.yyyy HH:mm:ss"
        ];
        return DateTime.TryParseExact(
            value.Trim(),
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(CleanMetadataValue).FirstOrDefault(value => value is not null);

    private static string? CleanMetadataValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => DisposeLibrary();

    private void DisposeLibrary()
    {
        if (library is null)
        {
            return;
        }

        try
        {
            library.unloadScan(0);
        }
        catch
        {
            // SDK 释放失败不应覆盖原始转换异常。
        }

        if (Marshal.IsComObject(library))
        {
            Marshal.FinalReleaseComObject(library);
        }

        library = null;
        options = null;
        halfScanRows = 0;
    }

    private sealed record HalfScanData(Array Positions, Array Reflections);

    private sealed record SourceMetadata(
        string? ScannerModel,
        string? ScannerSerialNumber,
        string? ScannerFirmware,
        DateTime? AcquisitionDate,
        string? ScanDataName,
        string? ProjectName,
        string? Location,
        ScanAcquisitionMetadata? AcquisitionParameters);
}
