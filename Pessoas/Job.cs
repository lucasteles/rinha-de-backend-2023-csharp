using System.Threading.Channels;
using StackExchange.Redis;


public sealed class Job(
    IServiceProvider provider,
    ILogger<Job> logger
) : BackgroundService
{
    const int MaxQueueBuffer = 100;
    const int MaxConcurrency = 4;
    static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel =
            Channel.CreateBounded<NovaPessoaMsg>(MaxQueueBuffer);

        await Task.WhenAll(
            PollingWorker(channel.Writer, stoppingToken),
            ConsumerWorker(channel.Reader, stoppingToken)
        );
    }

    async Task PollingWorker(
        ChannelWriter<NovaPessoaMsg> channel,
        CancellationToken ct)
    {
        using PeriodicTimer timer = new(PollingInterval);
        await using var scope = provider.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<Queue>();

        do
            try
            {
                await channel.WaitToWriteAsync(ct);
                logger.LogDebug("Polling messages...");

                var messages = await queue.TryRead();

                if (messages is null)
                    continue;

                logger.LogDebug("Received {MessagesCount} messages", messages.Count);

                await Task.WhenAll(messages.Select(m => channel.WriteAsync(m, ct).AsTask()));
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "erro no polling");
                await Task.Delay(1000, ct);
            }
        while (await timer.WaitForNextTickAsync(ct));

        channel.Complete();
    }

    async Task ConsumerWorker(
        ChannelReader<NovaPessoaMsg> channel,
        CancellationToken stopToken)
    {
        async Task TopicConsumer()
        {
            await channel.WaitToReadAsync(stopToken);
            List<Pessoa> pessoasNovas = new();
            List<RedisValue> msgIds = new();

            while (channel.TryRead(out var msg))
            {
                pessoasNovas.Add(msg.Pessoa);
                msgIds.Add(msg.Id);
            }

            await using var scope = provider.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<Queue>();
            var db = scope.ServiceProvider.GetRequiredService<Repositorio>();

            var inseridos = await db.InserirMuitos(pessoasNovas, stopToken);
            logger.LogDebug("Inseridos: {N}", inseridos);
            await Task.WhenAll(msgIds.Select(queue.Ack));
        }

        var tasks = Enumerable
            .Range(0, MaxConcurrency)
            .Select(_ => Task.Run(TopicConsumer, stopToken));
        await Task.WhenAll(tasks);
    }
}