using System.Threading.Channels;
using CertificateGenerator.Api.Models;

namespace CertificateGenerator.Api.Services;

public interface IBackgroundTaskQueue
{
    ValueTask QueueWorkItemAsync(GenerateJobWorkItem workItem, CancellationToken cancellationToken = default);
    ValueTask<GenerateJobWorkItem> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<GenerateJobWorkItem> _queue;

    public BackgroundTaskQueue(int capacity = 100)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _queue = Channel.CreateBounded<GenerateJobWorkItem>(options);
    }

    public ValueTask QueueWorkItemAsync(GenerateJobWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _queue.Writer.WriteAsync(workItem, cancellationToken);
    }

    public ValueTask<GenerateJobWorkItem> DequeueAsync(CancellationToken cancellationToken)
    {
        return _queue.Reader.ReadAsync(cancellationToken);
    }
}
