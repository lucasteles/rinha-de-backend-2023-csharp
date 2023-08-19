using StackExchange.Redis;

public sealed class Cache(IDatabaseAsync db)
{
    public async Task<bool> Existe(string apelido) =>
        await db.SetContainsAsync(nameof(Pessoa.Apelido), apelido);

    public async Task Set(Pessoa pessoa)
    {
        var pessoaId = pessoa.Id.ToString();

        await Task.WhenAll(
            db.SetAddAsync(nameof(Pessoa.Apelido), pessoa.Apelido),
            db.HashSetAsync(
                pessoaId,
                new HashEntry[]
                {
                    new(nameof(Pessoa.Id), pessoaId),
                    new(nameof(Pessoa.Apelido), pessoa.Apelido),
                    new(nameof(Pessoa.Nome), pessoa.Nome),
                    new(nameof(Pessoa.Nascimento), pessoa.Nascimento.ToString("o")),
                    new(nameof(Pessoa.Stack),
                        pessoa.Stack is null ? null : string.Join(',', pessoa.Stack)),
                })
        );
    }

    public async Task<Pessoa?> Get(Guid id)
    {
        var hash = await db.HashGetAllAsync(id.ToString());

        if (hash.Length is 0) return null;

        var values = hash.ToDictionary(x => x.Name.ToString(), x => x.Value);

        return new(
            Id: Guid.Parse(values[nameof(NovaPessoa.Apelido)]!),
            Apelido: values[nameof(NovaPessoa.Apelido)]!,
            Nome: values[nameof(NovaPessoa.Nome)]!,
            Nascimento: DateOnly.Parse(values[nameof(NovaPessoa.Nascimento)]!),
            Stack: values[nameof(NovaPessoa.Nascimento)].ToString().Split(',')
        );
    }
}