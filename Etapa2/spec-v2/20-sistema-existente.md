# 20 · Sistema existente e integrações — Identity-Service

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** AR (UT-1-02) · **Estado:** Rascunho (ciclo 5) · **Atualizado em:** 2026-10-19
> O serviço é novo: não há código anterior a integrar. Dependências com comportamento a fixar: EXT-01 a EXT-03 (infraestrutura, medida). Consumidores com contrato: EXT-04 e EXT-05 (não medidos: lacunas). Toda a linha leva etiqueta de evidência (`00` §5).
> Cada `[MEDIDO]` cita o identificador da medição (`M1`, `R1`, `K1`) num script reprodutível de `evidencias/` e o ficheiro `.out.txt` com o resultado de 2026-10-19. Reproduzir: `bash evidencias/medir-postgres.sh`, `medir-redis.sh`, `medir-kafka.sh` (Docker 29.7.2).
> `Actuar-Projects/userservice-api` e `accountservice-api` são só material de estudo (Ideia Base §3.2), não sistemas externos; as citações `[CÓDIGO]` estão nos PRS-03, PRS-04 e PRS-07 (`90` §2.1).

## EXT-01 · PostgreSQL 17.6

| Campo | Valor |
|---|---|
| O que é | Base de dados centralizada (Ideia Base §1, ADR-05) |
| Usado pelos módulos | M-11 (esquema, políticas, `identity_app`), M-13 (interceptores das camadas 2 a 4) e os módulos com escrita ou leitura em tabelas |
| Ambiente onde foi medido | Contentor `postgres:17.6` (`PostgreSQL 17.6 (Debian 17.6-2.pgdg13+1)`), 2026-10-19 |
| Autenticação | Utilizador e palavra-passe (variável `POSTGRES_PASSWORD` da imagem) [MEDIDO] `medir-postgres.sh` |

### Comportamentos que importam
| # | Comportamento | Consequência na especificação | Evidência |
|---|---|---|---|
| 1 | O superutilizador ignora RLS mesmo com `FORCE ROW LEVEL SECURITY`: 2 de 2 linhas visíveis sem variáveis | Os pedidos correm como `identity_app` (`SET ROLE`), não como o dono (PRS-17) | [MEDIDO] M1, `pg-rls.sql` |
| 2 | Como `identity_app`: sem variáveis 0 linhas; 1 tenant em `app.allowed_tenants` 1 linha; texto vazio 0 linhas; `app.auth_flow = 'true'` 2 linhas | A política de `30` §5 filtra a leitura como pretendido | [MEDIDO] M2a a M2d |
| 3 | `INSERT` com `tenant_id` fora da lista: `new row violates row-level security policy`; `DELETE` sem `GRANT`: `permission denied` | Camada 4 (RG-09) e append-only (PRS-19) | [MEDIDO] M3, M3b |
| 4 | `SET LOCAL` fora de transação: `WARNING: SET LOCAL can only be used in transaction blocks` e valor vazio | O mecanismo da v1.0 não atuava; trocado por `set_config(nome, valor, false)` (PRS-17) | [MEDIDO] M4 |
| 5 | `DISCARD ALL` repõe o papel (`current_user` = `postgres`) e limpa `app.auth_flow` | Uma ligação reposta no pool não leva o contexto; o reset do Npgsql é L-04 | [MEDIDO] M5 |
| 6 | Índice único parcial `(family_id) WHERE status = 'ACTIVE'`: o segundo `ACTIVE` da família falha com `duplicate key value violates unique constraint "uq_refresh_token_family_active"` (SQLSTATE 23505) | Duas rotações concorrentes: só uma confirma (M-02) | [MEDIDO] M6, `pg-ddl.sql` |
| 7 | O DDL de `30` §3, §5 e §6 cria-se sem erros e rejeita, pela restrição nomeada: permissão fora do formato, com vírgula ou `NULL` (`ck_role_permissions`); `LOCKED` sem `locked_until`; `SINGLE` sem tenant; `member_role` com tenant diferente do membro; hash não Argon2id; `FAILED` com `retry_count = 4` | O modelo de `30` é executável e as restrições têm os nomes de `30` | [MEDIDO] M7 a M7h |
| 8 | Como `identity_app`: `UPDATE auth_audit_log` e `DELETE FROM tenant` falham com `permission denied` | Append-only e sem remoção física impostos pela BD (PRS-19) | [MEDIDO] M8a, M8b |
| 9 | Com `app.write_tenants` = {A, B} e `app.allowed_tenants` = {A}, `INSERT` em B é aceite; com `app.write_tenants` = {A}, `INSERT` em B falha com violação de política | O conjunto de escrita (PRS-51) permite EP-06 e EP-07 sem alargar a leitura | [MEDIDO] M9a, M9b |
| 10 | Com a política de `30` §5 (leitura inclui `app.write_tenants`), `INSERT com RETURNING` num tenant só do conjunto de escrita é aceite; com a política antiga (leitura só com `app.allowed_tenants`) falha com violação de política. Matriz completa: 33 casos OK | `USING` de `30` §5 corrigido (achado A-33) | [MEDIDO] `matriz-isolamento.sql` I11, I20, I21; `matriz-isolamento.out.txt` |

### Concorrência e falhas
| Situação | Comportamento observado | Evidência |
|---|---|---|
| Duas transações inserem o mesmo valor único | A segunda falha com SQLSTATE 23505 | [MEDIDO] M6 |
| Base de dados indisponível | `NpgsqlException` (não `PostgresException`) em 18 ms [MEDIDO] `Medicoes.L05_FalhasViaWaf` | `70` §4 |

## EXT-02 · Redis 7.4.6

| Campo | Valor |
|---|---|
| O que é | Denylist distribuída (Ideia Base §6.2) |
| Usado pelos módulos | M-00 (dono de `IDenylist`); escrevem por ela: M-01, M-02, M-03, M-09, M-10, M-12; lê: M-08 |
| Ambiente onde foi medido | Contentor `redis:7.4.6` (`redis_version:7.4.6`), 2026-10-19 |
| Autenticação | Nenhuma na configuração por omissão da imagem (`PING` sem palavra-passe → `PONG`) [MEDIDO] R0, `medir-redis.sh` |

### Comportamentos que importam
| # | Comportamento | Consequência na especificação | Evidência |
|---|---|---|---|
| 1 | `SET k v EX 900` seguido de `SET k v EX 50` deixa `TTL k = 50` | Um novo `SET` substitui o TTL; os módulos escrevem sempre o TTL completo (`30` §4) | [MEDIDO] R1 |
| 2 | `MGET` de chave existente e inexistente devolve `1` e vazio, pela ordem pedida | RG-04 e RG-08 consultam todo o `allowed_tenants` num só comando | [MEDIDO] R2 |
| 3 | `SET k v EX 0` falha: `ERR invalid expire time in 'set' command` | TTL mínimo 1 s em `denylist:jti` (`max(1, exp − agora)`) | [MEDIDO] R3 |
| 4 | `DEL` de chave inexistente devolve `0`, sem erro | Reativação repetida de tenant é inócua | [MEDIDO] R4 |

### Concorrência e falhas
| Situação | Comportamento observado | Evidência |
|---|---|---|
| Redis indisponível | `RedisConnectionException` em 5,5 s [MEDIDO] `Medicoes.L05_FalhasViaWaf`; resposta 503 (PRS-16) | `70` §4 |

## EXT-03 · Apache Kafka 3.9.1

| Campo | Valor |
|---|---|
| O que é | Barramento de eventos (Ideia Base §1, §7.1) |
| Usado pelos módulos | M-14 (publicação) |
| Ambiente onde foi medido | Contentor `apache/kafka:3.9.1`, KRaft de nó único, configuração por omissão da imagem, 2026-10-19 |
| Autenticação | Nenhuma: listener `PLAINTEXT` em `localhost:9092` [MEDIDO] K1, `medir-kafka.sh` |

### Comportamentos que importam
| # | Comportamento | Consequência na especificação | Evidência |
|---|---|---|---|
| 1 | `auto.create.topics.enable=true`, `num.partitions=1` | O primeiro envio cria o tópico com 1 partição; o serviço não cria tópicos | [MEDIDO] K1 |
| 2 | O primeiro envio para tópico inexistente regista `UNKNOWN_TOPIC_OR_PARTITION` (recuperável) e é aceite. Duração do produtor de consola (inclui arranque da JVM): 1 688 ms no 1.º envio, 1 570 ms no 2.º | O M-14 não trata esse aviso como falha; a criação do tópico acrescentou cerca de 120 ms, dentro dos 3 s do RNF-05 | [MEDIDO] K2, K3 |

### Concorrência e falhas
| Situação | Comportamento observado | Evidência |
|---|---|---|
| Broker indisponível | [MEDIDO] `evidencias/env70`, `Medicoes.L03_Kafka`: com `MessageTimeoutMs = 3000`, erro `Local_MsgTimedOut` em 3,0 s; recupera após o broker voltar | `70` §4 |
| Publicação repetida | Não medido; os consumidores deduplicam por `event_id` | [DOC] Ideia Base §7.1 (at-least-once); L-02 |

## EXT-04 · API Gateway (consumidor)

| Campo | Valor |
|---|---|
| O que é | Ponto de entrada REST (Ideia Base §1) |
| Usado pelos módulos | Consome EP-10 (M-07) e lê `denylist:*` (M-00) |
| Ambiente onde foi medido | Não medido: o Gateway não existe nem tem documentação própria |

### Contratos usados
| ID | Operação | Resposta | Evidência |
|---|---|---|---|
| EXT-04.1 | Gateway obtém `GET /.well-known/jwks.json` | JWKS RFC 7517 (`40`) | [DOC] Ideia Base §6.2, §6.5 |
| EXT-04.2 | Gateway lê `denylist:jti`, `denylist:user`, `denylist:tenant` em cada pedido autenticado | Chaves de `30` §4 | [DOC] Ideia Base §6.2.2 |

### Lacunas
| ID | O que não se sabe | Porque não foi verificado | Risco | Quem pode responder |
|---|---|---|---|---|
| L-01 | Se o Gateway repassa o JWT, que cache de JWKS usa, como reage a `kid` desconhecido e se compara `denylist:user` com `iat` como a RG-07 | Gateway inexistente | Revogação diferente na borda e no serviço | PO |

## EXT-05 · Microsserviços de domínio (consumidores)

| Campo | Valor |
|---|---|
| O que é | `Produtos`, `Fornecimentos`, `Demanda`, `Mercado`, `Negociação/Leilão`, `Pedidos`, `Logística/Frete`, `Transportadoras` (Ideia Base §1) |
| Usado pelos módulos | Consomem claims (`10` §3.5) e EV-01 a EV-04 (M-14) |
| Ambiente onde foi medido | Não medido: serviços inexistentes |

### Lacunas
| ID | O que não se sabe | Porque não foi verificado | Risco | Quem pode responder |
|---|---|---|---|---|
| L-02 | Validação de `context_jws` e deduplicação por `event_id` nos consumidores | Serviços inexistentes | Só a jusante; não afeta CA deste serviço | PO |

## Lacunas de infraestrutura
| ID | O que não se sabe | Porque não foi verificado | Risco | Quem pode responder |
|---|---|---|---|---|
| L-03 | Fechada em UT-2-03: `Testcontainers.Kafka@4.16.0` arranca `apache/kafka:3.9.1` [MEDIDO] `Medicoes.L03_Kafka` | — | — | — |
| L-04 | Fechada em UT-2-03: o pool do Npgsql repõe o papel e as variáveis ao devolver a ligação [MEDIDO] `Medicoes.L04_PoolReset` | — | — | — |
| L-05 | Parcial: o driver foi medido (`Medicoes.L05_FalhasViaWaf`: Redis parado → `RedisConnectionException` em 5,5 s; PostgreSQL parado → `NpgsqlException` em 18 ms); a resposta 503 do serviço verifica-se nos CA de M-00 | Requer a implementação | PRS-16 | UT-3-00 |
| L-06 | Fechada em UT-2-90: `MessageTimeoutMs = 3000` (PRS-63) [MEDIDO] `Medicoes.L03_Kafka` | — | — | — |
