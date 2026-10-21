using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

/// <summary>
/// 点云转换服务边界。
/// </summary>
public interface IPointCloudConversionService
{
    Task<ConversionReport> ConvertAsync(
        ConversionOptions options,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken);
}
