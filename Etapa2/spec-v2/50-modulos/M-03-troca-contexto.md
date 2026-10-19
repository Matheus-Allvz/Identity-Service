# M-03 · Troca de contexto

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-03) · **Estado:** Rascunho · **Atualizado em:** 2026-10-19
> **Pacote:** `10`, `30` (comum, AUD.M03), `40` (EP-02, IFs), `70`, `90` (UT-3-03, PRS-05). Orçamento: 60 KiB.

## 1. Âmbito do módulo

| Dentro | Fora (outro módulo ou externo) |
|---|---|
| Troca para `SINGLE` (RF-02) ou `MACRO_GROUP` (RF-03) via `EP-02`. | Login inicial em `NONE` (M-01) e rotação de RT (M-02). |
| Elegibilidade via `IAccessQueries` (`IF-09`). | Gestão de tenants e membros (M-04, M-05). |
| Atualiza linha `ACTIVE` da família em `refresh_token` (`IF-01.2`). | Publicação outbox via Kafka (M-14). |
| Revoga token anterior (`IF-04.1`), emite novo JWT (`IF-06.1`) e auditoria (`IF-03.1`). | Gestão JWKS (M-10) e autorização em APIs negociais. |

**Escreve:** Nenhuma direta (BD via `IF-01.2`/`IF-03.1`, Redis via `IF-04.1`) · **Lê:** Nenhuma direta (`IF-09`, `IF-08.1`) · **Usa:** M-00, M-02, M-07, M-08

## 2. Endpoints e eventos (detalhe)

### EP-02 `POST /api/v1/auth/switch-context`

| Campo | Valor |
|---|---|
| RF | RF-02 (`SINGLE`), RF-03 (`MACRO_GROUP`) |
| Autenticação / Permissão | JWT Bearer. Sem claim act (403 `ERR-CTX-007`). Qualquer papel ou escopo |
| Idempotência | Não. Sucesso revoga token anterior (Redis) e emite novo JWT |
| RN aplicáveis | RN-50 a RN-57 |

**Request**

| Campo | Local | Tipo | Ob. | Regra |
|---|---|---|---|---|
| `Authorization` | header | string | Sim | `Bearer <jwt>` compacto |
| `Content-Type` | header | string | Sim | `application/json` |
| `X-Correlation-Id` | header | string | Não | UUID D (36 caracteres) |
| `view_mode` | body | string | Sim | `SINGLE` ou `MACRO_GROUP` |
| `target_scope` | body | string | Sim | Escopo operacional |
| `target_tenant_id` | body | string | Cond. | UUID D (`SINGLE`) |
| `target_group_id` | body | string | Cond. | UUID D (`MACRO_GROUP`) |
| `target_tenants` | body | array | Cond. | 1–50 UUIDs (`MACRO_GROUP`) |

```json
{"view_mode":"SINGLE","target_scope":"BUYER","target_tenant_id":"018f0002-0000-7000-8000-000000000001"}
```

**Ordem de avaliação** (a primeira que falha determina a resposta)

| # | Verificação | Camada | Se falhar |
|---|---|---|---|
| 1 | POST em `/api/v1/auth/switch-context` | Encaminhamento | 404 `ERR-REQ-001`, 405 `ERR-REQ-002` |
| 2 | Tamanho ≤ 64 KB e JSON | Transporte | 413 `ERR-REQ-003`, 415 `ERR-REQ-004` |
| 3 | RSA ativo (`ISigningKey`) | Autenticação | 503 `ERR-SYS-002` |
| 4 | JWT íntegro e não expirado | Autenticação | 401 `ERR-AUTH-002` |
| 5 | Sem denylist user/tenant | Autenticação | 403 `ERR-AUTH-004`, `ERR-CTX-002` |
| 6 | JSON válido e view_mode ≠ NONE | Formato | 400 `ERR-VAL-001` |
| 7 | Sem claim `act` | Autorização | 403 `ERR-CTX-007` |
| 8 | Utilizador ativo na BD (`IF-09.1`) | Negócio | 403 `ERR-AUTH-004` |
| 9 | SINGLE: elegibilidade (`IF-09`) | Negócio | 403 `ERR-CTX-001`..`003` |
| 10 | MACRO_GROUP: regras (`IF-09.5`) | Negócio | 403 `ERR-CTX-004`, `002`, `003` |
| 11 | Atualiza RT ativo (`IF-01.2`) | Negócio | 401 `ERR-AUTH-002` |
| 12 | Auditoria na BD (`IF-03.1`) | Negócio | 500 `ERR-SYS-001` |
| 13 | Redis `IF-04.1` e commit BD | Negócio | 503 `ERR-SYS-002` |
| 14 | Emite JWT via `IF-06.1` (mesmo fid) | Negócio | 200 OK |

**Respostas** (incluindo as do middleware)

| Status | Condição | Código |
|---|---|---|
| 200 | Sucesso; novo JWT emitido com mesmo `fid` | – |
| 400 | Payload inválido, view_mode = NONE ou campos conflitantes | `ERR-VAL-001` |
| 401 | Token inválido/expirado/revogado ou família sem RT ativo | `ERR-AUTH-002` |
| 403 | Claim act (`ERR-CTX-007`); inativo (`ERR-AUTH-004`); inelegível (`ERR-CTX-001`..`004`) | `ERR-CTX-007`, `ERR-AUTH-004`, `ERR-CTX-001`..`004` |
| 404 / 405 | Rota inexistente / Método inválido | `ERR-REQ-001` / `ERR-REQ-002` |
| 413 / 415 | Corpo > 64 KB / Content-Type não JSON | `ERR-REQ-003` / `ERR-REQ-004` |
| 500 / 503 | Falha BD inesperada / Falha Redis ou ISigningKey | `ERR-SYS-001` / `ERR-SYS-002` |

**Corpo de sucesso**

| Campo | Tipo | Sempre | Valor / regra | Ordenação |
|---|---|---|---|---|
| `access_token` | string | Sim | JWT RS256 com claims do novo contexto | Não se aplica |
| `token_type` | string | Sim | Literal `"Bearer"` | Não se aplica |
| `expires_in` | integer | Sim | Inteiro fixo `900` | Não se aplica |
| `context` | object | Sim | Objeto com claims do contexto ativo | Arrays em ASC |

```json
{"access_token":"<jwt>","token_type":"Bearer","expires_in":900,"context":{"sub":"USR1","group_id":"GRP1","tenant_id":"TEN1","view_mode":"SINGLE","active_scope":"BUYER","roles":[],"permissions":[]}}
```

**Efeitos colaterais:** 1. Atualiza RT ativo (`IF-01.2`). 2. Insere `CONTEXT_SWITCHED` (`IF-03.1`). 3. Grava `denylist:jti` no Redis (`IF-04.1`). 4. Commit BD. 5. Emite novo JWT com mesmo `fid` (`IF-06.1`).
**Transacionalidade:** BD (`IF-01.2`, `IF-03.1`) atómica. Redis gravado antes do commit (PRS-15). Em falha, rollback e token de entrada válido.

### EV (Eventos)

| Campo | Tipo | Obrigatório | Valor |
|---|---|---|---|
| Nenhum | – | – | Não publica Kafka diretamente (outbox via M-14) |

## 3. Requisitos funcionais

### RF-02 — Trocar contexto para visão individual (SINGLE)

| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Utilizador sem `act` / `POST /api/v1/auth/switch-context` (`view_mode = "SINGLE"`) |
| Pré-condições | Token ativo com `fid`, sem `act`; vínculo ativo no tenant/escopo |
| RN | RN-50..52, RN-54..57 |

**Fluxo:** 1. Valida camadas 1, 2 e 4 (sem `act`, RN-51). 2. Valida payload (RN-50). 3. Em transação BD, avalia `IF-09` (RN-52). 4. Atualiza RT (`IF-01.2`, RN-54) e auditoria (`IF-03.1`, RN-57). 5. Grava `denylist:jti` no Redis (`IF-04.1`, RN-55). 6. Commit BD, emite JWT via `IF-06.1` (RN-56) e retorna 200 OK.

### RF-03 — Trocar contexto para visão agregada (MACRO_GROUP)

| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Utilizador sem `act` / `POST /api/v1/auth/switch-context` (`view_mode = "MACRO_GROUP"`) |
| Pré-condições | Token ativo com `fid`, sem `act`; `can_view_group = true` no grupo |
| RN | RN-50, RN-51, RN-53..57 |

**Fluxo:** 1. Valida camadas 1, 2 e 4 (sem `act`). 2. Valida payload (RN-50). 3. Em transação BD, avalia `IF-09.1` e `IF-09.5` (RN-53, PRS-05). 4. Atualiza RT (`IF-01.2`), auditoria (`IF-03.1`), Redis (`IF-04.1`) e commit BD. 5. Emite JWT via `IF-06.1` e retorna 200 OK.

**Fluxos de exceção**

| ID | Passo | Condição | Resposta | Efeito |
|---|---|---|---|---|
| EX-02.1 | 1 | Token com claim `act` | 403 `ERR-CTX-007` | Inalterado |
| EX-02.2 | 2 | Corpo inválido ou `view_mode = NONE` | 400 `ERR-VAL-001` | Inalterado |
| EX-02.3 | 3 | Utilizador inativo/suspenso | 403 `ERR-AUTH-004` | Inalterado |
| EX-02.4 | 3 | Tenant sem vínculo ativo | 403 `ERR-CTX-001` | Inalterado |
| EX-02.5 | 3 | Tenant ou grupo suspenso | 403 `ERR-CTX-002` | Inalterado |
| EX-02.6 | 3 | Escopo inativo ou sem papéis | 403 `ERR-CTX-003` | Inalterado |
| EX-02.7 | 3 | Regras macro violadas | 403 `ERR-CTX-004` | Inalterado |
| EX-02.8 | 4 | Família sem RT ativo (`IF-01.2` false) | 401 `ERR-AUTH-002` | Rollback |
| EX-02.9 | 5 | Falha de Redis ou BD | 503 `ERR-SYS-002` | Rollback |

**Concorrência, idempotência e retomada**

| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Pedidos concorrentes com mesmo token | Um obtém 200; o outro falha 401 `ERR-AUTH-002` | CA-06.8 |
| Falha Redis antes do commit BD | Rollback BD; 503 `ERR-SYS-002`; token de entrada válido | CA-06.8 |
| Queda de rede após 200 | Estado persistido; token revogado; reenvio falha 401 | CA-06.1 |
| Família revogada concorrentemente | `IF-01.2` false; rollback BD e 401 `ERR-AUTH-002` | CA-06.7 |

**Pós-condições:** 1. RT ativo atualizado (`IF-01.2`). 2. Token revogado no Redis (`TTL = max(1, exp − agora)`). 3. Auditoria `CONTEXT_SWITCHED` gravada. 4. Novo JWT emitido (`exp = 900s`, mesmo `fid`). 5. Em rejeição, BD/Redis inalterados.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-50 | Validação | **SE** payload tem `SINGLE` com `target_tenant_id` ou `MACRO_GROUP` com `target_group_id` e 1–50 tenants **ENTÃO** aceita **SENÃO** 400 `ERR-VAL-001` | `1–50 tenants` | `ERR-VAL-001` | RF-02, RF-03 | `[DOC] 40 §4.1` |
| RN-51 | Autorização | **SE** token tem claim `act` **ENTÃO** 403 `ERR-CTX-007` **SENÃO** prossegue | Nenhum | `ERR-CTX-007` | RF-02, RF-03 | `[DOC] ADR-08` |
| RN-52 | Autorização | **SE** `SINGLE` **ENTÃO** valida utilizador, tenant, vínculo e papéis via `IF-09` **SENÃO** 403 | Nenhum | `ERR-CTX-001`..`003` | RF-02 | `[DOC] 10 §3.6` |
| RN-53 | Autorização | **SE** `MACRO_GROUP` **ENTÃO** valida via `ResolveMacroGroupAsync` 1–50 tenants, grupo, pertença, `can_view_group` e status **SENÃO** 403 | `can_view_group` | `ERR-CTX-004`, `002`, `003` | RF-03 | `[DOC] 10 §3.6` |
| RN-54 | Integridade | **SE** `UpdateActiveContextAsync` retorna `false` **ENTÃO** rollback e 401 `ERR-AUTH-002` **SENÃO** atualiza contexto | Nenhum | `ERR-AUTH-002` | RF-02, RF-03 | `[DOC] 40 §4.1` |
| RN-55 | Integridade | **SE** aprovado **ENTÃO** grava no Redis `denylist:jti:{jti}` (`TTL = max(1, exp − agora)`) antes do commit **SENÃO** 503 `ERR-SYS-002` | `TTL = max(1, exp − agora)` | `ERR-SYS-002` | RF-02, RF-03 | `[DOC] ADR-06` |
| RN-56 | Integridade | **SE** concluído **ENTÃO** emite novo JWT com mesmo `fid` e validade 900 s via `IF-06.1` **SENÃO** nenhum token emitido | `expires_in = 900` | Nenhum | RF-02, RF-03 | `[DOC] 10 §3.5` |
| RN-57 | Integridade | **SE** aprovado **ENTÃO** grava `CONTEXT_SWITCHED` em `auth_audit_log` na mesma transação BD **SENÃO** nada persiste | Nenhum | Nenhum | RF-02, RF-03 | `[DOC] 30 AUD.M03` |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-02 e RF-03 — Troca de contexto

Contexto:
  Dado grupo "GRP1", tenant "TEN1", utilizador "USR1" e família "FAM1" com RT ativo

@CA-06.1 @RF-02 @RN-50 @RN-52 @RN-54 @RN-55 @RN-56 @RN-57 @MUST @borda
Cenário: Troca para SINGLE no último segundo do token
  Dado token em NONE com jti "JTI-1", fid "FAM1" e exp em 1s
  E vínculo ativo de "USR1" no tenant "TEN1" em BUYER
  Quando POST "/api/v1/auth/switch-context" com SINGLE, BUYER e "TEN1"
  Então status 200, expires_in 900, "JTI-1" em denylist e RT atualizado

@CA-06.2 @RF-03 @RN-50 @RN-53 @RN-56 @MUST @borda
Cenário: Troca para MACRO_GROUP no limite de 50 tenants
  Dado token em SINGLE com jti "JTI-2", fid "FAM1" e 50 tenants válidos em "GRP1"
  Quando POST "/api/v1/auth/switch-context" com MACRO_GROUP e os 50 tenants
  Então status 200, 50 tenants ordenados, "JTI-2" em denylist e roles vazio

@CA-06.3 @RF-02 @RF-03 @RN-51 @MUST @critico @borda
Cenário: Rejeição de token sob impersonation
  Dado token sob impersonation com claim act
  Quando POST "/api/v1/auth/switch-context" com SINGLE
  Então status 403 com code "ERR-CTX-007" e sem escrita BD/Redis

@CA-06.4 @RF-02 @RF-03 @RN-50 @MUST @borda
Esquema do Cenário: Rejeição de payload inválido
  Quando POST "/api/v1/auth/switch-context" com corpo <corpo>
  Então status 400 com code "ERR-VAL-001" e sem escrita BD/Redis
  Exemplos:
    | corpo |
    | {"view_mode":"NONE"} |
    | {"view_mode":"SINGLE","target_tenants":["TEN1"]} |
    | {"view_mode":"MACRO_GROUP","target_tenant_id":"TEN1"} |
    | {"view_mode":"MACRO_GROUP","target_group_id":"GRP1","target_tenants":[]} |

@CA-06.5 @RF-02 @RN-52 @MUST @borda
Esquema do Cenário: Inelegibilidade em SINGLE
  Dado token com jti "JTI-5" e fid "FAM1"
  Quando POST "/api/v1/auth/switch-context" para SINGLE com <motivo>
  Então status <status> com code "<cod>" e sem escrita BD/Redis
  Exemplos:
    | motivo | status | cod |
    | tenant inexistente | 403 | ERR-CTX-001 |
    | tenant suspenso | 403 | ERR-CTX-002 |
    | sem vínculo | 403 | ERR-CTX-001 |
    | vínculo suspenso | 403 | ERR-CTX-001 |
    | sem papéis | 403 | ERR-CTX-003 |

@CA-06.6 @RF-03 @RN-53 @MUST @critico @borda
Esquema do Cenário: Inelegibilidade em MACRO_GROUP
  Dado token com jti "JTI-6" e fid "FAM1"
  Quando POST "/api/v1/auth/switch-context" para MACRO_GROUP com <motivo>
  Então status <status> com code "<cod>" e token de entrada válido
  Exemplos:
    | motivo | status | cod |
    | tenant outro grupo | 403 | ERR-CTX-004 |
    | can_view_group falso | 403 | ERR-CTX-004 |
    | permissões vazias | 403 | ERR-CTX-003 |
    | tenant duplicado | 403 | ERR-CTX-004 |

@CA-06.7 @RF-02 @RF-03 @RN-54 @MUST @critico @borda
Cenário: Rejeição 401 sem RT ativo
  Dado token em NONE com fid "FAM-REVOGADA" sem RT ativo
  Quando POST "/api/v1/auth/switch-context" para SINGLE
  Então status 401 com code "ERR-AUTH-002" e sem escrita BD/Redis

@CA-06.8 @RF-02 @RF-03 @RN-55 @RN-56 @MUST @critico @borda
Cenário: Concorrência com mesmo token
  Dado token com jti "JTI-8" e fid "FAM1"
  Quando duas requisições POST "/api/v1/auth/switch-context" concorrem
  Então uma obtém 200, a outra falha 401 "ERR-AUTH-002" e "JTI-8" entra na denylist
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-02 | MUST | RN-50..52, RN-54..57 | EP-02 | EX-02.1..02.6, EX-02.8, EX-02.9 | CA-06.1, CA-06.3..06.5, CA-06.7, CA-06.8 | F-00 |
| RF-03 | MUST | RN-50, RN-51, RN-53..57 | EP-02 | EX-02.1..02.3, EX-02.6..02.9 | CA-06.2..06.4, CA-06.6..06.8 | F-00 |

**Totais:** RF = 2 · RN = 8 · EP = 1 · EV = 0 · CA = 8 (`@critico` = 4) · Σw = 24

## 7. Pressupostos do módulo

### 7.1 Pressupostos (PRS)

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-130 | `IF-04.1 RevokeJtiAsync` executado antes do commit BD, permitindo rollback | ADR-06, PRS-15 | Pendente (PO) |

### 7.2 Dúvidas (Q)

| ID | Dúvida | Hipótese adotada | Bloqueia |
|---|---|---|---|
| Q-50 | Fase de entrega em `60`? | Hipótese: Fase `F-00` (token ativo) | Não |
| Q-51 | Convenção cálculo Σw? | Hipótese: Cada CA `@MUST` peso w = 3 (Σw = 24) | Não |

### 7.3 Pedidos de alteração (CR)

Nenhum. Contratos partilhados e interfaces constam no pacote.
