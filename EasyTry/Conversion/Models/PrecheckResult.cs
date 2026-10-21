namespace EasyTry.Conversion.Models;

public sealed class PrecheckResult
{
    public bool IsValid => Errors.Count == 0;

    public ScannerVendor DetectedVendor { get; init; } = ScannerVendor.Auto;

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Information { get; } = [];
}
