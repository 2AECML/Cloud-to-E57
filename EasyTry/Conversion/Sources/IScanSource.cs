using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Sources;

public interface IScanSource : IDisposable
{
    ScanMetadata Metadata { get; }

    void Open(ConversionOptions options, CancellationToken cancellationToken);

    IEnumerable<PointBatch> ReadBatches(
        int scanLinesPerBatch,
        CancellationToken cancellationToken);
}
