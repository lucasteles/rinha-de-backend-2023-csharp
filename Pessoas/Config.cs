using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Npgsql;
using StackExchange.Redis;

public static class Config
{
    public static async Task GaranteRedisQueue(this WebApplication app)
    {
        var muxer = app.Services.GetRequiredService<IConnectionMultiplexer>();
        var db = muxer.GetDatabase();
        if (!await db.KeyExistsAsync(Queue.StreamName) ||
            (await db.StreamGroupInfoAsync(Queue.StreamName)).All(x =>
                x.Name is not Queue.GroupName))
            await db.StreamCreateConsumerGroupAsync(Queue.StreamName, Queue.GroupName, "0-0");
    }

    public static void ConfigureServices(this IServiceCollection services,
        AppSettings settings)
    {
        services
            .Configure<RouteHandlerOptions>(opt => opt.ThrowOnBadRequest = false)
            .ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.TypeInfoResolver = StaticJsonTypeInfoResolver)
            .AddOutputCache(opt => opt
                .AddBasePolicy(b => b.Cache().SetLocking(false).Expire(TimeSpan.FromMinutes(5))))
            .AddStackExchangeRedisOutputCache(opt =>
            {
                opt.InstanceName = "pessoa-api";
                opt.Configuration = settings.CacheHost;
            })
            .AddNpgsqlDataSource(new NpgsqlConnectionStringBuilder
            {
                Host = settings.DbHost, Database = "pessoas_db", Username = "postgres",
                Password = "postgres",
                ConnectionPruningInterval = 1, NoResetOnClose = true, Enlist = false,
            }.ToString());

        services
            .AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(settings.CacheHost))
            .AddScoped<IDatabaseAsync>(sp =>
                sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase())
            .AddScoped<Queue>()
            .AddScoped<Cache>()
            .AddSingleton<Repositorio>();

        services.AddHostedService<Job>();
    }

    public static readonly IJsonTypeInfoResolver StaticJsonTypeInfoResolver =
        JsonTypeInfoResolver.Combine(PessoaJsonContext.Default, NovaPessoaJsonContext.Default);
}

[JsonSerializable(typeof(Pessoa))]
[JsonSerializable(typeof(IAsyncEnumerable<Pessoa>))]
[JsonSerializable(typeof(long))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Serialization | JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
public partial class PessoaJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(NovaPessoa))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
public partial class NovaPessoaJsonContext : JsonSerializerContext;