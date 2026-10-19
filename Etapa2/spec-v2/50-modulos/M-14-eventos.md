# M-14 · Eventos

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR · **Estado:** Proposta (UT-3-14) · **Atualizado em:** 2026-10-19
> **Pacote de contexto deste módulo:** este ficheiro + `10` (base + extras §3.7 e §5) + `30` (comum, ENT-10, enums) + `40` (§1, §6, EV-01 a EV-04, IF-05.1, IF-07, IF-08.1, tipos partilhados) + `70` (comum) + `20` (EXT-03) + `90` (UT-3-14, PRS-09, PRS-42, PRS-43). Tamanho total: 39.0 KiB (orçamento: 60 KiB).

## 1. Âmbito do módulo

| Dentro | Fora (outro módulo ou fora do sistema) |
|---|---|
| Implementação da interface `IEventOutbox` (`IF-07`) | Decisão de negócio que dispara o evento (M-01, M-02, M-04, M-10, M-12) |
| Inserção transacional em `outbox_message` (ENT-10) | Confirmação da transação de base de dados (`SaveChangesAsync`) |
| Montagem de cabeçalhos Kafka e assinatura do token `context_jws` | Consumo ou validação de eventos por microsserviços externos |
| Worker para publicação de eventos pendentes no Apache Kafka | Gestão do cluster Kafka ou criação física de tópicos (EXT-03) |
| Controlo de retentativas, loteamento e transição de estado da outbox | Exposição de endpoints HTTP (nenhum endpoint em M-14) |

**Escreve:** `outbox_message` (ENT-10) · **Lê:** `outbox_message` (ENT-10) · **Usa interfaces de:** M-00 (`IF-05`, `IF-08`)

## 2. Endpoints e eventos (detalhe)

O módulo M-14 não expõe endpoints HTTP (Total EP = 0). O módulo é dono da publicação de eventos no Apache Kafka a partir da tabela `outbox_message`.

### EV-01 `empresa_cadastrada`
- **Gatilho:** M-04 (RF-07: criação de tenant via EP-06 com status 201)
- **Chave de partição:** `tenant_id` (string minúscula formato D)
- **RN aplicáveis:** RN-160, RN-161, RN-162, RN-163, RN-167, RN-168, RN-169
- **Payload:** `event_id` (UUID v7), `tenant_id`, `group_id`, `cnpj`, `legal_name`, `branch_type` (`HQ`/`BRANCH`), `geo_lat`, `geo_lng`, `status` (`"ACTIVE"`), `scopes` (array), `created_at` (ISO 8601 UTC).

### EV-02 `tenant_bloqueado`
- **Gatilho:** M-12 (RF-09: suspensão de tenant via EP-08)
- **Chave de partição:** `tenant_id` (string minúscula formato D)
- **RN aplicáveis:** RN-160, RN-161, RN-162, RN-163, RN-167, RN-168, RN-169
- **Payload:** `event_id` (UUID v7), `tenant_id`, `group_id`, `cnpj`, `status` (`"SUSPENDED"`), `reason`, `blocked_by_user_id` (`sub` do chamador), `blocked_at` (ISO 8601 UTC).

### EV-03 `usuario_bloqueado`
- **Gatilho:** M-01 (RF-01: bloqueio por tentativas excedidas `LOCKED`); M-10 (RF-10: suspensão `SUSPENDED` via EP-09)
- **Chave de partição:** `user_id` (string minúscula formato D)
- **RN aplicáveis:** RN-160, RN-161, RN-162, RN-164, RN-167, RN-168, RN-169
- **Payload:** `event_id` (UUID v7), `user_id`, `status` (`"LOCKED"` ou `"SUSPENDED"`), `locked_until` (texto em `LOCKED`, literal `null` em `SUSPENDED`), `reason` (fixo em `LOCKED` via PRS-60; motivo recebido em `SUSPENDED`), `blocked_at` (ISO 8601 UTC).

### EV-04 `sessao_comprometida`
- **Gatilho:** M-02 (RF-04: deteção de reuso de refresh token via EP-03)
- **Chave de partição:** `user_id` (string minúscula formato D)
- **RN aplicáveis:** RN-160, RN-161, RN-162, RN-165, RN-167, RN-168, RN-169
- **Payload:** `event_id` (UUID v7), `user_id`, `family_id`, `reused_refresh_token_id`, `ip_address`, `detected_at` (ISO 8601 UTC).

---

### IF-07 `IEventOutbox`

Interface interna consumida pelos módulos para enfileirar eventos na transação corrente.

| Operação | Assinatura | Gatilho |
|---|---|---|
| `IF-07.1` | `Guid EnqueueTenantCreated(TenantCreatedPayload payload)` | EV-01 |
| `IF-07.2` | `Guid EnqueueTenantBlocked(TenantBlockedPayload payload)` | EV-02 |
| `IF-07.3` | `Guid EnqueueUserLocked(Guid userId, DateTimeOffset lockedUntil, DateTimeOffset blockedAt)` | EV-03 (`LOCKED`) |
| `IF-07.4` | `Guid EnqueueUserSuspended(Guid userId, string reason, DateTimeOffset blockedAt)` | EV-03 (`SUSPENDED`) |
| `IF-07.5` | `Guid EnqueueSessionCompromised(SessionCompromisedPayload payload, FamilyContext family)` | EV-04 |

**Cabeçalhos Kafka padronizados:**
- `event_id`: UUID string (sempre presente, igual a `outbox_message.id`).
- `tenant_id`, `group_id`: UUID string (presentes apenas se houver valor; ausentes se nulos).
- `initiator_user_id`: UUID string (sempre presente).
- `actor_user_id`: UUID string (presente em personificação; ausente se nulo).
- `acting_scope`: `ActingScope` (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`, `NONE`).
- `correlation_id`: UUID string da requisição HTTP de origem.
- `context_jws`: JWS compacto RS256 com claims (`event_id`, `tenant_id`, `group_id`, `initiator_user_id`, `actor_user_id`, `acting_scope`, `view_mode`, `allowed_tenants`, `correlation_id`, `iat`, `exp = iat + 300`). Nulos representados por literal `null`.

## 3. Requisitos funcionais

### RF-14 — Publicação transacional de eventos de identidade

| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Invocação dos métodos de `IF-07` e ciclo periódico do worker outbox |
| Pré-condições | `IdentityDbContext` com transação ativa; `ISigningKey` disponível; `KAFKA_BOOTSTRAP_SERVERS` configurado |
| RN | RN-160, RN-161, RN-162, RN-163, RN-164, RN-165, RN-166, RN-167, RN-168, RN-169 |

**Fluxo principal:**
1. O módulo chamador invoca método de `IEventOutbox` (`IF-07`) na transação ativa da operação.
2. A operação gera `event_id` (UUID v7), resolve o contexto via `IAuthContext` ou `FamilyContext` e assina `context_jws` com RS256.
3. A operação adiciona à transação a entidade `outbox_message` com status `PENDING`, `retry_count = 0` e chave de partição definida.
4. O chamador executa `SaveChangesAsync` persistindo o evento atomicamente com os dados de negócio.
5. A cada 500 ms (`OUTBOX_POLLING_INTERVAL_MS`), o worker seleciona até 50 registos `PENDING` por ordem `created_at ASC`, `id ASC`.
6. O worker envia cada mensagem ao Kafka via `Confluent.Kafka` com `Acks = All` e `MessageTimeoutMs = 3000`.
7. Após confirmação do broker, o worker atualiza `status = PUBLISHED`, preenche `processed_at = now` e grava a alteração.

**Fluxos de exceção**

| ID | Passo | Condição | Ação / Resposta | Efeitos / estado final |
|---|---|---|---|---|
| EX-14.1 | 2 | Falha ao assinar `context_jws` | Propaga exceção criptográfica | Transação desfeita com 503 `ERR-SYS-002` |
| EX-14.2 | 6 | Envio ao Kafka falha com `retry_count + 1 < 5` | Grava `retry_count = retry_count + 1` | Mensagem permanece `PENDING` para novo ciclo |
| EX-14.3 | 6 | Envio ao Kafka falha com `retry_count + 1 = 5` | Grava `status = FAILED`, `retry_count = 5` | `processed_at` preenchido; envio interrompido |

**Concorrência, idempotência e retomada**

| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Inserções concorrentes na outbox | Cada mensagem recebe `id` UUID v7 único e é persistida sem contenção na transação do chamador | CA-14.1 |
| Queda entre confirmação Kafka e update na base | Ao reiniciar, mensagem é reenviada (*at-least-once*); consumidor desduplica por `event_id` | CA-14.1 |
| Broker Kafka indisponível durante ciclo | Worker captura falha, incrementa `retry_count`, mantém processo vivo e retoma no próximo ciclo (C7) | CA-14.7, CA-14.8 |
| Retoma após restabelecimento do broker | Mensagens `PENDING` pendentes são enviadas respeitando `created_at ASC`, `id ASC` | CA-14.8 |

**Pós-condições:**
- Eventos confirmados assumem `PUBLISHED` com `processed_at` preenchido em tempo ≤ 3 s (RNF-05).
- Eventos com 5 falhas consecutivas assumem `FAILED` com `processed_at` preenchido.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-160 | Integridade | **SE** método de `IF-07` for invocado **ENTÃO** `outbox_message` **DEVE** ser adicionada à transação com `id = event_id` (UUID v7), `status = 'PENDING'`, `retry_count = 0` e `processed_at = null` **SENÃO** evento não é persistido | – | – | RF-14 | ENT-10, PRS-43 |
| RN-161 | Integridade | **SE** evento for gerado **ENTÃO** cabeçalho `context_jws` **DEVE** ser assinado com RS256 contendo `kid = KeyId` de `ISigningKey` e claims com `exp = iat + 300` **SENÃO** enfileiramento falha | `EXP_OFFSET = 300 s` | `ERR-SYS-002` | RF-14 | ADR-09, PRS-09 |
| RN-162 | Validação | **SE** cabeçalho Kafka possuir valor não nulo **ENTÃO** **DEVE** ser incluído em texto UTF-8 (UUID minúsculo formato D) **SENÃO** cabeçalho **DEVE** ser omitido da mensagem | – | – | RF-14 | 40 §6, PRS-56 |
| RN-163 | Integridade | **SE** evento for EV-01 ou EV-02 **ENTÃO** `partition_key` **DEVE** ser `tenant_id` e contexto derivado de `IAuthContext` **SENÃO** enfileiramento falha | – | – | RF-14 | 40 §6, PRS-42 |
| RN-164 | Integridade | **SE** evento for EV-03 de login (EP-01) **ENTÃO** `tenant_id` e `group_id` **DEVEM** ser omitidos e `acting_scope = 'NONE'`; SE de suspensão (EP-09) **ENTÃO** derivam de `IAuthContext` **SENÃO** falha | – | – | RF-14 | 40 §6, PRS-42 |
| RN-165 | Integridade | **SE** evento for EV-04 **ENTÃO** `partition_key` **DEVE** ser `user_id` e cabeçalhos provêm de `FamilyContext` com `actor_user_id` omitido **SENÃO** enfileiramento falha | – | – | RF-14 | 40 §6, PRS-42 |
| RN-166 | Temporal | **SE** worker outbox executar ciclo **ENTÃO** **DEVE** selecionar até 50 registos `PENDING` ordenados por `created_at ASC`, `id ASC` a cada 500 ms **SENÃO** ciclo aguarda | `BATCH = 50`, `INTERVAL = 500 ms` | – | RF-14 | PRS-43 |
| RN-167 | Integridade | **SE** mensagem for enviada ao Kafka **ENTÃO** produtor `Confluent.Kafka` **DEVE** utilizar `Acks = All` e `MessageTimeoutMs = 3000` **SENÃO** envio falha | `TIMEOUT = 3000 ms`, `ACKS = All` | – | RF-14 | 10 §3.7, PRS-63 |
| RN-168 | Temporal | **SE** broker Kafka confirmar receção **ENTÃO** worker **DEVE** atualizar `status = 'PUBLISHED'`, `processed_at = now` mantendo `retry_count` **SENÃO** atualização não ocorre | – | – | RF-14 | ENUM-09, PRS-43 |
| RN-169 | Integridade | **SE** envio ao Kafka falhar e `retry_count + 1 < 5` **ENTÃO** `retry_count` **DEVE** ser incrementado mantendo `PENDING`; SE `retry_count + 1 = 5` **ENTÃO** **DEVE** gravar `status = 'FAILED'`, `retry_count = 5` e `processed_at = now` **SENÃO** falha | `MAX_RETRIES = 5` | – | RF-14 | ENT-10, ENUM-09, PRS-43 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-14 — Publicação transacional de eventos de identidade

  Contexto:
    Dado que a base de dados possui tabela outbox_message sem pendências
    E que ISigningKey possui KeyId "key-2026-01" e par de chaves RSA 2048 bits
    E que o broker Kafka está ativo com timeout 3000 ms e Acks All

  @CA-14.1 @RF-14 @RN-160 @RN-161 @RN-162 @RN-163 @RN-167 @RN-168 @MUST
  Cenário: Enfileiramento e publicação de EV-01 empresa_cadastrada
    Dado a execução de IF-07.1 com TenantId "0192e2c0-0010-7000-8000-000000000010", GroupId "0192e2c0-0001-7000-8000-000000000001" e CNPJ "99000001000101"
    E que IAuthContext possui UserId "0192e2c0-0099-7000-8000-000000000099", ActiveScope "TENANT_ADMIN" e Act nulo
    Quando o chamador executa SaveChangesAsync e o worker outbox processa o lote
    Então outbox_message assume status "PUBLISHED", retry_count 0 e processed_at preenchido
    E o tópico "empresa_cadastrada" recebe mensagem com partition_key "0192e2c0-0010-7000-8000-000000000010"
    E a mensagem contém header "acting_scope" com valor "TENANT_ADMIN" e header "actor_user_id" ausente
    E o cabeçalho "context_jws" possui assinatura RS256 válida com exp = iat + 300

  @CA-14.2 @RF-14 @RN-161 @RN-162 @RN-163 @RN-168 @MUST
  Cenário: Publicação de EV-02 tenant_bloqueado com personificação ativa
    Dado a execução de IF-07.2 para TenantId "0192e2c0-0020-7000-8000-000000000020" e GroupId "0192e2c0-0001-7000-8000-000000000001"
    E que IAuthContext possui UserId "0192e2c0-0005-7000-8000-000000000005" e ActorUserId "0192e2c0-0009-7000-8000-000000000009"
    Quando a transação é confirmada e o worker outbox processa o lote
    Então o tópico "tenant_bloqueado" recebe mensagem com partition_key "0192e2c0-0020-7000-8000-000000000020"
    E o header "initiator_user_id" é "0192e2c0-0005-7000-8000-000000000005" e "actor_user_id" é "0192e2c0-0009-7000-8000-000000000009"
    E a outbox_message correspondente assume status "PUBLISHED"

  @CA-14.3 @RF-14 @RN-162 @RN-164 @RN-168 @MUST
  Cenário: Publicação de EV-03 usuario_bloqueado por tentativas de login incorretas
    Dado a execução de IF-07.3 para UserId "0192e2c0-0003-7000-8000-000000000003" com status LOCKED
    Quando a transação é confirmada e o worker outbox processa o lote
    Então o tópico "usuario_bloqueado" recebe mensagem com partition_key "0192e2c0-0003-7000-8000-000000000003"
    E os headers "tenant_id", "group_id" e "actor_user_id" estão ausentes da mensagem
    E o header "acting_scope" possui o valor "NONE"
    E o payload contém status "LOCKED" e reason "Excesso de tentativas consecutivas de login com palavra-passe incorreta"

  @CA-14.4 @RF-14 @RN-162 @RN-164 @RN-168 @MUST
  Cenário: Publicação de EV-03 usuario_bloqueado por suspensão administrativa
    Dado a execução de IF-07.4 para UserId "0192e2c0-0003-7000-8000-000000000003" com reason "Violacao de politica"
    E que IAuthContext possui UserId "0192e2c0-0001-7000-8000-000000000001", GroupId "0192e2c0-0002-7000-8000-000000000002" e ActiveTenantId nulo
    Quando a transação é confirmada e o worker outbox processa o lote
    Então o tópico "usuario_bloqueado" recebe mensagem com partition_key "0192e2c0-0003-7000-8000-000000000003"
    E o header "group_id" é "0192e2c0-0002-7000-8000-000000000002" e "tenant_id" está ausente
    E o payload contém status "SUSPENDED" e locked_until nulo

  @CA-14.5 @RF-14 @RN-161 @RN-162 @RN-165 @RN-168 @MUST @critico
  Cenário: Publicação de EV-04 sessao_comprometida a partir de FamilyContext
    Dado a execução de IF-07.5 com FamilyContext contendo UserId "0192e2c0-0007-7000-8000-000000000007", GroupId "0192e2c0-0001-7000-8000-000000000001", ActiveTenantId "0192e2c0-0010-7000-8000-000000000010" e ActiveScope "BUYER"
    Quando a transação é confirmada e o worker outbox processa o lote
    Então o tópico "sessao_comprometida" recebe mensagem com partition_key "0192e2c0-0007-7000-8000-000000000007"
    E o header "acting_scope" é "BUYER" e o header "actor_user_id" está ausente
    E o claim "allowed_tenants" de "context_jws" contém as claims resolvidas da família

  @CA-14.6 @RF-14 @RN-166 @RN-167 @RN-168 @MUST
  Esquema do Cenário: Limiar temporal de publicação da outbox RNF-05 em lote
    Dado que existem <qtd> mensagens em outbox_message com status "PENDING"
    Quando o worker outbox executa com intervalo de 500 ms
    Então todas as <qtd> mensagens transitam para status "PUBLISHED" no tempo limite de 3000 ms
    Exemplos:
      | qtd |
      | 1   |
      | 50  |

  @CA-14.7 @RF-14 @RN-167 @RN-169 @MUST @critico
  Esquema do Cenário: Ciclo de retentativas e transição para FAILED após 5 tentativas falhadas
    Dado que existe uma mensagem PENDING com retry_count inicial <retry_inicial>
    E que o broker Kafka rejeita a publicação da mensagem
    Quando o worker outbox tenta processar o envio
    Então a mensagem em outbox_message assume status <status_final> e retry_count <retry_final>
    Exemplos:
      | retry_inicial | status_final | retry_final |
      | 0             | "PENDING"    | 1           |
      | 3             | "PENDING"    | 4           |
      | 4             | "FAILED"     | 5           |

  @CA-14.8 @RF-14 @RN-167 @RN-169 @MUST @critico
  Cenário: Resiliência do worker sob indisponibilidade do broker Kafka
    Dado que o broker Kafka está pausado
    E que existem 2 mensagens em outbox_message com status "PENDING"
    Quando o worker outbox processa o ciclo de envio
    Então o worker captura o erro de conexão sem abortar a aplicação
    E as mensagens têm retry_count incrementado em 1 mantendo status "PENDING"
    Quando o broker Kafka retoma a operação normal e ocorre novo ciclo
    Então as mensagens são publicadas com sucesso e transitam para "PUBLISHED"
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-14 | MUST | RN-160, RN-161, RN-162, RN-163, RN-164, RN-165, RN-166, RN-167, RN-168, RN-169 | EV-01, EV-02, EV-03, EV-04 | EX-14.1, EX-14.2, EX-14.3 | CA-14.1, CA-14.2, CA-14.3, CA-14.4, CA-14.5, CA-14.6, CA-14.7, CA-14.8 | F-00 |

**Totais:** RF = 1 · RN = 10 · EP = 0 · EV = 4 · CA = 8 (`@critico` = 3) · Σw = 24

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-320 | O serializador Kafka utiliza codificação UTF-8 estrita para payloads JSON e headers | Interoperabilidade com consumidores externos (10 §3.3) | PO, 2026-10-19 |
| PRS-321 | Mensagens com status `FAILED` não são reprocessadas pelo worker ordinário | Evitar envenenamento de fila após 5 tentativas falhadas (ENT-10, PRS-43) | PO, 2026-10-19 |
| PRS-322 | A validação de deduplicação e inspeção de `context_jws` é dos consumidores | Desacoplamento entre produtor e consumidores (PRS-14, L-02) | PO, 2026-10-19 |
