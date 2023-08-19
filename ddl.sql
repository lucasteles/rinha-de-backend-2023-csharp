CREATE TABLE IF NOT EXISTS pessoas (
    id uuid PRIMARY KEY,
    apelido varchar(32) UNIQUE,
    nome varchar(100) NOT NULL,
    nascimento date NOT NULL,
    stack varchar(32)[] NULL,
    meta text NULL
);
CREATE INDEX IF NOT EXISTS busca_pessoa_index ON pessoas (meta text_pattern_ops);
