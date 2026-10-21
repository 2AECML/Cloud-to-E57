using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

public static class ConversionOutputLayout
{
    public static E57OutputMode ResolveOutputMode(
        ConversionOptions options,
        long totalScanColumns) => options.OutputMode switch
    {
        E57OutputMode.AutoByScanColumnThreshold =>
            totalScanColumns <= options.SegmentScanColumns
                ? E57OutputMode.SingleFile
                : E57OutputMode.SegmentedDataset,
        _ => options.OutputMode
    };

    public static string GetDatasetDirectory(string outputFilePath)
    {
        var fullPath = Path.GetFullPath(outputFilePath);
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("E57 输出目录无效。");
        var name = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(parent, $"{name}_dataset");
    }

    public static bool OutputExists(ConversionOptions options) =>
        options.OutputMode switch
        {
            E57OutputMode.SegmentedDataset =>
                Directory.Exists(GetDatasetDirectory(options.OutputFilePath)),
            E57OutputMode.AutoByScanColumnThreshold =>
                File.Exists(options.OutputFilePath) ||
                Directory.Exists(GetDatasetDirectory(options.OutputFilePath)),
            _ => File.Exists(options.OutputFilePath)
        };

    public static string GetDisplayPath(ConversionOptions options) =>
        options.OutputMode == E57OutputMode.SegmentedDataset
            ? GetDatasetDirectory(options.OutputFilePath)
            : Path.GetFullPath(options.OutputFilePath);

    public static string GetDisplayPath(
        ConversionOptions options,
        E57OutputMode resolvedOutputMode) =>
        resolvedOutputMode == E57OutputMode.SegmentedDataset
            ? GetDatasetDirectory(options.OutputFilePath)
            : Path.GetFullPath(options.OutputFilePath);
}
