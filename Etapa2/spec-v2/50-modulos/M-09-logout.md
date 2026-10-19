# M-09 · Logout

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-09) · **Estado:** Rascunho (ciclo 5) · **Atualizado em:** 2026-10-19
> **Pacote de contexto deste módulo:** este ficheiro + `10` (base §1–§2, §3 comum, §4, §6–§10) + `30` (comum §1–§3, ENUM-06, AUD.M09) + `40` (§1–§3, EP-04, IF-01.3, IF-03.1, IF-04.1, IF-08.1, tipos partilhados) + `70` (comum §1). Tamanho total: 36.9 KiB (orçamento: 60 KiB).

## 1. Âmbito do módulo

| Dentro | Fora (outro módulo ou fora do sistema) |
|---|---|
| Revogação imediata de token JWT via chave Redis `denylist:jti:{jti}` com TTL = `max(1, exp − agora)`. | Emissão e assinatura de tokens JWT (M-01, M-02, M-03, M-06). |
| Revogação atómica da família de refresh tokens (`fid`) em `refresh_token` se `fid` presente no token. | Rotação e consumo de refresh tokens (M-02). |
| Registo de auditoria `LOGOUT` em `auth_audit_log` na mesma transação de base de dados. | Autenticação transversal no pipeline HTTP (M-00). |
| Recepção idempotente de requisições de logout com corpo de 0 bytes ou `{}`. | Publicação Kafka via outbox (M-14). |
| Encerramento de sessões normais (com `fid`) e impersonation (com `act`, sem `fid`). | Suspensão global de utilizadores ou tenants (M-04, M-05, M-06, M-12). |

**Escreve:** Nenhuma (escreve em `refresh_token` via `IF-01.3` e em `auth_audit_log` via `IF-03.1`; grava no Redis via `IF-04.1`) · **Lê:** Nenhuma (obtém dados de autenticação via `IF-08.1`) · **Usa interfaces de:** M-00 (`IF-03.1`, `IF-04.1`, `IF-08.1`), M-02 (`IF-01.3`)

## 2. Endpoints e eventos (detalhe)

### EP-04 `POST /api/v1/auth/logout`

| Campo | Valor |
|---|---|
| RF | RF-05 |
| Autenticação / Permissão | JWT em `Authorization: Bearer <jwt>`. Qualquer token válido emitido pelo serviço (`view_mode` = `NONE`, `SINGLE`, `MACRO_GROUP` ou sob `act`). Todos os escopos e papéis de plataforma (`40` §5) |
| Idempotência | Sim. `SET` no Redis é idempotente. `IF-01.3` atualiza status `ACTIVE`/`CONSUMED` para `REVOKED`; se já revogados, retorna 0 sem erro. Chamadas posteriores com mesmo token recebem 401 `ERR-AUTH-002` (RG-06) |
| RN aplicáveis | RN-110, RN-111, RN-112, RN-113, RN-114, RN-115, RN-116 |

**Request**

| Campo | Local | Tipo | Obrigatório | Validação (formato, mín/máx, enum) |
|---|---|---|---|---|
| `Authorization` | header | string | Sim | Prefixo `Bearer ` seguido de espaço e JWT compacto válido |
| `Content-Type` | header | string | Não | Obrigatório se corpo ≥ 1 byte: `application/json` (parâmetro opcional `; charset=utf-8`). Sem corpo (0 bytes), não verificado |
| `X-Correlation-Id` | header | string | Não | UUID formato `D` (36 caracteres). Ausente ou inválido → novo UUID v7 gerado |
| corpo | body | object | Não | Vazio (0 bytes) ou `{}`. Propriedades presentes, tipo não objeto ou bytes inválidos → 400 `ERR-VAL-001`. Máximo 65 536 bytes |

```json
{}
```

**Ordem de avaliação** (a primeira que falha determina a resposta)

| # | Verificação | Camada (`40` §2) | Se falhar |
|---|---|---|---|
| 1 | Rota existe e método HTTP é `POST` | Encaminhamento (0) | 404 `ERR-REQ-001`; 405 `ERR-REQ-002` |
| 2 | Tamanho do corpo ≤ 65 536 bytes | Transporte (1) | 413 `ERR-REQ-003` |
| 3 | Se corpo ≥ 1 byte: `Content-Type` application/json (parâmetro opcional `charset=utf-8`) | Transporte (1) | 415 `ERR-REQ-004` |
| 4 | Obtenção da chave em `ISigningKey` operacional | Autenticação (2) | 503 `ERR-SYS-002` |
| 5 | Token presente, formato `Bearer`, assinatura válida, `exp > agora` e sem `denylist:jti` (RG-06) | Autenticação (2) | 401 `ERR-AUTH-002` |
| 6 | Chave `denylist:user` de `sub` (e `act.sub`) ausente ou < `iat` (RG-07) | Autenticação (2) | 403 `ERR-AUTH-004` |
| 7 | Chave `denylist:tenant` ausente para todos em `allowed_tenants` (RG-08) | Autenticação (2) | 403 `ERR-CTX-002` |
| 8 | Se corpo ≥ 1 byte: UTF-8 válido, JSON válido e raiz é objeto JSON | Formato (3) | 400 `ERR-VAL-001` |
| 9 | Se corpo for objeto JSON: possui 0 propriedades (RN-110) | Formato (3) | 400 `ERR-VAL-001` |
| 10 | Matriz de autorização: sem condição restritiva | Autorização (4) | Nenhuma |
| 11 | Conectividade PostgreSQL e Redis operacional | Regras de negócio (6) | 503 `ERR-SYS-002` |
| 12 | Revogação de família (IF-01.3 se `fid`), auditoria `LOGOUT` (IF-03.1), denylist:jti (IF-04.1) e commit na BD (RN-111 a RN-115) | Regras de negócio (6) | 204 sem corpo |

**Respostas** (incluindo as do middleware)

| Status | Condição | Código |
|---|---|---|
| 204 | Logout concluído. Token revogado no Redis; família (se `fid`) e auditoria persistidas | – |
| 400 | JSON malformado, raiz não objeto ou objeto com propriedades | `ERR-VAL-001` |
| 401 | Token ausente, assinatura inválida, expirado (`exp ≤ agora`) ou `jti` em `denylist:jti` (RG-06) | `ERR-AUTH-002` |
| 403 | Utilizador com `denylist:user` ≥ `iat` (RG-07) | `ERR-AUTH-004` |
| 403 | Tenant em `allowed_tenants` com `denylist:tenant` ativo (RG-08) | `ERR-CTX-002` |
| 404 | Rota não encontrada | `ERR-REQ-001` |
| 405 | Método HTTP diferente de `POST` | `ERR-REQ-002` |
| 413 | Corpo > 65 536 bytes | `ERR-REQ-003` |
| 415 | Corpo ≥ 1 byte e `Content-Type` ausente ou ≠ `application/json; charset=utf-8` | `ERR-REQ-004` |
| 500 | Erro inesperado no servidor | `ERR-SYS-001` |
| 503 | PostgreSQL, Redis ou `ISigningKey` indisponível | `ERR-SYS-002` |

**Corpo de sucesso**

| Campo | Tipo | Sempre presente | Valor / regra de cálculo | Ordenação |
|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | 204 sem corpo (0 bytes) | Não se aplica |

```text
(Sem corpo - 0 bytes)
```

**Efeitos colaterais (por ordem):**
1. Se claim `fid` presente (`IAuthContext.FamilyId ≠ null`), executa `ISessionStore.RevokeFamilyAsync(familyId)` (`IF-01.3`) na transação da base de dados: status vira `REVOKED` e `revoked_at = agora`. Se `fid` nula, omite o passo.
2. Executa `IAuditLog.Append(AuditEventType.LOGOUT, userId, actorUserId, tenantId, groupId, ticketId, null)` (`IF-03.1`) na mesma transação (`metadata = {}`).
3. Grava no Redis `denylist:jti:{jti}` = `"1"` com TTL = `Math.Max(1, exp − agora)` segundos via `IDenylist.RevokeJtiAsync(jti, exp)` (`IF-04.1`), antes do commit e antes do envio do 204.
4. Confirma a transação na base de dados (`IdentityDbContext.SaveChangesAsync()`).

**Transacionalidade:**
Revogação em `refresh_token` (`IF-01.3`) e auditoria em `auth_audit_log` (`IF-03.1`) partilham a mesma transação no `IdentityDbContext`. A gravação no Redis (`IF-04.1`) ocorre antes do commit (PRS-15, UT-3-09). Em caso de falha no Redis ou no commit, a transação na base de dados é revertida integralmente e retorna 503 `ERR-SYS-002`.

### EV (Eventos)

| Campo | Tipo | Obrigatório | Valor |
|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | M-09 não define eventos Kafka diretamente (publicação via outbox pertence a M-14) |

## 3. Requisitos funcionais

### RF-05 — Efetuar logout de sessão

| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Utilizador autenticado (qualquer papel ou escopo, com ou sem impersonation) / Requisição HTTP `POST /api/v1/auth/logout` |
| Pré-condições | JWT válido em `Authorization: Bearer <jwt>`, assinado por chave RSA do serviço, `exp > agora` e sem revogação prévia na denylist do Redis |
| RN | RN-110, RN-111, RN-112, RN-113, RN-114, RN-115, RN-116 |

**Fluxo principal:**
1. Cliente envia `POST /api/v1/auth/logout` com `Authorization: Bearer <jwt>`, corpo de 0 bytes ou `{}`.
2. Middleware valida assinatura e avalia RG-06, RG-07 e RG-08 no Redis, populando `IAuthContext`.
3. Endpoint valida que o corpo é vazio ou `{}` (RN-110).
4. Módulo inicia transação de base de dados no `IdentityDbContext`.
5. Se `fid` presente, invoca `ISessionStore.RevokeFamilyAsync(familyId)` (`IF-01.3`) para revogar tokens da família (RN-112, RN-115). Se `fid` nula, segue sem revogação.
6. Invoca `IAuditLog.Append` (`IF-03.1`) com evento `LOGOUT`, claims do token e `metadata = {}` (RN-113).
7. Grava no Redis `denylist:jti:{jti}` = `"1"` com TTL = `max(1, exp − agora)` segundos via `IF-04.1` (RN-111).
8. Confirma transação de base de dados via `SaveChangesAsync()` (RN-114).
9. Retorna 204 sem corpo, com `Cache-Control: no-store` e `X-Correlation-Id` (RN-116).

**Fluxos de exceção**

| ID | Passo | Condição | Resposta | Efeitos / estado final |
|---|---|---|---|---|
| EX-05.1 | 1 | Corpo ≥ 1 byte e `Content-Type` ausente ou ≠ `application/json; charset=utf-8` | 415 `ERR-REQ-004` | Rejeição camada 1. Sem persistência |
| EX-05.2 | 1 | Corpo > 65 536 bytes | 413 `ERR-REQ-003` | Rejeição camada 1. Sem persistência |
| EX-05.3 | 2 | Token ausente, assinatura inválida, expirado (`exp ≤ agora`) ou `jti` em `denylist:jti` (RG-06) | 401 `ERR-AUTH-002` | Rejeição camada 2. Sem persistência |
| EX-05.4 | 2 | Chave `denylist:user` com valor ≥ `iat` (RG-07) | 403 `ERR-AUTH-004` | Rejeição camada 2. Sem persistência |
| EX-05.5 | 2 | Tenant em `allowed_tenants` com `denylist:tenant` ativo (RG-08) | 403 `ERR-CTX-002` | Rejeição camada 2. Sem persistência |
| EX-05.6 | 3 | Corpo com propriedades, JSON malformado ou raiz não objeto (RN-110) | 400 `ERR-VAL-001` | Rejeição camada 3. Sem persistência |
| EX-05.7 | 5, 6, 8 | Indisponibilidade ou falha SQL no PostgreSQL | 503 `ERR-SYS-002` | Transação revertida. Sem persistência BD |
| EX-05.8 | 7 | Indisponibilidade ou falha no Redis durante gravação de denylist | 503 `ERR-SYS-002` | Transação revertida. Sem persistência BD/Redis |

**Concorrência, idempotência e retomada**

| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Dois pedidos simultâneos com mesmo token | Execução idempotente no Redis (`SET`) e BD (`IF-01.3`); ambos respondem 204. Requisição posterior recebe 401 `ERR-AUTH-002` | CA-05.6 |
| Processo abortado antes do commit da BD | BD reverte transação; `jti` no Redis retém TTL. Nova chamada recebe 401 `ERR-AUTH-002` | CA-05.7 |
| Resposta perdida após commit na BD | Estado revogado no servidor. Reenvio com mesmo token recebe 401 `ERR-AUTH-002` | CA-05.5 |
| Família de refresh tokens já revogada | `IF-01.3` retorna 0 linhas; conclui com 204 gravando auditoria e revogando `jti` | CA-05.4 |

**Pós-condições:**
1. Chave `denylist:jti:{jti}` gravada no Redis com valor `"1"` e TTL = `max(1, exp − agora)` segundos.
2. Se claim `fid` presente, todos os refresh tokens da família possuem status `REVOKED` e `revoked_at` preenchido.
3. Registo de auditoria `LOGOUT` persistido em `auth_audit_log`.
4. Chamadas posteriores com o mesmo token recebem status 401 `ERR-AUTH-002`.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-110 | Validação | **SE** corpo tem 0 bytes ou é objeto JSON `{}` **ENTÃO** aceita corpo e prossegue **SENÃO** rejeita com status 400 e código `ERR-VAL-001` | `MAX_BODY_BYTES = 65536` | `ERR-VAL-001` | RF-05 | `[DOC] 40 §3 (R1, R2)` |
| RN-111 | Integridade | **SE** requisição validada e autenticada **ENTÃO** grava no Redis `denylist:jti:{jti}` = `"1"` com TTL = `max(1, exp − agora)` segundos via `IF-04.1` antes do status 204 **SENÃO** rejeita com status 503 `ERR-SYS-002` | `TTL = max(1, exp − agora)` | `ERR-SYS-002` | RF-05 | `[DOC] ADR-06, 40 IF-04.1, UT-3-09` |
| RN-112 | Integridade | **SE** token contém claim `fid` com UUID v7 **ENTÃO** atualiza na BD tokens da família com status `ACTIVE`/`CONSUMED` para `REVOKED` com `revoked_at = agora` via `IF-01.3` **SENÃO** omite revogação de família | Nenhum | Nenhum | RF-05 | `[DOC] ADR-07, 40 IF-01.3, UT-3-09` |
| RN-113 | Integridade | **SE** processamento de logout executado **ENTÃO** insere em `auth_audit_log` via `IF-03.1` evento `LOGOUT` com claims do token e `metadata = {}` **SENÃO** interrompe com status 500 `ERR-SYS-001` em erro irrecuperável | Nenhum | `ERR-SYS-001` | RF-05 | `[DOC] 30 AUD.M09, 40 IF-03.1, UT-3-09` |
| RN-114 | Integridade | **SE** operação persiste alterações na BD (`refresh_token` e `auth_audit_log`) **ENTÃO** executa operações SQL em transação única antes do status 204 **SENÃO** reverte alterações na BD em falha | Nenhum | `ERR-SYS-002` | RF-05 | `[DOC] 10 §3.4, UT-3-09` |
| RN-115 | Integridade | **SE** família informada por `fid` tem zero tokens `ACTIVE` ou `CONSUMED` **ENTÃO** `IF-01.3` retorna 0 linhas e conclui com sucesso **SENÃO** atualiza linhas elegíveis e conclui com sucesso | Nenhum | Nenhum | RF-05 | `[DOC] 40 IF-01, IF-01.3` |
| RN-116 | Validação | **SE** processamento resulta em status 204 **ENTÃO** inclui cabeçalho `Cache-Control: no-store` e omite `Pragma: no-cache` **SENÃO** segue convenção de erro | Nenhum | Nenhum | RF-05 | `[DOC] 40 §1, PRS-44` |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-05 — Efetuar logout de sessão

  Contexto:
    Dado grupo "GRP1", tenant "TEN1" e utilizador "018f0003-0000-7000-8000-000000000001" ativos

  @CA-05.1 @RF-05 @RN-111 @RN-112 @RN-113 @RN-114 @RN-116 @MUST @borda
  Cenário: Logout de sessão normal com corpo de 0 bytes e expiração iminente
    Dado família "FAM1" com tokens "ACTIVE" e "CONSUMED" para o utilizador
    E token JWT válido com jti "JTI-01", fid "FAM1" e exp a 1 segundo adiante de agora
    Quando é enviado POST "/api/v1/auth/logout" com corpo vazio de 0 bytes
    Então o status da resposta é 204
    E a resposta não possui corpo
    E o cabeçalho "Cache-Control" é "no-store"
    E o cabeçalho "Pragma" não está presente
    E o cabeçalho "X-Correlation-Id" está presente
    E a chave Redis "denylist:jti:JTI-01" tem valor "1" com TTL de 1 segundo
    E os refresh tokens de "FAM1" têm status "REVOKED" e revoked_at preenchido
    E existe registo em "auth_audit_log" com event_type "LOGOUT" e metadata "{}"

  @CA-05.2 @RF-05 @RN-110 @RN-113 @RN-116 @MUST @borda
  Cenário: Logout de sessão sob impersonation com corpo "{}" e ticket no limite de tamanho
    Dado agente "018f0003-0000-7000-8000-000000000099" com platform_role "SUPPORT_AGENT"
    E token JWT sob impersonation com jti "JTI-02", sem fid, e act com sub "018f0003-0000-7000-8000-000000000099" e ticket_id "SUP-20261019-12345678" de 21 caracteres
    Quando é enviado POST "/api/v1/auth/logout" com corpo "{}" e cabeçalho Content-Type "application/json ; charset=utf-8"
    Então o status da resposta é 204
    E a chave Redis "denylist:jti:JTI-02" tem valor "1"
    E nenhum registo da tabela "refresh_token" é alterado
    E existe registo em "auth_audit_log" com event_type "LOGOUT", actor_user_id "018f0003-0000-7000-8000-000000000099" e ticket_id "SUP-20261019-12345678"

  @CA-05.3 @RF-05 @RN-110 @MUST @borda
  Esquema do Cenário: Rejeição de corpo contendo propriedades ou JSON inválido
    Dado token JWT válido em Authorization
    Quando é enviado POST "/api/v1/auth/logout" com corpo <corpo>
    Então o status da resposta é 400
    E o campo "code" é "ERR-VAL-001"
    E nenhuma alteração é persistida

    Exemplos:
      | corpo                 |
      | "{\"campo\": 1}"      |
      | "{\"\": \"valor\"}"   |
      | "{\"sessao\": null}"  |
      | "[]"                  |
      | "\"logout\""          |
      | "{"                   |

  @CA-05.4 @RF-05 @RN-112 @RN-115 @MUST @borda
  Cenário: Idempotência de logout quando refresh tokens da família já estão revogados ou ausentes
    Dado família "FAM2" sem refresh tokens com status "ACTIVE" nem "CONSUMED"
    E token JWT válido com jti "JTI-04" e fid "FAM2"
    Quando é enviado POST "/api/v1/auth/logout" com corpo vazio de 0 bytes
    Então o status da resposta é 204
    E a chave Redis "denylist:jti:JTI-04" tem valor "1"
    E os registos da família "FAM2" mantêm status "REVOKED"
    E existe registo em "auth_audit_log" com event_type "LOGOUT"

  @CA-05.5 @RF-05 @MUST @critico @borda
  Esquema do Cenário: Rejeição de token ausente, expirado ou revogado
    Dado condição do token correspondente a <condicao>
    Quando é enviado POST "/api/v1/auth/logout" com <autorizacao>
    Então o status da resposta é 401
    E o campo "code" é "ERR-AUTH-002"
    E nenhuma alteração é persistida

    Exemplos:
      | condicao                            | autorizacao                                            |
      | "cabeçalho Authorization ausente"   | sem cabeçalho Authorization                            |
      | "token expirou no segundo anterior" | cabeçalho Authorization com exp = agora - 1          |
      | "jti já consta na denylist"         | cabeçalho Authorization com jti já revogado            |
      | "assinatura forjada"                | cabeçalho Authorization com assinatura inválida        |

  @CA-05.6 @RF-05 @RN-111 @RN-114 @MUST @critico @borda
  Cenário: Execução simultânea de dois pedidos de logout com o mesmo token de acesso
    Dado família "FAM3" com refresh token "ACTIVE"
    E token JWT válido com jti "JTI-06" e fid "FAM3"
    Quando dois pedidos POST "/api/v1/auth/logout" são enviados simultaneamente com o referido token
    Então ambos os pedidos completam com status de resposta 204
    E a chave Redis "denylist:jti:JTI-06" tem valor "1"
    E os refresh tokens de "FAM3" têm status "REVOKED"
    E chamada subsequente com o token "JTI-06" retorna status 401 com código "ERR-AUTH-002"

  @CA-05.7 @RF-05 @RN-111 @RN-114 @MUST @critico @borda
  Cenário: Reversão atómica de transação em caso de timeout ou falha do Redis
    Dado família "FAM4" com refresh token "ACTIVE"
    E token JWT válido com jti "JTI-07" e fid "FAM4"
    E o Redis atinge timeout de 15 segundos durante a gravação da denylist
    Quando é enviado POST "/api/v1/auth/logout" com corpo vazio de 0 bytes
    Então o status da resposta é 503
    E o campo "code" é "ERR-SYS-002"
    E os refresh tokens de "FAM4" permanecem com status "ACTIVE"
    E nenhum registo de auditoria com event_type "LOGOUT" é persistido

  @CA-05.8 @RF-05 @MUST @critico @borda
  Esquema do Cenário: Rejeição de transporte ou presença em denylist administrativa
    Dado token JWT nas condições <condicao>
    Quando é enviado POST "/api/v1/auth/logout" com <corpo_ou_header>
    Então o status da resposta é <status>
    E o campo "code" é "<codigo_erro>"
    E nenhuma alteração é persistida

    Exemplos:
      | condicao                              | corpo_ou_header                        | status | codigo_erro  |
      | "token válido"                        | corpo de 65537 bytes                   | 413    | ERR-REQ-003  |
      | "token válido"                        | corpo de 5 bytes com tipo "text/plain" | 415    | ERR-REQ-004  |
      | "sub em denylist com timestamp = iat" | corpo vazio                            | 403    | ERR-AUTH-004 |
      | "tenant em allowed_tenants em denylist" | corpo vazio                          | 403    | ERR-CTX-002  |
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-05 | MUST | RN-110, RN-111, RN-112, RN-113, RN-114, RN-115, RN-116 | EP-04 | EX-05.1, EX-05.2, EX-05.3, EX-05.4, EX-05.5, EX-05.6, EX-05.7, EX-05.8 | CA-05.1, CA-05.2, CA-05.3, CA-05.4, CA-05.5, CA-05.6, CA-05.7, CA-05.8 | F-00 |

**Totais:** RF = 1 · RN = 7 · EP = 1 · EV = 0 · CA = 8 (`@critico` = 4) · Σw = 24

## 7. Pressupostos do módulo

### 7.1 Pressupostos (PRS)

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-190 | A gravação em `denylist:jti:{jti}` no Redis (`IF-04.1`) ocorre antes do commit na base de dados (`IdentityDbContext.SaveChangesAsync()`), garantindo reversão da transação em falha do Redis | Alinhado a PRS-15 (SET antes do commit), ADR-06 e regra de dependências (10 §7) | Pendente (PO) |

### 7.2 Dúvidas (Q)

| ID | Dúvida | Hipótese adotada | Bloqueia |
|---|---|---|---|
| Q-110 | Qual é a fase de entrega de RF-05 no plano de fases (`60`)? | Hipótese: RF-05 pertence à fase `F-00` por integrar o laço transversal de autenticação (`10` §3.4 e guia §3.3) | Não |
| Q-111 | Qual é a convenção de pesos para o somatório $\Sigma w$? | Hipótese: Cada CA `@MUST` possui peso $w = 3$, totalizando $\Sigma w = 8 \times 3 = 24$ | Não |

### 7.3 Pedidos de alteração (CR)

Nenhum. Interfaces internas, contratos de dados, tabelas e erros necessários constam integralmente em `pacote-M-09.md`.
