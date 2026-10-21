namespace EasyTry.Conversion.Models;

/// <summary>
/// 固定数量扫描线形成的有界内存批次。
/// </summary>
public sealed class PointBatch : IDisposable
{
    public PointBatch(
        uint firstColumnIndex,
        int scanLineCount,
        long sourcePointCount,
        long invalidPointCount,
        List<UnifiedScanPoint> points)
    {
        FirstColumnIndex = firstColumnIndex;
        ScanLineCount = scanLineCount;
        SourcePointCount = sourcePointCount;
        InvalidPointCount = invalidPointCount;
        Points = points ?? throw new ArgumentNullException(nameof(points));
    }

    public uint FirstColumnIndex { get; }
    public int ScanLineCount { get; }
    public long SourcePointCount { get; }
    public long InvalidPointCount { get; }
    public List<UnifiedScanPoint> Points { get; }
    public long ValidPointCount => Points.Count;

    public void Dispose() => Points.Clear();
}
