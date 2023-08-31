using MassTransit;
using Npgsql;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureServices();

var app = builder.Build();
app.UseOutputCache();

var pessoas = app.MapGroup("pessoas");

pessoas.MapPost("/",
    async (Queue queue, Cache cache, NovaPessoa novaPessoa) =>
    {
        if (novaPessoa is not
            {
                Apelido.Length: > 0 and <= 32,
                Nome.Length: > 0 and <= 100,
            }
            || !DateOnly.TryParse(novaPessoa.Nascimento, out var nascimento)
           )
            return Results.UnprocessableEntity();

        if (novaPessoa.Stack is not null)
            for (var i = 0; i < novaPessoa.Stack.Length; i++)
                if (novaPessoa.Stack[i] is not {Length: > 0 and <= 32})
                    return Results.UnprocessableEntity();

        Pessoa pessoa = new(
            Id: NewId.NextSequentialGuid(),
            Apelido: novaPessoa.Apelido,
            Nome: novaPessoa.Nome,
            Nascimento: nascimento,
            Stack: novaPessoa.Stack
        );

        if (!await cache.TentaReservar(pessoa))
            return Results.UnprocessableEntity();

        await Task.WhenAll(
            queue.Push(pessoa),
            cache.Set(pessoa)
        );

        return Results.Created($"/pessoas/{pessoa.Id}", null);
    });

pessoas.MapGet("/{id}", async (Cache cache, Guid id) =>
    await cache.Get(id) is not { } pessoa
        ? Results.NotFound()
        : Results.Ok(pessoa));

pessoas.MapGet("/", (CancellationToken ct, Repositorio db, string t) =>
    string.IsNullOrWhiteSpace(t)
        ? Results.BadRequest()
        : Results.Ok(db.Buscar(t, 50, ct)));

app.MapGet("contagem-pessoas", async (
        CancellationToken ct, 
        Repositorio repo, 
        Queue queue, 
        ILogger<Program> logger) =>
        {
            do
            {
                logger.LogInformation("Waiting to complete!");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            while (await queue.HasMessages());

            return await repo.Contar(ct);
        })
    .CacheOutput(x => x.NoCache())
    .ShortCircuit();

app.MapGet("status", async (CancellationToken ct, NpgsqlDataSource dataSource) =>
    {
        await using var cmd = dataSource.CreateCommand("SELECT version()");
        return $"{Environment.MachineName} => {await cmd.ExecuteScalarAsync(ct)}";
    })
    .CacheOutput(x => x.NoCache())
    .ShortCircuit();

await app.GaranteRedisQueue();
await app.RunAsync();