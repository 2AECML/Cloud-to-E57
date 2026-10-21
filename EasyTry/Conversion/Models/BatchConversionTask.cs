namespace EasyTry.Conversion.Models;

public sealed record BatchConversionTask(
    string InputRoot,
    string OutputDirectory,
    IReadOnlyList<string> InputPaths)
{
    public string? TlsdFilePath { get; init; }
}

