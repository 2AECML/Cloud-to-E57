namespace EasyTry.Conversion.Models;

public sealed record ConversionProgress(
    int Percent,
    string Stage,
    long ProcessedScanLines = 0,
    long TotalScanLines = 0,
    long ValidPointCount = 0,
    long InvalidPointCount = 0);
