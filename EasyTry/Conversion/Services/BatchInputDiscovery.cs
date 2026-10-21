using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

/// <summary>
/// Discovers vendor-specific raw scan inputs without mixing SDK formats.
/// </summary>
public static class BatchInputDiscovery
{
    public static IReadOnlyList<string> NormalizeZfsFiles(
        IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        return filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => string.Equals(
                Path.GetExtension(path),
                ".zfs",
                StringComparison.OrdinalIgnoreCase))
            .Select(ZfsVolumeSet.GetPrimaryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> DiscoverFaroScanFolders(
        string selectedDirectory)
    {
        if (string.IsNullOrWhiteSpace(selectedDirectory))
        {
            return [];
        }

        var root = Path.GetFullPath(selectedDirectory);
        if (!Directory.Exists(root))
        {
            return [];
        }

        if (IsSupportedFaroInput(root))
        {
            return [root];
        }

        var fileInputs = Directory
            .EnumerateFiles(root, "*.fls", SearchOption.TopDirectoryOnly);
        var packageInputs = Directory
            .EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(IsCompleteFaroScanFolder);
        return fileInputs
            .Concat(packageInputs)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> NormalizeFaroFiles(
        IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        return filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(IsSupportedFaroInput)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static bool IsSupportedFaroInput(string inputPath) =>
        string.Equals(
            Path.GetExtension(inputPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)),
            ".fls",
            StringComparison.OrdinalIgnoreCase) &&
        (File.Exists(inputPath) || IsCompleteFaroScanFolder(inputPath));

    public static bool IsCompleteFaroScanFolder(string directoryPath)
    {
        if (!Directory.Exists(directoryPath) ||
            !string.Equals(
                Path.GetExtension(directoryPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)),
                ".fls",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mainPath = Path.Combine(directoryPath, "Main");
        var scansPath = Path.Combine(directoryPath, "Scans");
        if (!File.Exists(mainPath) || !Directory.Exists(scansPath))
        {
            return false;
        }

        try
        {
            return Directory
                .EnumerateFiles(
                    scansPath,
                    "ScanDataStream*",
                    SearchOption.AllDirectories)
                .Any();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string BuildOutputFilePath(
        string outputDirectory,
        string inputPath)
    {
        var normalizedInput = inputPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var name = Path.GetFileNameWithoutExtension(normalizedInput);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("无法从输入路径生成输出文件名。", nameof(inputPath));
        }

        return Path.Combine(Path.GetFullPath(outputDirectory), name + ".e57");
    }

    public static ScannerVendor ValidateSingleVendor(
        IReadOnlyCollection<string> inputPaths,
        ScannerVendor selectedVendor)
    {
        if (inputPaths.Count == 0)
        {
            throw new InvalidOperationException("请先选择至少一个原始扫描输入。");
        }

        var detected = inputPaths
            .Select(ConversionPrecheckService.DetectVendor)
            .Distinct()
            .ToArray();
        if (detected.Length != 1 || detected[0] == ScannerVendor.Auto)
        {
            throw new InvalidOperationException(
                "同一批任务只能包含一种厂商格式：Z+F .zfs 或 FARO .fls 扫描包。");
        }
        if (selectedVendor != ScannerVendor.Auto && selectedVendor != detected[0])
        {
            throw new InvalidOperationException("选择的厂商与批量输入格式不一致。");
        }

        return detected[0];
    }
}
