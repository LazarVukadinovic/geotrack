using System.Threading.Channels;
using Geotrack.Application.Analytics;
using Geotrack.Application.Interfaces.Service;

namespace Geotrack.Server.Services;

public sealed class AnalyticsProcessingQueue : IAnalyticsProcessingQueue
{
    private readonly Channel<AnalyticsProcessingJob> _channel = Channel.CreateBounded<AnalyticsProcessingJob>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    public bool TryEnqueue(AnalyticsProcessingJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<AnalyticsProcessingJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
