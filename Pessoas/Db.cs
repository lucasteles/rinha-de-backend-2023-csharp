using Npgsql;

public static class Db
{
    public static bool OnContainer { get; } =
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") is "true";

    public static string DbHost { get; } =
        Environment.GetEnvironmentVariable("DB_HOST") ??
        (OnContainer ? "host.docker.internal" : "localhost");

    const string DbName = "pessoas_db";

    public static string ConnectionString { get; } =
        $"Host={DbHost};Username=postgres;Password=postgres;Database={DbName}";

    public static readonly NpgsqlDataSource DataSource = NpgsqlDataSource.Create(ConnectionString);

    public const string InsertPessoaCommand =
        """
        INSERT INTO
        pessoas (id, apelido, nome, nascimento, stack)
        VALUES ($1, $2, $3, $4, $5) ON CONFLICT (apelido) DO NOTHING
        """;

    public const string QueryOne = "SELECT * FROM pessoas WHERE id = $1";

    public const string QueryMany =
        """
        SELECT * FROM pessoas
        WHERE apelido LIKE '%' || $1 || '%'
        or nome LIKE '%' || $1 || '%'
        or EXISTS (SELECT FROM unnest(stack) elem WHERE  elem LIKE '%' || $1 || '%' )
        LIMIT 50
        """;

    public const string Count = "SELECT COUNT(1) FROM pessoas";

    public static async Task Migrate()
    {
        await EnsureDb();
        await using var dataSource = NpgsqlDataSource.Create(ConnectionString);
        await using var cmd = dataSource.CreateCommand(
            """
            BEGIN;
            LOCK pg_type;
            CREATE TABLE IF NOT EXISTS pessoas (
                id uuid PRIMARY KEY,
                apelido varchar(32) UNIQUE,
                nome varchar(100) NOT NULL,
                nascimento date NOT NULL,
                stack varchar(32)[] NULL
            );
            COMMIT;
            """
        );

        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task EnsureDb()
    {
        if (OnContainer) return;

        var connectionString =
            $"Host={DbHost};Username=postgres;Password=postgres;Database=postgres";
        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        await using var cmd = dataSource.CreateCommand($"CREATE DATABASE {DbName}");
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("already exists"))
        {
            // ignore
        }
    }
}