using MassTransit;
using Npgsql;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration.AddEnvironmentVariables();
var settings = builder.Configuration.Get<AppSettings>() ?? new();
builder.Services.ConfigureServices(settings);

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

        if (await cache.Existe(novaPessoa.Apelido))
            return Results.UnprocessableEntity();

        Pessoa pessoa = new(
            Id: NewId.NextSequentialGuid(),
            Apelido: novaPessoa.Apelido,
            Nome: novaPessoa.Nome,
            Nascimento: nascimento,
            Stack: novaPessoa.Stack
        );

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

pessoas.MapGet("/", (Repositorio db, string t) =>
    string.IsNullOrWhiteSpace(t)
        ? Results.BadRequest()
        : Results.Ok(db.Buscar(t)));

app.MapGet("contagem-pessoas", (Repositorio repo) => repo.Contar()).CacheOutput(x => x.NoCache())
    .ShortCircuit();

app.MapGet("status", async (NpgsqlDataSource dataSource) =>
    {
        await using var cmd = dataSource.CreateCommand("SELECT version()");
        return $"{Environment.MachineName} => {await cmd.ExecuteScalarAsync()}";
    }).CacheOutput(x => x.NoCache())
    .ShortCircuit();

app.MapPost("reset", async (NpgsqlDataSource dataSource) =>
    {
        await using var cmd = dataSource.CreateCommand("TRUNCATE TABLE pessoas");
    }).CacheOutput(x => x.NoCache())
    .ShortCircuit();

await app.GaranteRedisQueue();
await app.RunAsync();