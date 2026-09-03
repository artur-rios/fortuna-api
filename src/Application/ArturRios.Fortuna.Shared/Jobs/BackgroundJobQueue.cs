using System.Threading.Channels;

namespace ArturRios.Fortuna.Shared.Jobs;

public sealed class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<Guid> channel;
    private int depth;

    public BackgroundJobQueue(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public int Depth => Volatile.Read(ref depth);

    public async ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await channel.Writer.WriteAsync(jobId, cancellationToken);
        Interlocked.Increment(ref depth);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        var id = await channel.Reader.ReadAsync(cancellationToken);
        Interlocked.Decrement(ref depth);
        return id;
    }
}
