using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text;
using Npgsql;
using NpgsqlTypes;

public sealed class Repositorio(NpgsqlDataSource dataSource)
{
    public async IAsyncEnumerable<Pessoa> Buscar(
        string termo,
        int count,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        const string query =
            """
            SELECT id, apelido, nome, nascimento, stack FROM pessoas
            WHERE meta LIKE '%' || $1 || '%'
            LIMIT $2
            """;

        await using var cmd = dataSource.CreateCommand(query);
        cmd.Parameters.Add(new NpgsqlParameter<string> {Value = termo});
        cmd.Parameters.Add(new NpgsqlParameter<int> {Value = count});
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        if (!reader.HasRows)
            yield break;

        while (await reader.ReadAsync(ct))
            yield return await ReadPessoa(reader, ct);
    }

    public async Task<bool> Existe(string apelido, CancellationToken ct = default)
    {
        const string exists = "SELECT EXISTS(SELECT FROM pessoas WHERE apelido=$1)";
        await using var cmd = dataSource.CreateCommand(exists);
        cmd.Parameters.Add(new NpgsqlParameter<string> {Value = apelido});
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as bool? ?? false;
    }

    public async Task<Pessoa?> Um(Guid id, CancellationToken ct = default)
    {
        const string query = "SELECT * FROM pessoas WHERE id = $1 LIMIT 1";

        await using var cmd = dataSource.CreateCommand(query);
        cmd.Parameters.Add(new NpgsqlParameter<Guid> {Value = id});
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        if (!reader.HasRows)
            return null;

        await reader.ReadAsync(ct);

        var pessoa = await ReadPessoa(reader, ct);
        return pessoa;
    }

    public async Task<long> Contar(CancellationToken ct = default)
    {
        const string count = "SELECT COUNT(1) FROM pessoas";
        await using var cmd = dataSource.CreateCommand(count);
        return await cmd.ExecuteScalarAsync(ct) as long? ?? 0L;
    }

    public async Task<int> Inserir(
        IEnumerable<Pessoa> pessoas,
        CancellationToken ct = default)
    {
        var batch = dataSource.CreateBatch();
        const string insertSql =
            """
            INSERT INTO pessoas (id, apelido, nome, nascimento, stack, meta)
            VALUES ($1, $2, $3, $4, $5, $6)
            """;

        foreach (var novaPessoa in pessoas)
        {
            StringBuilder meta = new();
            if (novaPessoa.Stack is not null)
                for (var i = 0; i < novaPessoa.Stack.Length; i++)
                    meta.Append(novaPessoa.Stack[i]);
            meta.Append(novaPessoa.Apelido);
            meta.Append(novaPessoa.Nome);

            var cmd = new NpgsqlBatchCommand(insertSql);
            cmd.Parameters.Add(new NpgsqlParameter<Guid> {Value = novaPessoa.Id});
            cmd.Parameters.Add(new NpgsqlParameter<string> {Value = novaPessoa.Apelido});
            cmd.Parameters.Add(new NpgsqlParameter<string> {Value = novaPessoa.Nome});
            cmd.Parameters.Add(new NpgsqlParameter<DateOnly> {Value = novaPessoa.Nascimento});
            cmd.Parameters.Add(new NpgsqlParameter<string[]?>
                // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
                {
                    Value = novaPessoa.Stack,
                    NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar,
                });

            cmd.Parameters.Add(new NpgsqlParameter<string> {Value = meta.ToString()});

            batch.BatchCommands.Add(cmd);
        }

        return await batch.ExecuteNonQueryAsync(ct);
    }


    static async Task<Pessoa> ReadPessoa(DbDataReader reader, CancellationToken ct) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            DateOnly.FromDateTime(reader.GetDateTime(3)),
            await reader.GetFieldValueAsync<object>(4, ct) as string[]
        );
}