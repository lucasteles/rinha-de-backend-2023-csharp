using StackExchange.Redis;

public readonly record struct NovaPessoaMsg(RedisValue Id, Pessoa Pessoa);

public sealed class Queue(IDatabaseAsync db)
{
    public const string StreamName = "novas-pessoas";
    public const string GroupName = $"{StreamName}-consumers";

    public async Task Push(Pessoa pessoa)
    {
        await db.StreamAddAsync(StreamName, new NameValueEntry[]
        {
            new(nameof(Pessoa.Id), pessoa.Id.ToString()),
            new(nameof(Pessoa.Apelido), pessoa.Apelido),
            new(nameof(Pessoa.Nome), pessoa.Nome),
            new(nameof(Pessoa.Nascimento), pessoa.Nascimento.ToString("o")),
            new(nameof(Pessoa.Stack),
                pessoa.Stack is null ? null : string.Join(',', pessoa.Stack)),
        });
    }

    public async Task<IReadOnlyList<NovaPessoaMsg>?> TryRead()
    {
        var entries =
            await db.StreamReadGroupAsync(StreamName, GroupName, Environment.MachineName, ">", 100);

        if (entries.Length is 0)
            return null;

        List<NovaPessoaMsg> resultado = new();

        foreach (var entry in entries)
        {
            var values = entry.Values.ToDictionary(x => x.Name.ToString(), x => x.Value);
            Pessoa pessoa = new(
                Id: Guid.Parse(values[nameof(NovaPessoa.Apelido)]!),
                Apelido: values[nameof(NovaPessoa.Apelido)]!,
                Nome: values[nameof(NovaPessoa.Nome)]!,
                Nascimento: DateOnly.Parse(values[nameof(NovaPessoa.Nascimento)]!),
                Stack: values[nameof(NovaPessoa.Nascimento)].ToString().Split(',')
            );
            resultado.Add(new(entry.Id, pessoa));
        }

        return resultado;
    }

    public async Task Ack(RedisValue id) =>
        await db.StreamAcknowledgeAsync(StreamName, GroupName, id);
}