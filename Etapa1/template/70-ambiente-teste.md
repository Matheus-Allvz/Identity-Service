# 70 · Contrato do ambiente de teste — <<Nome do Sistema>>

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** <<AUD>> · **Estado:** <<>> · **Atualizado em:** <<>>
> A suite de aceitação é escrita **antes** do código. Este contrato é o que o código gerado é obrigado a cumprir para que a suite corra contra ele.

## 1. Arranque e acesso
| Item | Especificação |
|---|---|
| Arranque | <<`docker compose up -d --build`>> |
| Pronto quando | <<`GET /health` = 200 em ≤ N s>> |
| Base URL e portas | <<>> |
| Comando de G1 (build + arranque + migração) | <<comando exato>> |
| Comando da suite | <<comando exato>> |
| Variáveis de ambiente de teste | <<nome = valor de teste>> |

## 2. Dados e estado
| Item | Especificação |
|---|---|
| Carga dos dados de partida | <<por exemplo, SQL direto antes de cada cenário>> |
| Limpeza entre cenários | <<>> |
| Cunhagem de tokens de teste | <<chave privada de teste, `kid`, claims mínimas>> |
| Relógio | <<como os testes controlam o tempo>> |
| Inspeção de efeitos | <<como os testes leem BD, cache e tópicos>> |
| Dados pessoais | **Proibidos.** Fixtures só com dados fictícios. Varredura obrigatória: <<comando>> |

## 3. Fakes dos sistemas externos
| Fake | Simula | Comportamentos implementados | Só do `[MEDIDO]`? | Endpoints de controlo |
|---|---|---|---|---|
| <<>> | <<EXT-nn>> | <<>> | <<Sim \| Não: lista de comportamentos [DOC] marcados como não verificados>> | <<por exemplo, `/_calls`, `/_faults`, `/_state`>> |

## 4. Injeção de falhas
O contrato distingue falha de infraestrutura (503) de erros do motor de dados com semântica de negócio (mapeados por SQLSTATE).

| Falha | Mecanismo | Comportamento esperado | CA que a usa |
|---|---|---|---|
| <<sistema externo indisponível>> | <<paragem de contentor / timeout>> | <<503 ERR-SYS-002>> | <<CA-nn.m>> |
| <<processo morto a meio>> | <<SIGKILL / crash>> | <<recuperação e consistência>> | <<>> |
| <<violação de isolamento / RLS>> | <<contexto com inquilino não autorizado>> | <<403 ERR-CTX-007 (SQLSTATE 42501)>> | <<>> |
| <<conflito de unicidade>> | <<inserção concorrente com mesma chave>> | <<409 ERR-CTX-005 (SQLSTATE 23505)>> | <<>> |

## 5. Convenções da suite
| Item | Convenção |
|---|---|
| Nome do teste de um CA | `CA_<nn>_<m>_<descricao>` |
| Nome do teste de uma borda | `CA_<nn>_<m>_Borda_<descricao>` |
| Agrupamento | <<uma classe ou ficheiro por fase: `F<nn>AceitacaoTests`>> |
| Timeout por teste / sessão | <<>> |
| Esperas | Só com espera ativa e teto (nunca `sleep` fixo) |
