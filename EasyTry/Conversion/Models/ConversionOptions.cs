namespace EasyTry.Conversion.Models;

public enum ScannerVendor
{
    Auto = 0,
    ZPlusF = 1,
    Faro = 2
}

public enum MileageDirection
{
    Ascending = 0,
    Descending = 1
}

public enum MobileScanDirection
{
    Forward = 0,
    Reverse = 1
}

public enum E57OutputMode
{
    SingleFile = 0,
    SegmentedDataset = 1,
    AutoByScanColumnThreshold = 2
}

/// <summary>
/// 点云转换任务参数。C1 由页面创建，C2/C3 继续复用。
/// </summary>
public sealed class ConversionOptions
{
    public required string InputFilePath { get; init; }

    /// <summary>Optional project metadata file supplied by a task-list row.</summary>
    public string? TlsdFilePath { get; init; }

    /// <summary>
    /// Ordered FARO inputs that form one logical mobile scan. Empty for the
    /// existing per-input conversion modes.
    /// </summary>
    public IReadOnlyList<string> ContinuousInputPaths { get; init; } = [];

    public required string OutputFilePath { get; init; }

    public ScannerVendor Vendor { get; init; } = ScannerVendor.Auto;

    public MileageDirection MileageDirection { get; init; } = MileageDirection.Ascending;

    public MobileScanDirection ScanDirection { get; init; } = MobileScanDirection.Forward;

    public bool PreserveRawIntensity { get; init; } = true;

    public bool PreserveProcessedIntensity { get; init; } = true;

    public E57OutputMode OutputMode { get; init; } = E57OutputMode.SingleFile;

    public int SegmentScanColumns { get; init; } = 20_000;
}
