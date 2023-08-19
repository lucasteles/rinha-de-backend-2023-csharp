public sealed record Pessoa(
    Guid Id,
    string Apelido,
    string Nome,
    DateOnly Nascimento,
    string[]? Stack
);

public sealed record NovaPessoa(string Apelido, string Nome, string Nascimento, string[]? Stack);