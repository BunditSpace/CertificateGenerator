using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CertificateGenerator.Api.Models;

namespace CertificateGenerator.Api.Services;

/// <summary>
/// Manages per-job Server-Sent Events (SSE) subscriber channels and caches the latest job status.
/// </summary>
public sealed class JobProgressTracker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<JobStatusUpdate>>> _subscribers = new();
    private readonly ConcurrentDictionary<Guid, JobStatusUpdate> _latestStatus = new();

    public void Publish(JobStatusUpdate update)
    {
        _latestStatus[update.JobId] = update;

        if (!_subscribers.TryGetValue(update.JobId, out var jobChannels))
        {
            return;
        }

        foreach (var (_, channel) in jobChannels)
        {
            channel.Writer.TryWrite(update);

            if (JobStatuses.IsTerminal(update.Status))
            {
                channel.Writer.TryComplete();
            }
        }
    }

    public JobStatusUpdate? GetLatestStatus(Guid jobId)
    {
        return _latestStatus.TryGetValue(jobId, out var status) ? status : null;
    }

    public async IAsyncEnumerable<JobStatusUpdate> SubscribeAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<JobStatusUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var jobChannels = _subscribers.GetOrAdd(jobId, _ => new ConcurrentDictionary<Guid, Channel<JobStatusUpdate>>());
        jobChannels[subscriptionId] = channel;

        try
        {
            // Immediately emit the latest known state so a newly connected client doesn't wait.
            if (_latestStatus.TryGetValue(jobId, out var latest))
            {
                yield return latest;
                if (JobStatuses.IsTerminal(latest.Status))
                {
                    yield break;
                }
            }

            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
                if (JobStatuses.IsTerminal(update.Status))
                {
                    yield break;
                }
            }
        }
        finally
        {
            if (_subscribers.TryGetValue(jobId, out var existing))
            {
                existing.TryRemove(subscriptionId, out _);
                if (existing.IsEmpty)
                {
                    _subscribers.TryRemove(jobId, out _);
                }
            }
        }
    }
}
