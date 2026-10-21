namespace EasyTry.Conversion.Services;

public static class DiskSpaceGuard
{
    public const long MinimumFreeBytes = 20L * 1024 * 1024 * 1024;

    public static bool HasSafeFreeSpace(string outputPath, out long freeBytes)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"无法确定输出盘：{outputPath}");
        var drive = new DriveInfo(root);
        if (!drive.IsReady)
        {
            throw new IOException($"输出盘不可用：{root}");
        }

        freeBytes = drive.AvailableFreeSpace;
        return freeBytes >= MinimumFreeBytes;
    }

    public static string FormatBytes(long bytes) =>
        $"{bytes / 1024d / 1024d / 1024d:N2} GB";
}
