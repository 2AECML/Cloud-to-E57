namespace EasyTry.Conversion.Models;

/// <summary>
/// 两类移动二维扫描统一后的单点。Y 为纵向里程坐标。
/// </summary>
public readonly struct UnifiedScanPoint
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double RawIntensity { get; init; }
    public float ProcessedIntensity { get; init; }
    public uint RowIndex { get; init; }
    public uint ColumnIndex { get; init; }
}
