using Geotrack.Application.Analytics;

namespace Geotrack.Application.Interfaces.Service;

public interface IAnalyticsProcessingQueue
{
    bool TryEnqueue(AnalyticsProcessingJob job);

    IAsyncEnumerable<AnalyticsProcessingJob> ReadAllAsync(CancellationToken cancellationToken);
}
