namespace EasyTry.Conversion.Models;

public sealed record ScanMetadata(
    string SourceFilePath,
    ScannerVendor Vendor,
    long ScanColumns,
    int ScanRows,
    long GlobalColumnOffset = 0,
    long LogicalTotalScanColumns = 0,
    string? ScannerModel = null,
    string? ScannerSerialNumber = null,
    string? ScannerFirmware = null,
    DateTime? AcquisitionDate = null,
    string? ScanDataName = null,
    string? ProjectName = null,
    string? Location = null,
    ScanAcquisitionMetadata? AcquisitionParameters = null,
    IReadOnlyList<ScanSourcePartMetadata>? SourceParts = null);

public sealed record ScanAcquisitionMetadata(
    string? Resolution,
    string? ResolutionRaw,
    string? Quality,
    string? QualityRaw,
    double? PointRatePerSecond,
    int? PointsPerRevolution,
    double? NominalRevolutionsPerSecond,
    double? CalculatedRevolutionsPerSecond,
    string? Calculation,
    string? ParameterTable,
    string? ParameterSource);

public sealed record ScanSourcePartMetadata(
    int SourceIndex,
    string SourcePath,
    long LocalScanColumns,
    long GlobalPhysicalColumnStart,
    long GlobalPhysicalColumnEnd,
    long EffectiveGlobalColumnStart,
    long EffectiveGlobalColumnEnd,
    string? ScannerModel = null,
    string? ScannerSerialNumber = null,
    DateTime? AcquisitionDate = null);
