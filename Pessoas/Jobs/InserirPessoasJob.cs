using System.Threading.Channels;
using Npgsql;
using StackExchange.Redis;

public sealed class InserirPessoasJob(IServiceProvider provider, ILogger<InserirPessoasJob> logger) : BackgroundService
{
    const int MaxQueueBuffer = 500;
    const int MaxConcurrency = 4;
    static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel =
            Channel.CreateBounded<NovaPessoaMsg>(MaxQueueBuffer);

        await Task.WhenAll(
            PollingWorker(channel.Writer, stoppingToken),
            ConsumerWorker(channel, stoppingToken)
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
                logger.LogCritical(ex, "Erro no polling");
                await Task.Delay(1000, ct);
            }
        while (await timer.WaitForNextTickAsync(ct));

        channel.Complete();
    }

    async Task ConsumerWorker(
        Channel<NovaPessoaMsg> channel,
        CancellationToken ct)
    {
        async Task TopicConsumer()
        {
            List<NovaPessoaMsg> msgLidas = new();
            List<Pessoa> pessoasNovas = new();
            List<RedisValue> msgIds = new();

            while (true)
            {
                await channel.Reader.WaitToReadAsync(ct);
                msgLidas.Clear();
                pessoasNovas.Clear();
                msgIds.Clear();

                while (channel.Reader.TryRead(out var msg))
                {
                    msgLidas.Add(msg);
                    pessoasNovas.Add(msg.Pessoa);
                    msgIds.Add(msg.Id);
                }

                await using var scope = provider.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<Queue>();
                var db = scope.ServiceProvider.GetRequiredService<Repositorio>();

                try
                {
                    var inseridos = await db.Inserir(pessoasNovas, ct);
                    logger.LogDebug("Inseridos: {N}", inseridos);
                }
                catch (NpgsqlException ex)
                {
                    logger.LogCritical(ex, "Erro no banco de dados");
                    foreach (var msg in msgLidas)
                        await channel.Writer.WriteAsync(msg, ct);

                    await Task.Delay(3000, ct);
                    continue;
                }

                await Task.WhenAll(msgIds.Select(queue.Ack));
            }
        }

        var tasks = Enumerable
            .Range(0, MaxConcurrency)
            .Select(_ => Task.Run(TopicConsumer, ct));
        await Task.WhenAll(tasks);
    }
}