using System.Threading.Channels;
using Npgsql;
using StackExchange.Redis;

public sealed class InserirPessoasJob
    (IServiceProvider provider, ILogger<InserirPessoasJob> logger) : BackgroundService
{
    const int MaxConcurrency = 10;
    const int MaxQueueBuffer = 10_000;

    static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

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
                await channel.WaitToWriteAsync(ct).ConfigureAwait(false);
                logger.LogDebug("Polling messages...");

                var messages = await queue.TryRead(MaxQueueBuffer).ConfigureAwait(false);

                if (messages is null)
                    continue;

                logger.LogInformation("Received {MessagesCount} messages [{Thread}]",
                    messages.Count, Environment.CurrentManagedThreadId);

                await Task.WhenAll(messages.Select(m =>
                        channel.WriteAsync(m, ct)
                            .AsTask()))
                    .ConfigureAwait(false);
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
                await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false);
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

        await Task.WhenAll(Enumerable
            .Range(0, MaxConcurrency)
            .Select(_ => Task.Run(TopicConsumer, ct)));
    }
}