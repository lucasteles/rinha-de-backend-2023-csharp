using System.Data;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MassTransit;
using Npgsql;
using NpgsqlTypes;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services
    .Configure<RouteHandlerOptions>(opt => opt.ThrowOnBadRequest = false)
    .ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolver =
        JsonTypeInfoResolver.Combine(PessoaJsonContext.Default, PessoaInJsonContext.Default))
    .AddOutputCache(opt => opt.AddBasePolicy(b => b.Cache().Expire(TimeSpan.FromMinutes(5))))
    .AddStackExchangeRedisOutputCache(opt =>
    {
        opt.InstanceName = "pessoa-api";
        opt.Configuration = Environment.GetEnvironmentVariable("CACHE_HOST") ?? "localhost:6379";
    });

var app = builder.Build();
app.UseOutputCache();

var pessoas = app.MapGroup("pessoas");

pessoas.MapPost("/", async (PessoaIn pessoa) =>
{
    if (pessoa is not
        {
            Apelido.Length: > 0 and <= 32,
            Nome.Length: > 0 and <= 100,
        }
        || !DateOnly.TryParse(pessoa.Nascimento, out var nascimento))
        return Results.UnprocessableEntity();

    if (pessoa.Stack is not null)
        for (var i = 0; i < pessoa.Stack.Length; i++)
            if (pessoa.Stack[i] is not { Length: > 0 and <= 32 })
                return Results.UnprocessableEntity();

    var id = NewId.NextSequentialGuid();

    await using var cmd = Db.DataSource.CreateCommand(Db.InsertPessoaCommand);
    cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = id });
    cmd.Parameters.Add(new NpgsqlParameter<string> { Value = pessoa.Apelido });
    cmd.Parameters.Add(new NpgsqlParameter<string> { Value = pessoa.Nome });
    cmd.Parameters.Add(new NpgsqlParameter<DateOnly> { Value = nascimento });
    cmd.Parameters.Add(new NpgsqlParameter<string[]?>
    // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
    { Value = pessoa.Stack, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar });

    var dbResult = await cmd.ExecuteNonQueryAsync();

    if (dbResult is 0)
        return Results.UnprocessableEntity();

    return Results.Created($"/pessoas/{id}", null);
});

pessoas.MapGet("/{id}", async (Guid id) =>
{
    await using var cmd = Db.DataSource.CreateCommand(Db.QueryOne);
    cmd.Parameters.Add(new NpgsqlParameter<Guid> { Value = id });
    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

    if (!reader.HasRows)
        return Results.NotFound();

    await reader.ReadAsync();
    var pessoa = await ReadPessoa(reader);
    return Results.Ok(pessoa);
});

pessoas.MapGet("/", async (string t) =>
{
    await using var cmd = Db.DataSource.CreateCommand(Db.QueryMany);
    cmd.Parameters.Add(new NpgsqlParameter<string> { Value = t });
    var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

    return string.IsNullOrWhiteSpace(t) ? Results.BadRequest() : Results.Ok(ReadAllAsync(reader));
});

app.MapGet("contagem-pessoas", async () =>
    {
        await using var cmd = Db.DataSource.CreateCommand(Db.Count);
        return await cmd.ExecuteScalarAsync();
    }).CacheOutput(x => x.NoCache())
    .ShortCircuit();

app.MapGet("status", async () =>
    {
        await using var cmd = Db.DataSource.CreateCommand("SELECT version()");
        return $"{Environment.MachineName} => {await cmd.ExecuteScalarAsync()}";
    }).CacheOutput(x => x.NoCache())
    .ShortCircuit();

static async Task<Pessoa> ReadPessoa(NpgsqlDataReader reader) =>
    new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        DateOnly.FromDateTime(reader.GetDateTime(3)),
        await reader.GetFieldValueAsync<object>(4) as string[]
    );

static async IAsyncEnumerable<Pessoa> ReadAllAsync(NpgsqlDataReader reader)
{
    await using (reader)
    {
        if (!reader.HasRows)
            yield break;

        while (await reader.ReadAsync())
            yield return await ReadPessoa(reader);
    }
}

await using (Db.DataSource)
{
    await Db.Migrate();
    app.Run();
}

public sealed record Pessoa(
    Guid Id, string Apelido, string Nome, DateOnly Nascimento, string[]? Stack);

public sealed record PessoaIn(
    string Apelido, string Nome, string Nascimento, string[]? Stack);


[JsonSerializable(typeof(Pessoa))]
[JsonSerializable(typeof(IAsyncEnumerable<Pessoa>))]
[JsonSerializable(typeof(long))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
public partial class PessoaJsonContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(PessoaIn))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
public partial class PessoaInJsonContext : JsonSerializerContext
{
}
