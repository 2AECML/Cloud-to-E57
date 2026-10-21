using System.Text;
using System.Globalization;
using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

internal static class ScanMetadataDocumentWriter
{
    public static string Write(
        ConversionReport report,
        ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(options);

        var path = GetOutputPath(options, report.ResolvedOutputMode);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("扫描说明文件输出目录无效。");
        Directory.CreateDirectory(directory);

        var metadata = report.Metadata;
        var acquisition = metadata.AcquisitionParameters;
        var tlsd = TlsdProjectMetadataReader.Read(options);
        var content = new StringBuilder()
            .AppendLine("扫描数据说明")
            .AppendLine("============")
            .AppendLine($"扫描仪厂商：{GetVendorName(metadata.Vendor)}")
            .AppendLine($"扫描仪型号：{ValueOrUnknown(metadata.ScannerModel)}")
            .AppendLine($"扫描数据名称：{ValueOrUnknown(GetScanDataName(metadata))}")
            .AppendLine($"扫描项目名称或地点：{ValueOrUnknown(GetProjectOrLocation(metadata))}")
            .AppendLine($"分辨率：{ValueOrUnknown(acquisition?.Resolution)}")
            .AppendLine($"质量：{ValueOrUnknown(acquisition?.Quality)}")
            .AppendLine()
            .AppendLine("每秒圈数计算数据")
            .AppendLine("----------------")
            .AppendLine($"每秒采集点数：{FormatRate(acquisition?.PointRatePerSecond, "点/秒")}")
            .AppendLine($"每圈采样点数：{FormatInteger(acquisition?.PointsPerRevolution, "点/圈")}")
            .AppendLine($"名义每秒圈数：{FormatRate(acquisition?.NominalRevolutionsPerSecond, "圈/秒")}")
            .AppendLine($"计算公式：{ValueOrUnknown(acquisition?.Calculation)}")
            .AppendLine($"计算得到的每秒圈数：{FormatRate(acquisition?.CalculatedRevolutionsPerSecond, "圈/秒")}")
            .AppendLine()
            .AppendLine("TLSD 项目参数")
            .AppendLine("-------------")
            .AppendLine($"小车速度：{FormatRate(tlsd.Speed, "m/s")}")
            .AppendLine($"环宽：{FormatRate(tlsd.RingWidth, "m")}")
            .AppendLine($"内径：{FormatRate(tlsd.InnerDiameter, "m")}")
            .AppendLine($"TLSD路径：{ValueOrUnknown(tlsd.FilePath)}")
            .AppendLine($"TLSD参数读取状态：{tlsd.Status}")
            .AppendLine()
            .AppendLine("参数说明")
            .AppendLine("--------")
            .AppendLine($"分辨率原始值：{ValueOrUnknown(acquisition?.ResolutionRaw)}")
            .AppendLine($"质量原始值：{ValueOrUnknown(acquisition?.QualityRaw)}")
            .AppendLine($"采用的型号参数表：{ValueOrUnknown(acquisition?.ParameterTable)}")
            .AppendLine($"参数来源：{ValueOrUnknown(acquisition?.ParameterSource)}")
            .AppendLine($"原始扫描路径：{metadata.SourceFilePath}")
            .ToString();

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.Move(temporaryPath, path, overwrite: true);
        return path;
    }

    public static string GetOutputPath(
        ConversionOptions options,
        E57OutputMode resolvedOutputMode)
    {
        if (resolvedOutputMode == E57OutputMode.SegmentedDataset)
        {
            return Path.Combine(
                ConversionOutputLayout.GetDatasetDirectory(options.OutputFilePath),
                "扫描说明.txt");
        }

        var fullOutputPath = Path.GetFullPath(options.OutputFilePath);
        var directory = Path.GetDirectoryName(fullOutputPath)
            ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(fullOutputPath);
        return Path.Combine(directory, $"{stem}_扫描说明.txt");
    }

    private static string GetVendorName(ScannerVendor vendor) => vendor switch
    {
        ScannerVendor.ZPlusF => "Z+F",
        ScannerVendor.Faro => "FARO",
        _ => "未知"
    };

    private static string? GetScanDataName(ScanMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ScanDataName))
        {
            return metadata.ScanDataName;
        }

        var normalized = metadata.SourceFilePath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return Path.GetFileNameWithoutExtension(normalized);
    }

    private static string? GetProjectOrLocation(ScanMetadata metadata)
    {
        var values = new[] { metadata.ProjectName, metadata.Location }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? null : string.Join(" / ", values);
    }

    private static string ValueOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "未读取到" : value.Trim();

    private static string FormatRate(double? value, string unit) =>
        value is > 0 && double.IsFinite(value.Value)
            ? $"{value.Value.ToString("0.######", CultureInfo.InvariantCulture)} {unit}"
            : "未读取到";

    private static string FormatInteger(int? value, string unit) =>
        value is > 0 ? $"{value.Value:N0} {unit}" : "未读取到";
}
