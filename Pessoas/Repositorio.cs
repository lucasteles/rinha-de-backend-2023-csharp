using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text;
using MassTransit;
using Npgsql;
using NpgsqlTypes;

public sealed class Repositorio(NpgsqlDataSource dataSource)
{
    public async ValueTask<Guid?> Inserir(NovaPessoa novaPessoa, CancellationToken ct = default)
    {
        if (!DateOnly.TryParse(novaPessoa.Nascimento, out var nascimento))
            return null;

        StringBuilder meta = new();

        if (novaPessoa.Stack is not null)
            for (var i = 0; i < novaPessoa.Stack.Length; i++)
            {
                if (novaPessoa.Stack[i] is not {Length: > 0 and <= 32})
                    return null;

                meta.Append(novaPessoa.Stack[i]);
            }

        var id = NewId.NextSequentialGuid();

        const string insertCommand =
            """
            INSERT INTO pessoas (id, apelido, nome, nascimento, stack, meta)
            VALUES ($1, $2, $3, $4, $5, $6) ON CONFLICT (apelido) DO NOTHING
            """;

        await using var cmd = dataSource.CreateCommand(insertCommand);
        cmd.Parameters.Add(new NpgsqlParameter<Guid> {Value = id});
        cmd.Parameters.Add(new NpgsqlParameter<string> {Value = novaPessoa.Apelido});
        cmd.Parameters.Add(new NpgsqlParameter<string> {Value = novaPessoa.Nome});
        cmd.Parameters.Add(new NpgsqlParameter<DateOnly> {Value = nascimento});
        cmd.Parameters.Add(new NpgsqlParameter<string[]?>
            // ReSharper disable once BitwiseOperatorOnEnumWithoutFlags
            {Value = novaPessoa.Stack, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar});

        meta.Append(novaPessoa.Apelido);
        meta.Append(novaPessoa.Nome);

        cmd.Parameters.Add(new NpgsqlParameter<string> {Value = meta.ToString()});

        var dbResult = await cmd.ExecuteNonQueryAsync(ct);

        return dbResult is 0 ? null : id;
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

    public async IAsyncEnumerable<Pessoa> Buscar(string termo,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        const string query =
            """
            SELECT id, apelido, nome, nascimento, stack FROM pessoas
            WHERE meta LIKE '%' || $1 || '%'
            LIMIT 50
            """;

        await using var cmd = dataSource.CreateCommand(query);
        cmd.Parameters.Add(new NpgsqlParameter<string> {Value = termo});
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

    public async Task<long> Contar(CancellationToken ct = default)
    {
        const string count = "SELECT COUNT(1) FROM pessoas";
        await using var cmd = dataSource.CreateCommand(count);
        return await cmd.ExecuteScalarAsync(ct) as long? ?? 0L;
    }

    public async Task<int> InserirMuitos(
        IEnumerable<Pessoa> pessoas, CancellationToken ct = default)
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