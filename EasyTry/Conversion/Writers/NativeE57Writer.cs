using System.Runtime.InteropServices;
using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Writers;

public readonly record struct E57ValidationResult(
    ulong PointCount,
    uint FieldMask,
    bool HasAllRequiredFields);

/// <summary>
/// 通过 libE57Format 原生桥接层流式写入真实 ASTM E57 文件。
/// 写入期间仅创建同目录临时文件；完成并回读校验后才替换正式输出。
/// </summary>
public sealed class NativeE57Writer : IPointCloudWriter
{
    private const int ScanLinesPerBatch = 64;
    private IntPtr handle;
    private string? temporaryPath;
    private string? outputPath;
    private long writtenPointCount;
    private bool completed;

    public static string? GetAvailabilityError()
    {
        var libraryPath = Path.Combine(AppContext.BaseDirectory, "EasyTryE57Native.dll");
        if (!File.Exists(libraryPath))
        {
            return $"缺少真实 E57 写入组件：{libraryPath}";
        }

        try
        {
            if (!NativeLibrary.TryLoad(libraryPath, out var libraryHandle))
            {
                return "无法加载 EasyTryE57Native.dll，请确认程序以 x64 运行。";
            }

            NativeLibrary.Free(libraryHandle);
            return null;
        }
        catch (Exception ex)
        {
            return $"E57 写入组件加载失败：{ex.Message}";
        }
    }

    public static E57ValidationResult ValidateFile(string filePath, bool readAllPoints)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (NativeE57Methods.easytry_e57_validate(
                fullPath,
                readAllPoints ? 1 : 0,
                out var pointCount,
                out var fieldMask) == 0)
        {
            throw CreateNativeException("E57 回读校验失败");
        }

        return new E57ValidationResult(
            pointCount,
            fieldMask,
            (fieldMask & NativeE57Methods.RequiredFieldMask) ==
            NativeE57Methods.RequiredFieldMask);
    }

    public void Open(ScanMetadata metadata, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfOpen();

        if (metadata.ScanRows <= 0 || metadata.ScanColumns <= 0 ||
            metadata.ScanColumns > uint.MaxValue)
        {
            throw new InvalidOperationException("扫描矩阵尺寸超出 E57 索引字段支持范围。");
        }

        outputPath = Path.GetFullPath(options.OutputFilePath);
        var outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("E57 输出目录无效。");
        Directory.CreateDirectory(outputDirectory);

        temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.writing");

        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
        {
            throw new NotSupportedException(availabilityError);
        }

        var descriptionFields = new List<string>
        {
            "EasyTry research conversion",
            $"vendor={metadata.Vendor}",
            "coordinateSchema=easytry-scan-column-v1",
            "longitudinalCoordinate=scanColumnIndex",
            "cartesianYUnit=scanColumn",
            $"globalColumnStart={metadata.GlobalColumnOffset}",
            $"logicalTotalColumns={(
                metadata.LogicalTotalScanColumns > 0
                    ? metadata.LogicalTotalScanColumns
                    : metadata.ScanColumns)}"
        };
        AddDescriptionField(descriptionFields, "scannerModel", metadata.ScannerModel);
        AddDescriptionField(
            descriptionFields,
            "scannerSerialNumber",
            metadata.ScannerSerialNumber);
        AddDescriptionField(
            descriptionFields,
            "scannerFirmware",
            metadata.ScannerFirmware);
        AddDescriptionField(
            descriptionFields,
            "acquisitionDate",
            metadata.AcquisitionDate?.ToString("yyyy-MM-dd'T'HH:mm:ss.fff"));
        AddDescriptionField(descriptionFields, "projectName", metadata.ProjectName);
        AddDescriptionField(descriptionFields, "location", metadata.Location);
        if (metadata.SourceParts is { Count: > 0 } sourceParts)
        {
            descriptionFields.Add($"continuousSourceCount={sourceParts.Count}");
            foreach (var sourcePart in sourceParts.OrderBy(part => part.SourceIndex))
            {
                descriptionFields.Add(
                    $"source{sourcePart.SourceIndex:D4}=" +
                    $"{Path.GetFileName(sourcePart.SourcePath)}," +
                    $"globalColumns:{sourcePart.EffectiveGlobalColumnStart}-" +
                    sourcePart.EffectiveGlobalColumnEnd);
            }
        }
        var description = string.Join("; ", descriptionFields);
        handle = NativeE57Methods.easytry_e57_create(
            temporaryPath,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            Path.GetFileNameWithoutExtension(metadata.SourceFilePath),
            description,
            checked((uint)metadata.ScanRows),
            checked((uint)metadata.ScanColumns),
            checked((nuint)((long)metadata.ScanRows * ScanLinesPerBatch)));

        if (handle == IntPtr.Zero)
        {
            DeleteTemporaryFile();
            throw CreateNativeException("创建 E57 文件失败");
        }

        writtenPointCount = 0;
        completed = false;
    }

    public void WriteBatch(PointBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        EnsureOpen();
        cancellationToken.ThrowIfCancellationRequested();

        var count = batch.Points.Count;
        if (count == 0)
        {
            return;
        }

        var x = new double[count];
        var y = new double[count];
        var z = new double[count];
        var processedIntensity = new float[count];
        var rawIntensity = new double[count];
        var rowIndex = new uint[count];
        var columnIndex = new uint[count];

        for (var index = 0; index < count; index++)
        {
            var point = batch.Points[index];
            x[index] = point.X;
            y[index] = point.Y;
            z[index] = point.Z;
            processedIntensity[index] = Math.Clamp(point.ProcessedIntensity, 0f, 1f);
            rawIntensity[index] = point.RawIntensity;
            rowIndex[index] = point.RowIndex;
            columnIndex[index] = point.ColumnIndex;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (NativeE57Methods.easytry_e57_write(
                handle,
                x,
                y,
                z,
                processedIntensity,
                rawIntensity,
                rowIndex,
                columnIndex,
                checked((nuint)count)) == 0)
        {
            throw CreateNativeException("写入 E57 点批次失败");
        }

        writtenPointCount += count;
    }

    public void Complete(ConversionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        EnsureOpen();

        if (writtenPointCount != report.ValidPointCount)
        {
            throw new InvalidOperationException(
                $"E57 已写入点数 {writtenPointCount:N0} 与统计有效点数 " +
                $"{report.ValidPointCount:N0} 不一致。");
        }

        if (NativeE57Methods.easytry_e57_complete(handle) == 0)
        {
            throw CreateNativeException("关闭 E57 文件失败");
        }

        var validation = ValidateFile(temporaryPath!, readAllPoints: false);

        if (validation.PointCount != checked((ulong)writtenPointCount))
        {
            throw new InvalidDataException(
                $"E57 回读点数 {validation.PointCount:N0} 与写入点数 " +
                $"{writtenPointCount:N0} 不一致。");
        }

        if (!validation.HasAllRequiredFields)
        {
            throw new InvalidDataException(
                $"E57 回读字段不完整，字段掩码为 0x{validation.FieldMask:X2}。");
        }

        ReleaseNativeHandle();
        File.Move(temporaryPath!, outputPath!, overwrite: true);
        temporaryPath = null;
        completed = true;
    }

    private static void AddDescriptionField(
        ICollection<string> fields,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var safeValue = value
            .Replace(';', ',')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (safeValue.Length > 0)
        {
            fields.Add($"{name}={safeValue}");
        }
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            if (!completed)
            {
                NativeE57Methods.easytry_e57_cancel(handle);
            }

            ReleaseNativeHandle();
        }

        if (!completed)
        {
            DeleteTemporaryFile();
        }
    }

    private void EnsureOpen()
    {
        if (handle == IntPtr.Zero || completed)
        {
            throw new InvalidOperationException("E57 写入器尚未打开或已经关闭。");
        }
    }

    private void ThrowIfOpen()
    {
        if (handle != IntPtr.Zero)
        {
            throw new InvalidOperationException("E57 写入器已经打开。");
        }
    }

    private static InvalidOperationException CreateNativeException(string operation) =>
        new($"{operation}：{NativeE57Methods.GetLastError()}");

    private void ReleaseNativeHandle()
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeE57Methods.easytry_e57_destroy(handle);
        handle = IntPtr.Zero;
    }

    private void DeleteTemporaryFile()
    {
        if (temporaryPath is null)
        {
            return;
        }

        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch
        {
            // 保留原始转换异常；下次启动或人工清理时可删除 .writing 文件。
        }
    }
}
