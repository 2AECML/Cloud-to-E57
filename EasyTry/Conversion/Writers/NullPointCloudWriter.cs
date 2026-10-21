using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Writers;

/// <summary>
/// C2 统计写入器：消费批次但不创建任何文件。
/// </summary>
public sealed class NullPointCloudWriter : IPointCloudWriter
{
    public long ConsumedPointCount { get; private set; }

    public void Open(ScanMetadata metadata, ConversionOptions options)
    {
        ConsumedPointCount = 0;
    }

    public void WriteBatch(PointBatch batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConsumedPointCount += batch.ValidPointCount;
    }

    public void Complete(ConversionReport report)
    {
        if (ConsumedPointCount != report.ValidPointCount)
        {
            throw new InvalidOperationException("Null Writer 消费点数与转换统计不一致。");
        }
    }

    public void Dispose()
    {
    }
}
