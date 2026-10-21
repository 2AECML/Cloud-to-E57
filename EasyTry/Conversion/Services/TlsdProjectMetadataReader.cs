using System.Globalization;
using System.Text.Json;
using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

internal sealed record TlsdProjectMetadata(
    string? FilePath,
    double? Speed,
    double? RingWidth,
    double? InnerDiameter,
    string Status,
    bool HasWarning = false);

/// <summary>Reads project JSON without changing or requiring the original TLSD file.</summary>
internal static class TlsdProjectMetadataReader
{
    public static TlsdProjectMetadata Read(ConversionOptions options) =>
        Read(options.InputFilePath, options.TlsdFilePath);

    public static TlsdProjectMetadata Read(string inputFilePath, string? tlsdFilePath)
    {
        string? filePath = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(tlsdFilePath))
            {
                filePath = Path.GetFullPath(tlsdFilePath);
                if (!Path.GetExtension(filePath).Equals(".tlsd", StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(filePath, "指定的项目文件不是 .tlsd 文件");
                }
                if (!File.Exists(filePath))
                {
                    return Failure(filePath, "指定的 .tlsd 文件不存在或无法访问");
                }
            }
            else
            {
                foreach (var directory in GetProjectDirectories(inputFilePath))
                {
                    // Do not search siblings or descend into another project's directory.
                    var candidates = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                        .Where(path => Path.GetExtension(path).Equals(".tlsd", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (candidates.Length > 1)
                    {
                        return Failure(null,
                            $"目录中有多个 .tlsd 文件，无法确定当前项目；请在任务清单第三列指定路径：{directory}");
                    }
                    if (candidates.Length == 1)
                    {
                        filePath = candidates[0];
                        break;
                    }
                }
            }

            if (filePath is null)
            {
                return new(null, null, null, null, "当前项目未发现 .tlsd 文件");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(filePath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure(filePath, ".tlsd 内容必须是 JSON 对象");
            }

            var speed = ReadPositiveNumber(root, "Speed");
            double? width = null;
            double? diameter = null;
            if (TryGetProperty(root, "CircleInfo", out var circle) && circle.ValueKind == JsonValueKind.Object)
            {
                width = ReadPositiveNumber(circle, "RingWidth");
                diameter = ReadPositiveNumber(circle, "RingDiameter");
            }
            var missing = new List<string>();
            if (!speed.HasValue) { missing.Add("小车速度"); }
            if (!width.HasValue) { missing.Add("环宽"); }
            if (!diameter.HasValue) { missing.Add("内径"); }
            var status = missing.Count == 0
                ? "已读取"
                : $"已读取；以下参数缺失或无有效正数：{string.Join("、", missing)}";
            return new(filePath, speed, width, diameter, status, missing.Count > 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException or NotSupportedException)
        {
            return Failure(filePath, $".tlsd 参数读取失败：{ex.Message}");
        }
    }

    private static TlsdProjectMetadata Failure(string? filePath, string status) =>
        new(filePath, null, null, null, status, true);

    private static IEnumerable<string> GetProjectDirectories(string inputPath)
    {
        var fullPath = Path.GetFullPath(inputPath);
        var current = Path.GetDirectoryName(fullPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (current is null) { yield break; }

        // Typical projects store scans in project\scan (possibly scan\1).
        // Only climb within that project; an unrelated ancestor's TLSD must never apply.
        var scanDirectory = new DirectoryInfo(current);
        while (scanDirectory.Parent is not null &&
            !scanDirectory.Name.Equals("scan", StringComparison.OrdinalIgnoreCase) &&
            !scanDirectory.Name.Equals("scans", StringComparison.OrdinalIgnoreCase) &&
            !scanDirectory.Name.Equals("激光扫描数据", StringComparison.OrdinalIgnoreCase))
        {
            scanDirectory = scanDirectory.Parent;
        }
        var projectDirectory = scanDirectory.Parent is not null ? scanDirectory.Parent.FullName : current;
        for (var directory = new DirectoryInfo(current); directory is not null; directory = directory.Parent)
        {
            yield return directory.FullName;
            if (directory.FullName.Equals(projectDirectory, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }
        }
    }

    private static double? ReadPositiveNumber(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var property)) { return null; }
        double value;
        var valid = property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(property.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value),
            _ => SetInvalid(out value)
        };
        return valid && double.IsFinite(value) && value > 0 ? value : null;
    }

    private static bool SetInvalid(out double value)
    {
        value = 0;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
