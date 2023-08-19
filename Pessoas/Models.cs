public sealed record Pessoa(
    Guid Id, string Apelido, string Nome, DateOnly Nascimento, string[]? Stack);

public sealed record NovaPessoa(
    string Apelido, string Nome, string Nascimento, string[]? Stack);


public sealed class AppSettings
{
    [ConfigurationKeyName("DOTNET_RUNNING_IN_CONTAINER")]
    public bool OnContainer { get; init; }

    [ConfigurationKeyName("DB_HOST")]
    public string DbHost { get; init; } = "localhost";

    [ConfigurationKeyName("CACHE_HOST")]
    public string CacheHost { get; init; } = "localhost:6379";
}