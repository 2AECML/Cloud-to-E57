using EasyTry.Conversion.Models;
using System.Security.Cryptography;
using System.Text;

namespace EasyTry.Conversion.Services;

public static class ConversionTaskList
{
    public static IReadOnlyList<BatchConversionTask> Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("任务清单不存在。", fullPath);
        }

        var tasks = new List<BatchConversionTask>();
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(fullPath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var columns = rawLine.Split('\t');
            if (columns.Length is not (2 or 3) ||
                string.IsNullOrWhiteSpace(columns[0]) ||
                string.IsNullOrWhiteSpace(columns[1]))
            {
                throw new InvalidDataException(
                    $"任务清单第 {lineNumber} 行必须用 Tab 分隔输入目录、输出目录和可空的 tlsd 路径（兼容旧两列格式）。");
            }

            var inputRoot = Path.GetFullPath(columns[0].Trim().Trim('"'));
            var outputDirectory = Path.GetFullPath(columns[1].Trim().Trim('"'));
            var tlsdValue = columns.Length == 3 ? columns[2].Trim().Trim('"') : null;
            var tlsdPath = string.IsNullOrWhiteSpace(tlsdValue)
                ? null
                : Path.GetFullPath(tlsdValue, Path.GetDirectoryName(fullPath)!);
            if (tlsdPath is not null &&
                !Path.GetExtension(tlsdPath).Equals(".tlsd", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"任务清单第 {lineNumber} 行第三列必须是 .tlsd 文件路径或空值。");
            }
            if (!Directory.Exists(inputRoot))
            {
                throw new DirectoryNotFoundException(
                    $"任务清单第 {lineNumber} 行的输入目录不存在：{inputRoot}");
            }

            var inputs = BatchInputDiscovery.DiscoverFaroScanFolders(inputRoot)
                .Concat(BatchInputDiscovery.NormalizeZfsFiles(
                    Directory.EnumerateFiles(inputRoot, "*.zfs", SearchOption.TopDirectoryOnly)))
                .ToArray();
            if (inputs.Length == 0)
            {
                throw new InvalidDataException(
                    $"任务清单第 {lineNumber} 行没有发现 FARO .fls 文件、完整扫描包或 Z+F .zfs 文件：{inputRoot}");
            }

            tasks.Add(new BatchConversionTask(inputRoot, outputDirectory, inputs)
            {
                TlsdFilePath = tlsdPath
            });
        }

        return tasks.Count > 0
            ? tasks
            : throw new InvalidDataException("任务清单中没有有效任务。");
    }

    public static IReadOnlyList<ConversionOptions> BuildOptions(
        BatchConversionTask task,
        E57OutputMode outputMode,
        MileageDirection mileageDirection,
        MobileScanDirection scanDirection,
        int segmentColumns)
    {
        var faroInputs = BatchInputDiscovery.NormalizeFaroFiles(task.InputPaths);
        var zfsInputs = BatchInputDiscovery.NormalizeZfsFiles(task.InputPaths);
        if (outputMode == E57OutputMode.AutoByScanColumnThreshold)
        {
            if (zfsInputs.Count > 0)
            {
                throw new InvalidOperationException(
                    "清单包含 Z+F 扫描，不能使用“连续 FARO 合并”模式。请选择“单个 E57”或“连续分段 E57 数据集”。");
            }

            var continuousInputs = ContinuousFaroInputSet.NormalizeAndValidate(faroInputs);
            var conventionalPath = ContinuousFaroInputSet.BuildOutputFilePath(
                task.OutputDirectory, continuousInputs);
            return
            [
                new ConversionOptions
                {
                    InputFilePath = continuousInputs[0],
                    TlsdFilePath = task.TlsdFilePath,
                    ContinuousInputPaths = continuousInputs,
                    OutputFilePath = BuildUniqueOutputFilePath(
                        task.OutputDirectory,
                        task.InputRoot,
                        Path.GetFileNameWithoutExtension(conventionalPath)),
                    Vendor = ScannerVendor.Faro,
                    MileageDirection = mileageDirection,
                    ScanDirection = scanDirection,
                    PreserveRawIntensity = true,
                    PreserveProcessedIntensity = true,
                    OutputMode = outputMode,
                    SegmentScanColumns = segmentColumns
                }
            ];
        }

        return faroInputs.Concat(zfsInputs).Select(inputPath => new ConversionOptions
        {
            InputFilePath = inputPath,
            TlsdFilePath = task.TlsdFilePath,
            OutputFilePath = BuildUniqueOutputFilePath(task.OutputDirectory, inputPath),
            Vendor = ConversionPrecheckService.DetectVendor(inputPath),
            MileageDirection = mileageDirection,
            ScanDirection = scanDirection,
            PreserveRawIntensity = true,
            PreserveProcessedIntensity = true,
            OutputMode = outputMode,
            SegmentScanColumns = segmentColumns
        }).ToArray();
    }

    public static int RemoveTasks(
        string taskListPath,
        IReadOnlyCollection<BatchConversionTask> tasks)
    {
        if (tasks.Count == 0) { return 0; }
        var keys = tasks.Select(task => BuildTaskKey(task.InputRoot, task.OutputDirectory))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retained = new List<string>();
        var removed = 0;
        foreach (var line in File.ReadAllLines(taskListPath))
        {
            var columns = line.Split('\t');
            if (columns.Length is 2 or 3 &&
                !string.IsNullOrWhiteSpace(columns[0]) &&
                !string.IsNullOrWhiteSpace(columns[1]) &&
                keys.Contains(BuildTaskKey(columns[0], columns[1])))
            {
                removed++;
            }
            else
            {
                retained.Add(line);
            }
        }
        File.WriteAllLines(taskListPath, retained, new UTF8Encoding(true));
        return removed;
    }

    private static string BuildTaskKey(string input, string output) =>
        $"{Path.GetFullPath(input.Trim().Trim('"'))}\n{Path.GetFullPath(output.Trim().Trim('"'))}";

    public static string BuildUniqueOutputFilePath(
        string outputDirectory,
        string identityPath,
        string? preferredName = null)
    {
        var normalizedIdentity = Path.GetFullPath(identityPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var name = string.IsNullOrWhiteSpace(preferredName)
            ? Path.GetFileNameWithoutExtension(normalizedIdentity)
            : preferredName.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(normalizedIdentity)))
            .Substring(0, 10)
            .ToLowerInvariant();
        return Path.Combine(
            Path.GetFullPath(outputDirectory),
            $"{name}_{hash}.e57");
    }
}
