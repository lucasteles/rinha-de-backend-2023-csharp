using StackExchange.Redis;

public sealed class Cache(IDatabase db)
{
    public Task<bool> TentaReservar(Pessoa pessoa) =>
        db.SetAddAsync(nameof(Pessoa.Apelido), pessoa.Apelido);

    public async Task Set(Pessoa pessoa)
    {
        var pessoaId = pessoa.Id.ToString();

        await db.HashSetAsync(
            pessoaId,
            new HashEntry[]
            {
                new(nameof(Pessoa.Id), pessoaId),
                new(nameof(Pessoa.Apelido), pessoa.Apelido),
                new(nameof(Pessoa.Nome), pessoa.Nome),
                new(nameof(Pessoa.Nascimento), pessoa.Nascimento.ToString("o")),
                new(nameof(Pessoa.Stack),
                    pessoa.Stack is null
                        ? RedisValue.EmptyString
                        : string.Join(',', pessoa.Stack)),
            });
    }

    public async Task<Pessoa?> Get(Guid id)
    {
        var hash = await db.HashGetAllAsync(id.ToString());

        if (hash.Length is 0) return null;

        var values = hash.ToDictionary(x => x.Name.ToString(), x => x.Value);
        var stackValue = values[nameof(Pessoa.Stack)];

        return new(
            Id: Guid.Parse(values[nameof(Pessoa.Id)]!),
            Apelido: values[nameof(Pessoa.Apelido)]!,
            Nome: values[nameof(Pessoa.Nome)]!,
            Nascimento: DateOnly.Parse(values[nameof(Pessoa.Nascimento)]!),
            Stack: string.IsNullOrWhiteSpace(stackValue)
                ? null
                : stackValue.ToString().Split(',')
        );
    }
}