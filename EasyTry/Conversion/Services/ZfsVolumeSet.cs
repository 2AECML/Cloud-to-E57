using System.Text.RegularExpressions;

namespace EasyTry.Conversion.Services;

/// <summary>
/// Resolves a logical Z+F scan made of a primary .zfs file and optional
/// .part2.zfs, .part3.zfs, ... continuation volumes.
/// </summary>
public sealed class ZfsVolumeSet
{
    private static readonly Regex ContinuationNamePattern = new(
        @"^(?<stem>.+)\.part(?<number>[0-9]+)\.zfs$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private ZfsVolumeSet(
        string primaryPath,
        IReadOnlyList<string> volumePaths,
        bool isPartialVolumeChain = false)
    {
        PrimaryPath = primaryPath;
        VolumePaths = volumePaths;
        IsPartialVolumeChain = isPartialVolumeChain;
    }

    public string PrimaryPath { get; }
    public IReadOnlyList<string> VolumePaths { get; }
    public bool IsMultipart => VolumePaths.Count > 1;
    public bool IsPartialVolumeChain { get; }

    /// <summary>
    /// Maps a selected continuation volume back to its logical primary path
    /// when that primary file is available. Otherwise, preserves the selected
    /// continuation so its recoverable data can still be converted.
    /// </summary>
    public static string GetPrimaryPath(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new ArgumentException("ZFS 输入路径不能为空。", nameof(selectedPath));
        }

        var fullPath = Path.GetFullPath(selectedPath);
        var fileName = Path.GetFileName(fullPath);
        var match = ContinuationNamePattern.Match(fileName);
        if (!match.Success)
        {
            return fullPath;
        }

        var primaryPath = Path.Combine(
            Path.GetDirectoryName(fullPath) ?? string.Empty,
            match.Groups["stem"].Value + ".zfs");
        return File.Exists(primaryPath) ? primaryPath : fullPath;
    }

    /// <summary>
    /// Resolves and validates every available volume belonging to one logical
    /// ZFS scan, or identifies an explicitly selected orphan continuation.
    /// </summary>
    public static ZfsVolumeSet Resolve(string selectedPath)
    {
        var selectedFullPath = Path.GetFullPath(selectedPath);
        if (!File.Exists(selectedFullPath))
        {
            throw new FileNotFoundException(
                $"Z+F 输入文件不存在：{selectedFullPath}",
                selectedFullPath);
        }

        var primaryPath = GetPrimaryPath(selectedFullPath);
        var selectedNameMatch = ContinuationNamePattern.Match(
            Path.GetFileName(selectedFullPath));
        if (string.Equals(primaryPath, selectedFullPath, StringComparison.OrdinalIgnoreCase) &&
            selectedNameMatch.Success)
        {
            // A field copy can contain only one continuation volume. The Z+F SDK
            // can still recover the available scan data when OPEN_UNTIL_LAST is
            // used, so keep the explicitly selected file instead of redirecting
            // it to a primary volume that is not present.
            return new ZfsVolumeSet(
                selectedFullPath,
                [selectedFullPath],
                isPartialVolumeChain: true);
        }

        var directory = Path.GetDirectoryName(primaryPath)
            ?? throw new InvalidDataException("无法确定 ZFS 文件所在目录。");
        var stem = Path.GetFileNameWithoutExtension(primaryPath);
        var continuationPattern = new Regex(
            $@"^{Regex.Escape(stem)}\.part(?<number>[0-9]+)\.zfs$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var continuations = new SortedDictionary<int, string>();
        foreach (var candidate in Directory.EnumerateFiles(
                     directory,
                     "*.zfs",
                     SearchOption.TopDirectoryOnly))
        {
            var match = continuationPattern.Match(Path.GetFileName(candidate));
            if (!match.Success ||
                !int.TryParse(match.Groups["number"].Value, out var partNumber))
            {
                continue;
            }

            if (partNumber < 2)
            {
                throw new InvalidDataException(
                    $"Z+F 分卷编号无效：{Path.GetFileName(candidate)}。主卷应命名为 {stem}.zfs，续卷从 .part2.zfs 开始。");
            }

            continuations[partNumber] = Path.GetFullPath(candidate);
        }

        if (continuations.Count == 0)
        {
            return new ZfsVolumeSet(primaryPath, [primaryPath]);
        }

        var maximumPart = continuations.Keys.Max();
        var missingParts = Enumerable.Range(2, maximumPart - 1)
            .Where(partNumber => !continuations.ContainsKey(partNumber))
            .ToArray();
        if (missingParts.Length > 0)
        {
            throw new InvalidDataException(
                "Z+F 分卷不完整，缺少：" +
                string.Join(", ", missingParts.Select(number => $"{stem}.part{number}.zfs")));
        }

        var volumePaths = new List<string>(continuations.Count + 1)
        {
            primaryPath
        };
        volumePaths.AddRange(continuations.Values);

        if (!string.Equals(selectedFullPath, primaryPath, StringComparison.OrdinalIgnoreCase) &&
            !volumePaths.Contains(selectedFullPath, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"所选文件不属于 Z+F 逻辑扫描：{selectedFullPath}");
        }

        return new ZfsVolumeSet(primaryPath, volumePaths);
    }
}
