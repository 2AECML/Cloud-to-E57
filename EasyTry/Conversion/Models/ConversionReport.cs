namespace EasyTry.Conversion.Models;

public readonly record struct NumericRange(double Minimum, double Maximum)
{
    public bool HasValue => double.IsFinite(Minimum) && double.IsFinite(Maximum);

    public override string ToString() =>
        HasValue ? $"[{Minimum:G9}, {Maximum:G9}]" : "无有效值";
}

public sealed record ConversionReport(
    ScanMetadata Metadata,
    long ProcessedScanLines,
    long ValidPointCount,
    long InvalidPointCount,
    NumericRange XRange,
    NumericRange YRange,
    NumericRange ZRange,
    NumericRange RawIntensityRange,
    NumericRange ProcessedIntensityRange,
    TimeSpan Elapsed,
    E57OutputMode ResolvedOutputMode = E57OutputMode.SingleFile,
    string? MetadataDocumentPath = null);
