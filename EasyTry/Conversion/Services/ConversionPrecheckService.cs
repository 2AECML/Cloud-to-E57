using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

/// <summary>
/// 执行不依赖厂商 SDK 的基础输入检查。
/// </summary>
public sealed class ConversionPrecheckService
{
    public PrecheckResult Check(ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.InputFilePath))
        {
            var emptyResult = new PrecheckResult();
            emptyResult.Errors.Add("请选择 Z+F（.zfs）扫描文件或 FARO（.fls）原始扫描文件夹。");
            return emptyResult;
        }

        string inputPath;
        try
        {
            inputPath = Path.GetFullPath(options.InputFilePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            var invalidPathResult = new PrecheckResult();
            invalidPathResult.Errors.Add($"输入路径无效：{ex.Message}");
            return invalidPathResult;
        }

        var detectedVendor = DetectVendor(inputPath);
        var result = new PrecheckResult { DetectedVendor = detectedVendor };

        IReadOnlyList<string> continuousInputs = [];
        if (options.ContinuousInputPaths.Count > 0)
        {
            try
            {
                continuousInputs = ContinuousFaroInputSet.NormalizeAndValidate(
                    options.ContinuousInputPaths);
                if (detectedVendor != ScannerVendor.Faro ||
                    options.Vendor is not (ScannerVendor.Auto or ScannerVendor.Faro))
                {
                    result.Errors.Add("连续多输入合并当前只支持 FARO .fls 扫描包。");
                }
                else
                {
                    result.Information.Add(
                        $"连续 FARO 扫描组：{continuousInputs.Count:N0} 个输入，将按数字编号顺序合并。");
                }
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or ArgumentException)
            {
                result.Errors.Add($"连续 FARO 扫描组检查失败：{ex.Message}");
            }
        }

        var inputIsFile = File.Exists(inputPath);
        var inputIsDirectory = Directory.Exists(inputPath);
        ZfsVolumeSet? zfsVolumeSet = null;
        if (!inputIsFile && !inputIsDirectory)
        {
            result.Errors.Add($"输入文件或文件夹不存在：{inputPath}");
            return result;
        }

        if (detectedVendor == ScannerVendor.Auto)
        {
            result.Errors.Add("无法识别输入类型。仅支持 .zfs 文件和名称以 .fls 结尾的 FARO 原始扫描文件夹。");
        }
        else
        {
            result.Information.Add($"已识别设备类型：{GetVendorDisplayName(detectedVendor)}");
        }

        if (options.Vendor != ScannerVendor.Auto &&
            detectedVendor != ScannerVendor.Auto &&
            options.Vendor != detectedVendor)
        {
            result.Errors.Add("页面选择的设备类型与输入路径扩展名不一致。");
        }

        if (detectedVendor == ScannerVendor.ZPlusF && !inputIsFile)
        {
            result.Errors.Add("Z+F 输入必须是 .zfs 扫描文件，不能选择文件夹。");
        }
        else if (detectedVendor == ScannerVendor.ZPlusF)
        {
            try
            {
                zfsVolumeSet = ZfsVolumeSet.Resolve(inputPath);
                if (zfsVolumeSet.IsPartialVolumeChain)
                {
                    result.Warnings.Add(
                        "所选文件是缺少主卷或相邻卷的 Z+F 续卷；程序将读取当前可访问的数据，但输出不代表完整扫描。");
                }
                else
                {
                    result.Information.Add(zfsVolumeSet.IsMultipart
                        ? $"已识别 Z+F 分卷扫描：1 个主卷 + {zfsVolumeSet.VolumePaths.Count - 1} 个续卷；将合并为一个 E57，并跳过分卷间重复数据。"
                        : "Z+F 单文件扫描结构检查通过。");
                }
            }
            catch (Exception ex) when (
                ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                result.Errors.Add($"Z+F 分卷检查失败：{ex.Message}");
            }
        }

        if (detectedVendor == ScannerVendor.Faro)
        {
            if (!inputIsFile && !inputIsDirectory)
            {
                result.Errors.Add("FARO 输入必须选择完整的 .fls 原始扫描文件夹，不能选择单个文件。");
            }
            else if (inputIsDirectory)
            {
                ValidateFaroScanFolder(inputPath, result);
            }
            else
            {
                result.Information.Add("FARO .fls 原始文件检查通过。");
            }
        }

        foreach (var additionalInput in continuousInputs.Skip(1))
        {
            if (!BatchInputDiscovery.IsSupportedFaroInput(additionalInput))
            {
                result.Errors.Add($"连续 FARO 输入无效：{additionalInput}");
            }
        }

        if (options.OutputMode is E57OutputMode.SegmentedDataset or
            E57OutputMode.AutoByScanColumnThreshold)
        {
            if (options.SegmentScanColumns < 5_000)
            {
                result.Errors.Add("连续分段模式下，每段扫描列数不能少于 5,000。");
            }
            else if (options.SegmentScanColumns > 1_000_000)
            {
                result.Errors.Add("连续分段模式下，每段扫描列数不能超过 1,000,000。");
            }
            else
            {
                if (options.OutputMode == E57OutputMode.AutoByScanColumnThreshold)
                {
                    result.Information.Add(
                        $"E57 输出模式：总扫描列不超过 {options.SegmentScanColumns:N0} 时生成单个 E57，超过时按该阈值连续分段。");
                }
                else
                {
                    result.Information.Add(
                        $"E57 输出模式：连续分段数据集，每段最多 {options.SegmentScanColumns:N0} 列。");
                }
                if (Directory.Exists(
                        ConversionOutputLayout.GetDatasetDirectory(options.OutputFilePath)))
                {
                    result.Warnings.Add(
                        "分段数据集目录已经存在；参数一致时将复用已完成分段，否则会要求使用新的输出名称。");
                }
            }
        }
        else
        {
            result.Information.Add("E57 输出模式：每个原始扫描生成一个 E57 文件。");
        }

        ValidateOutputPath(options.OutputFilePath, inputPath, result);

        var tlsd = TlsdProjectMetadataReader.Read(options);
        if (tlsd.HasWarning)
        {
            result.Warnings.Add($"TLSD 项目参数：{tlsd.Status}。扫描说明将保留可读参数和读取状态。");
        }
        else
        {
            result.Information.Add($"TLSD 项目参数：{tlsd.Status}" +
                (tlsd.FilePath is null ? "。" : $"：{tlsd.FilePath}"));
        }

        if (inputIsDirectory)
        {
            var directorySize = TryGetDirectorySize(inputPath, out var sizeError);
            if (directorySize.HasValue)
            {
                result.Information.Add($"输入文件夹总大小：{FormatFileSize(directorySize.Value)}");
            }
            else
            {
                result.Warnings.Add($"未能统计输入文件夹大小：{sizeError}");
            }
        }
        else if (zfsVolumeSet is not null)
        {
            try
            {
                var totalBytes = zfsVolumeSet.VolumePaths.Aggregate(
                    0L,
                    (total, path) => checked(total + new FileInfo(path).Length));
                result.Information.Add(
                    $"Z+F 逻辑扫描总大小：{FormatFileSize(totalBytes)}（{zfsVolumeSet.VolumePaths.Count} 个物理卷）");
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or OverflowException)
            {
                result.Warnings.Add($"未能统计 Z+F 分卷总大小：{ex.Message}");
            }
        }
        else
        {
            var inputInfo = new FileInfo(inputPath);
            result.Information.Add($"输入文件大小：{FormatFileSize(inputInfo.Length)}");
        }
        result.Information.Add($"里程方向：{GetMileageDisplayName(options.MileageDirection)}");
        result.Information.Add($"扫描方向：{GetScanDirectionDisplayName(options.ScanDirection)}");
        result.Information.Add("强度策略：同时保留原始强度和 SDK 处理强度。");
        result.Information.Add("纵向坐标策略：cartesianY 保存扫描列号；实际断面间距在正射成图时由小车速度和每秒圈数计算。");
        result.Information.Add("无回波策略：不写入无效点，以 rowIndex/columnIndex 保留稀疏结构。");

        return result;
    }

    private static void ValidateFaroScanFolder(string inputPath, PrecheckResult result)
    {
        var mainPath = Path.Combine(inputPath, "Main");
        var scansPath = Path.Combine(inputPath, "Scans");
        if (!File.Exists(mainPath) || !Directory.Exists(scansPath))
        {
            result.Errors.Add("所选文件夹不是完整的 FARO 原始扫描包：缺少 Main 文件或 Scans 文件夹。");
            return;
        }

        try
        {
            var hasScanData = Directory
                .EnumerateFiles(scansPath, "ScanDataStream*", SearchOption.AllDirectories)
                .Any();
            if (!hasScanData)
            {
                result.Errors.Add("所选 FARO 文件夹中未找到 ScanDataStream 扫描数据。");
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add($"无法检查 FARO 扫描数据目录：{ex.Message}");
            return;
        }

        result.Information.Add("FARO 原始扫描文件夹结构检查通过。");
    }

    private static long? TryGetDirectorySize(string directoryPath, out string? error)
    {
        try
        {
            long totalBytes = 0;
            foreach (var filePath in Directory.EnumerateFiles(
                         directoryPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                totalBytes = checked(totalBytes + new FileInfo(filePath).Length);
            }

            error = null;
            return totalBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static void ValidateOutputPath(string outputFilePath, string inputPath, PrecheckResult result)
    {
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            result.Errors.Add("请选择 E57 输出文件路径。");
            return;
        }

        string outputPath;
        try
        {
            outputPath = Path.GetFullPath(outputFilePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            result.Errors.Add($"输出路径无效：{ex.Message}");
            return;
        }

        if (!string.Equals(Path.GetExtension(outputPath), ".e57", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("输出文件扩展名必须为 .e57。");
        }

        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("输入文件与输出文件不能是同一路径。");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            result.Errors.Add("输出目录无效。");
        }
        else if (!Directory.Exists(outputDirectory))
        {
            result.Warnings.Add($"输出目录尚不存在，正式转换时将尝试创建：{outputDirectory}");
        }

        if (File.Exists(outputPath))
        {
            result.Warnings.Add("输出 E57 已存在。正式转换前将要求确认是否覆盖。");
        }
    }

    public static ScannerVendor DetectVendor(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".zfs" => ScannerVendor.ZPlusF,
            ".fls" => ScannerVendor.Faro,
            _ => ScannerVendor.Auto
        };

    public static string GetVendorDisplayName(ScannerVendor vendor) => vendor switch
    {
        ScannerVendor.ZPlusF => "Z+F",
        ScannerVendor.Faro => "FARO",
        _ => "自动识别"
    };

    public static string GetMileageDisplayName(MileageDirection direction) =>
        direction == MileageDirection.Descending ? "里程降序" : "里程升序";

    public static string GetScanDirectionDisplayName(MobileScanDirection direction) =>
        direction == MobileScanDirection.Reverse ? "移动 2D（反向）" : "移动 2D（正向）";

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }
}
