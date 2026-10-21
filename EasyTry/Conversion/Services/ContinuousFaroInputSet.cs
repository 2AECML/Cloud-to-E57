using System.Text.RegularExpressions;

namespace EasyTry.Conversion.Services;

/// <summary>
/// Validates and naturally orders separately stored FARO packages that form
/// one continuous mobile scan.
/// </summary>
public static partial class ContinuousFaroInputSet
{
    public static IReadOnlyList<string> NormalizeAndValidate(
        IEnumerable<string> inputPaths)
    {
        var normalized = BatchInputDiscovery.NormalizeFaroFiles(inputPaths);
        if (normalized.Count == 0)
        {
            throw new InvalidOperationException(
                "连续 FARO 扫描组至少需要一个有效的 .fls 文件或扫描包。");
        }

        var entries = normalized.Select(ParseEntry).ToArray();
        var prefix = entries[0].Prefix;
        if (entries.Any(entry => !string.Equals(
                entry.Prefix,
                prefix,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "连续 FARO 扫描组的文件名必须具有相同前缀和连续数字后缀，例如 Zuo001.fls、Zuo002.fls。");
        }

        var duplicate = entries
            .GroupBy(entry => entry.SequenceNumber)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"连续 FARO 扫描组存在重复编号：{duplicate.Key}。");
        }

        var ordered = entries
            .OrderBy(entry => entry.SequenceNumber)
            .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            var expected = ordered[index - 1].SequenceNumber + 1;
            if (ordered[index].SequenceNumber != expected)
            {
                throw new InvalidOperationException(
                    $"连续 FARO 扫描组编号不连续：缺少 {prefix}{expected.ToString().PadLeft(ordered[index].NumberWidth, '0')}.fls。");
            }
        }

        return ordered.Select(entry => entry.Path).ToArray();
    }

    public static string BuildDatasetName(IReadOnlyList<string> orderedPaths)
    {
        if (orderedPaths.Count == 0)
        {
            throw new ArgumentException("连续 FARO 扫描组不能为空。", nameof(orderedPaths));
        }

        var first = Path.GetFileNameWithoutExtension(
            orderedPaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (orderedPaths.Count == 1)
        {
            return first;
        }

        var last = Path.GetFileNameWithoutExtension(
            orderedPaths[^1].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return $"{first}-{last}";
    }

    public static string BuildOutputFilePath(
        string outputDirectory,
        IReadOnlyList<string> orderedPaths) => Path.Combine(
            Path.GetFullPath(outputDirectory),
            BuildDatasetName(orderedPaths) + ".e57");

    private static Entry ParseEntry(string path)
    {
        var name = Path.GetFileNameWithoutExtension(
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var match = NumberedNamePattern().Match(name);
        if (!match.Success ||
            !long.TryParse(match.Groups["number"].Value, out var sequence))
        {
            throw new InvalidOperationException(
                $"连续 FARO 输入名称缺少数字后缀：{Path.GetFileName(path)}。");
        }

        return new Entry(
            path,
            match.Groups["prefix"].Value,
            sequence,
            match.Groups["number"].Value.Length);
    }

    [GeneratedRegex(@"^(?<prefix>.*?)(?<number>[0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedNamePattern();

    private sealed record Entry(
        string Path,
        string Prefix,
        long SequenceNumber,
        int NumberWidth);
}
