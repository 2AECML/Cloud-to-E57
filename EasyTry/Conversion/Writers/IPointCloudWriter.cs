using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Writers;

public interface IPointCloudWriter : IDisposable
{
    void Open(ScanMetadata metadata, ConversionOptions options);
    void WriteBatch(PointBatch batch, CancellationToken cancellationToken);
    void Complete(ConversionReport report);
}
