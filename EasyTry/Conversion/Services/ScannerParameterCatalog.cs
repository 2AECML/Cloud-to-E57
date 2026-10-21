using System.Globalization;
using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

/// <summary>
/// 扫描仪型号参数表。文件内参数优先；型号表用于把厂商档位转换成
/// 每圈采样数、名义转速等可计算参数。新增型号时只需扩充这里的表项。
/// </summary>
internal static class ScannerParameterCatalog
{
    private static readonly IReadOnlyDictionary<string, int> FaroResolutionPoints =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["1/1"] = 40960,
            ["1/2"] = 20480,
            ["1/4"] = 10240,
            ["1/5"] = 8192,
            ["1/8"] = 5120,
            ["1/10"] = 4096,
            ["1/16"] = 2560,
            ["1/20"] = 2048,
            ["1/32"] = 1280
        };

    private static readonly IReadOnlyDictionary<string, double> FaroQualityPointRates =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["1x"] = 976562.5,
            ["2x"] = 488281.25,
            ["3x"] = 244140.625,
            ["4x"] = 122070.3125
        };

    private static readonly ZfModelTable[] ZfTables =
    [
        new(
            "Z+F PROFILER 9012系列参数表",
            ["9012"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["l"] = 5120,
                ["h"] = 10240,
                ["sh"] = 20480
            },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["l"] = 200,
                ["n"] = 100,
                ["h"] = 50
            }),
        new(
            "Z+F PROFILER 9020系列参数表",
            ["9020"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["20000"] = 20480,
                ["10000"] = 10240,
                ["8000"] = 8192,
                ["6000"] = 6827,
                ["5000"] = 5120,
                ["4000"] = 4096,
                ["3000"] = 3413,
                ["2000"] = 2048,
                ["1000"] = 1024,
                ["sh"] = 20480,
                ["h"] = 10240,
                ["l"] = 5120
            },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["260"] = 267,
                ["210"] = 214,
                ["160"] = 160,
                ["130"] = 134,
                ["100"] = 107,
                ["80"] = 80,
                ["60"] = 67,
                ["50"] = 53,
                ["l"] = 214,
                ["n"] = 107,
                ["h"] = 53
            }),
        new(
            "Z+F 5000系列参数表",
            ["5000"],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["l"] = 5120,
                ["h"] = 10240,
                ["sh"] = 20480
            },
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["l"] = 55,
                ["n"] = 27,
                ["h"] = 14
            })
    ];

    public static ScanAcquisitionMetadata ResolveFaro(
        string? scannerModel,
        IReadOnlyDictionary<string, string> parameters)
    {
        var pointRate = TryGetDouble(parameters, "MeasurementRate");
        var rowCount = TryGetInt(parameters, "NumRows");
        var startAngle = TryGetDouble(parameters, "RowStartAngle");
        var endAngle = TryGetDouble(parameters, "RowEndAngle");

        int? estimatedPointsPerRevolution = null;
        if (rowCount is > 0 && startAngle.HasValue && endAngle.HasValue)
        {
            var angleSpan = Math.Abs(startAngle.Value - endAngle.Value);
            if (angleSpan > 0)
            {
                estimatedPointsPerRevolution = checked((int)Math.Round(
                    rowCount.Value * Math.Tau / angleSpan));
            }
        }

        var resolution = FindClosest(
            FaroResolutionPoints,
            estimatedPointsPerRevolution,
            relativeTolerance: 0.03);
        var pointsPerRevolution = resolution?.Value ?? estimatedPointsPerRevolution;
        var quality = FindClosest(
            FaroQualityPointRates,
            pointRate,
            relativeTolerance: 0.02);
        var revolutionsPerSecond = Divide(pointRate, pointsPerRevolution);

        return new ScanAcquisitionMetadata(
            resolution is null
                ? FormatDerivedResolution(pointsPerRevolution)
                : $"{resolution.Value.Key}（{resolution.Value.Value:N0}点/圈）",
            BuildFaroResolutionRaw(rowCount, startAngle, endAngle),
            quality?.Key ?? FormatUnknownQuality(pointRate),
            pointRate.HasValue
                ? $"每秒采集点数={pointRate.Value.ToString("0.###", CultureInfo.InvariantCulture)}"
                : null,
            pointRate,
            pointsPerRevolution,
            null,
            revolutionsPerSecond,
            BuildCalculation(pointRate, pointsPerRevolution),
            IsFaroFocus(scannerModel)
                ? "FARO Focus系列参数表"
                : "FARO通用文件参数推算表",
            "FLS包内Params参数；分辨率由扫描角度与行数匹配型号表，质量由测量率匹配型号表");
    }

    public static ScanAcquisitionMetadata ResolveZf(
        string? scannerModel,
        string? resolutionRaw,
        string? qualityRaw,
        double? pointRatePerSecond,
        int pixelsPerProfile)
    {
        var table = ZfTables.FirstOrDefault(candidate =>
            candidate.ModelTokens.Any(token =>
                scannerModel?.Contains(token, StringComparison.OrdinalIgnoreCase) == true));
        var pointsFromTable = Lookup(table?.ResolutionPoints, resolutionRaw);
        var pointsPerRevolution = pixelsPerProfile > 0
            ? pixelsPerProfile
            : pointsFromTable;
        var nominalRate = Lookup(table?.QualityRevolutionsPerSecond, qualityRaw);
        var calculatedRate = Divide(pointRatePerSecond, pointsPerRevolution)
            ?? nominalRate;

        return new ScanAcquisitionMetadata(
            FormatZfResolution(resolutionRaw, pointsPerRevolution),
            resolutionRaw,
            FormatZfQuality(qualityRaw, nominalRate),
            qualityRaw,
            pointRatePerSecond,
            pointsPerRevolution,
            nominalRate,
            calculatedRate,
            BuildCalculation(pointRatePerSecond, pointsPerRevolution),
            table?.Name ?? "Z+F通用文件参数推算表",
            "ZFS文件头、每线像素数及内嵌扫描命令");
    }

    private static string? BuildFaroResolutionRaw(
        int? rowCount,
        double? startAngle,
        double? endAngle)
    {
        if (!rowCount.HasValue)
        {
            return null;
        }

        if (!startAngle.HasValue || !endAngle.HasValue)
        {
            return $"半断面行数={rowCount.Value}";
        }

        var spanDegrees = Math.Abs(startAngle.Value - endAngle.Value) * 180.0 / Math.PI;
        return $"半断面行数={rowCount.Value}；扫描角度={spanDegrees:0.###}度";
    }

    private static string? BuildCalculation(double? pointRate, int? pointsPerRevolution) =>
        pointRate.HasValue && pointsPerRevolution is > 0
            ? $"{pointRate.Value:0.###}点/秒 ÷ {pointsPerRevolution.Value}点/圈"
            : null;

    private static double? Divide(double? numerator, int? denominator) =>
        numerator is > 0 && denominator is > 0
            ? numerator.Value / denominator.Value
            : null;

    private static KeyValuePair<string, int>? FindClosest(
        IReadOnlyDictionary<string, int> values,
        int? target,
        double relativeTolerance)
    {
        if (target is not > 0)
        {
            return null;
        }

        var closest = values.MinBy(entry => Math.Abs(entry.Value - target.Value));
        return Math.Abs(closest.Value - target.Value) / (double)closest.Value <= relativeTolerance
            ? closest
            : null;
    }

    private static KeyValuePair<string, double>? FindClosest(
        IReadOnlyDictionary<string, double> values,
        double? target,
        double relativeTolerance)
    {
        if (target is not > 0)
        {
            return null;
        }

        var closest = values.MinBy(entry => Math.Abs(entry.Value - target.Value));
        return Math.Abs(closest.Value - target.Value) / closest.Value <= relativeTolerance
            ? closest
            : null;
    }

    private static int? Lookup(
        IReadOnlyDictionary<string, int>? values,
        string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        values?.TryGetValue(key.Trim(), out var value) == true
            ? value
            : null;

    private static double? Lookup(
        IReadOnlyDictionary<string, double>? values,
        string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        values?.TryGetValue(key.Trim(), out var value) == true
            ? value
            : null;

    private static double? TryGetDouble(
        IReadOnlyDictionary<string, string> parameters,
        string name) =>
        parameters.TryGetValue(name, out var text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? TryGetInt(
        IReadOnlyDictionary<string, string> parameters,
        string name) =>
        parameters.TryGetValue(name, out var text) &&
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool IsFaroFocus(string? model) =>
        model?.Contains("Focus", StringComparison.OrdinalIgnoreCase) == true;

    private static string? FormatDerivedResolution(int? pointsPerRevolution) =>
        pointsPerRevolution is > 0
            ? $"文件推算（{pointsPerRevolution.Value:N0}点/圈）"
            : null;

    private static string? FormatUnknownQuality(double? pointRate) =>
        pointRate is > 0
            ? $"文件测量率档（{pointRate.Value:N0}点/秒）"
            : null;

    private static string? FormatZfResolution(string? raw, int? points) =>
        points is > 0
            ? $"{TranslateZfResolution(raw)}（{points.Value:N0}点/圈）"
            : TranslateZfResolution(raw);

    private static string? FormatZfQuality(string? raw, double? nominalRate) =>
        nominalRate is > 0
            ? $"{TranslateZfQuality(raw)}（名义{nominalRate.Value:0.###}圈/秒）"
            : TranslateZfQuality(raw);

    private static string? TranslateZfResolution(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "l" => "低",
            "h" => "高",
            "sh" => "超高",
            { Length: > 0 } value => value,
            _ => null
        };

    private static string? TranslateZfQuality(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "l" => "低",
            "n" => "普通",
            "h" => "高",
            { Length: > 0 } value => value,
            _ => null
        };

    private sealed record ZfModelTable(
        string Name,
        string[] ModelTokens,
        IReadOnlyDictionary<string, int> ResolutionPoints,
        IReadOnlyDictionary<string, double> QualityRevolutionsPerSecond);
}
