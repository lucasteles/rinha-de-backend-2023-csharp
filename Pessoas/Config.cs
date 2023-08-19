using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Npgsql;
using StackExchange.Redis;

public static class AppSettings
{
    public static bool IsOnContainer { get; } =
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") is "true";

    public static string DbHost { get; } =
        Environment.GetEnvironmentVariable("DB_HOST") ??
        (IsOnContainer ? "host.docker.internal" : "localhost");

    public static string CacheHost { get; } =
        Environment.GetEnvironmentVariable("CACHE_HOST") ??
        (IsOnContainer ? "host.docker.internal:6379" : "localhost:6379");
}

public static class Config
{
    public static void ConfigureServices(this IServiceCollection services)
    {
        services
            .Configure<RouteHandlerOptions>(opt => opt.ThrowOnBadRequest = false)
            .ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolver =
                JsonTypeInfoResolver.Combine(
                    PessoaJsonContext.Default,
                    NovaPessoaJsonContext.Default
                ))
            .AddOutputCache(opt => opt.AddBasePolicy(b => b.Cache().SetLocking(false)))
            .AddStackExchangeRedisOutputCache(opt =>
            {
                opt.InstanceName = "pessoa-api";
                opt.Configuration = AppSettings.CacheHost;
            })
            .AddNpgsqlDataSource(new NpgsqlConnectionStringBuilder
            {
                Host = AppSettings.DbHost,
                Database = "pessoas_db",
                Username = "postgres",
                Password = "postgres",
                ConnectionPruningInterval = 1,
                ConnectionIdleLifetime = 2,
                NoResetOnClose = true,
                Enlist = false,
            }.ToString());

        services
            .AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(AppSettings.CacheHost))
            .AddScoped<IDatabase>(sp =>
                sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase())
            .AddScoped<Queue>()
            .AddScoped<Cache>()
            .AddSingleton<Repositorio>();

        services.AddHostedService<InserirPessoasJob>();
    }

    public static async Task GaranteRedisQueue(this WebApplication app)
    {
        var muxer = app.Services.GetRequiredService<IConnectionMultiplexer>();
        var db = muxer.GetDatabase();
        if (!await db.KeyExistsAsync(Queue.StreamName) ||
            (await db.StreamGroupInfoAsync(Queue.StreamName))
            .All(x => x.Name is not Queue.GroupName))
            await db.StreamCreateConsumerGroupAsync(Queue.StreamName, Queue.GroupName, "0-0", true);
    }
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