# M-03 · Troca de contexto

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-03) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** `10`, `30` (comum, AUD.M03), `40` (EP-02, IFs), `70`, `90` (UT-3-03, PRS-05). Orçamento: 60 KiB.

## 1. Âmbito do módulo

| Dentro | Fora (outro módulo ou externo) |
|---|---|
| Troca para `SINGLE` (RF-02) ou `MACRO_GROUP` (RF-03) via `EP-02`. | Login inicial em `NONE` (M-01) e rotação de RT (M-02). |
| Elegibilidade via `IAccessQueries` (`IF-09`). | Gestão de tenants e membros (M-04, M-05). |
| Atualiza linha `ACTIVE` da família em `refresh_token` (`IF-01.2`). | Publicação outbox via Kafka (M-14). |
| Revoga token anterior (`IF-04.1`), emite novo JWT (`IF-06.1`) e auditoria (`IF-03.1`). | Gestão JWKS (M-07) e autorização em APIs negociais. |

**Escreve:** Nenhuma direta (BD via `IF-01.2`/`IF-03.1`, Redis via `IF-04.1`) · **Lê:** Nenhuma direta (`IF-09`, `IF-08.1`) · **Usa:** M-00, M-02, M-07, M-08

## 2. Endpoints e eventos (detalhe)

### EP-02 `POST /api/v1/auth/switch-context`

| Campo | Valor |
|---|---|
| RF | RF-02 (`SINGLE`), RF-03 (`MACRO_GROUP`) |
| Autenticação / Permissão | JWT Bearer. Sem claim `act` (403 `ERR-CTX-007`). Qualquer papel ou escopo |
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
| `target_tenant_id` | body | string | Cond. | UUID D (obrigatório em `SINGLE`) |
| `target_group_id` | body | string | Cond. | UUID D (obrigatório em `MACRO_GROUP`) |
| `target_tenants` | body | array | Cond. | Array JSON de UUIDs formato D (obrigatório em `MACRO_GROUP`) |

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
| 6 | JSON válido, objeto raiz, sintaxe correta (RN-50) | Formato | 400 `ERR-VAL-001` |
| 7 | Sem claim `act` (RN-51) | Autorização | 403 `ERR-CTX-007` |
| 8 | Utilizador ativo na BD (`IF-09.1`) | Negócio | 403 `ERR-AUTH-004` |
| 9 | SINGLE: elegibilidade do tenant via `IF-09` (RN-52) | Negócio | 403 `ERR-CTX-001`..`003` |
| 10 | MACRO_GROUP: RG-11 (1–50 tenants, sem dup, grupo) via `IF-09.5` (RN-53) | Negócio | 403 `ERR-CTX-004`, `002`, `003` |
| 11 | Atualiza RT ativo (`IF-01.2`, RN-54) | Negócio | 401 `ERR-AUTH-002` |
| 12 | Auditoria na BD (`IF-03.1`, RN-57) | Negócio | 500 `ERR-SYS-001` |
| 13 | Redis `IF-04.1` (RN-55) e commit BD | Negócio | 503 `ERR-SYS-002` |
| 14 | Emite JWT via `IF-06.1` (mesmo fid, RN-56) | Negócio | 200 OK |

**Respostas**
| Status | Condição | Código |
|---|---|---|
| 200 | Sucesso; novo JWT emitido com mesmo `fid` | – |
| 400 | Payload inválido, view_mode = NONE ou campos conflitantes | `ERR-VAL-001` |
| 401 | Token inválido/expirado/revogado ou família sem RT ativo | `ERR-AUTH-002` |
| 403 | Claim act (`ERR-CTX-007`); inativo (`ERR-AUTH-004`); inelegível (`ERR-CTX-001`..`004`) | `ERR-CTX-007`, `ERR-AUTH-004`, `ERR-CTX-001`..`004` |
| 404 / 405 | Rota inexistente / Método inválido | `ERR-REQ-001` / `ERR-REQ-002` |
| 413 / 415 | Corpo > 64 KB / Content-Type não JSON | `ERR-REQ-003` / `ERR-REQ-004` |
| 500 / 503 | Falha BD inesperada / Falha Redis ou ISigningKey | `ERR-SYS-001` / `ERR-SYS-002` |

**Headers de resposta 200:** `Content-Type: application/json; charset=utf-8`, `Cache-Control: no-store`, `Pragma: no-cache`.

**Corpo de sucesso**
```json
{"access_token":"<jwt>","token_type":"Bearer","expires_in":900,"context":{"sub":"USR1","group_id":"GRP1","tenant_id":"TEN1","view_mode":"SINGLE","active_scope":"BUYER","roles":[],"permissions":[],"allowed_tenants":["01900000-0000-7000-8000-000000000010"]}}
```

**Efeitos colaterais:** 1. Atualiza RT ativo (`IF-01.2`). 2. Insere `CONTEXT_SWITCHED` (`IF-03.1`). 3. Grava `denylist:jti` no Redis (`IF-04.1`). 4. Commit BD. 5. Emite novo JWT com mesmo `fid` (`IF-06.1`).

## 3. Requisitos funcionais

### RF-02 — Trocar contexto para visão individual (SINGLE)
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Utilizador sem `act` / `POST /api/v1/auth/switch-context` (`view_mode = "SINGLE"`) |
| Pré-condições | Token ativo com `fid`, sem `act`; vínculo ativo no tenant/escopo |
| RN | RN-50, RN-51, RN-52, RN-54, RN-55, RN-56, RN-57 |

**Fluxo principal:** 1. Valida camadas 1, 2 e 4 (sem `act`, RN-51). 2. Valida payload (RN-50). 3. Em transação BD, avalia `IF-09` (RN-52). 4. Atualiza RT (`IF-01.2`, RN-54) e auditoria (`IF-03.1`, RN-57). 5. Grava `denylist:jti` no Redis (`IF-04.1`, RN-55). 6. Commit BD, emite JWT via `IF-06.1` (RN-56) e retorna 200 OK com headers sem cache.

**Fluxos de exceção (RF-02):**
- EX-02.1: Token com claim `act` → 403 `ERR-CTX-007`.
- EX-02.2: Corpo inválido, `view_mode = NONE` ou `target_tenant_id` ausente → 400 `ERR-VAL-001`.
- EX-02.3: Utilizador inativo ou suspenso → 403 `ERR-AUTH-004`.
- EX-02.4: Tenant inexistente ou sem vínculo ativo → 403 `ERR-CTX-001`.
- EX-02.5: Tenant ou grupo suspenso → 403 `ERR-CTX-002`.
- EX-02.6: Escopo inativo ou sem papéis → 403 `ERR-CTX-003`.

---

### RF-03 — Trocar contexto para visão agregada (MACRO_GROUP)
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Utilizador sem `act` / `POST /api/v1/auth/switch-context` (`view_mode = "MACRO_GROUP"`) |
| Pré-condições | Token ativo com `fid`, sem `act`; `can_view_group = true` no grupo |
| RN | RN-50, RN-51, RN-53, RN-54, RN-55, RN-56, RN-57 |

**Fluxo principal:** 1. Valida camadas 1, 2 e 4 (sem `act`, RN-51). 2. Valida payload (RN-50). 3. Em transação BD, avalia RG-11 via `IF-09.1` e `IF-09.5` (RN-53, PRS-05). 4. Atualiza RT (`IF-01.2`, RN-54), auditoria (`IF-03.1`, RN-57), Redis (`IF-04.1`, RN-55) e commit BD. 5. Emite JWT via `IF-06.1` (RN-56) e retorna 200 OK com headers sem cache.

**Fluxos de exceção (RF-03):**
- EX-03.1: Token com claim `act` → 403 `ERR-CTX-007`.
- EX-03.2: Corpo inválido, `target_tenants` não-array ou UUIDs malformados → 400 `ERR-VAL-001`.
- EX-03.3: `target_tenants` vazio `[]`, > 50 elementos ou UUIDs duplicados (RG-11) → 403 `ERR-CTX-004`.
- EX-03.4: Sem permissão `can_view_group` ou tenant fora do grupo → 403 `ERR-CTX-004`.
- EX-03.5: Tenant ou grupo suspenso → 403 `ERR-CTX-002`.
- EX-03.6: Escopo sem papéis ou permissões vazias → 403 `ERR-CTX-003`.

**Concorrência, idempotência e retomada**
| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Pedidos concorrentes com mesmo token | Um obtém 200; o outro falha 401 `ERR-AUTH-002` | CA-03.4 |
| Falha Redis antes do commit BD | Rollback BD; 503 `ERR-SYS-002`; token de entrada válido | CA-03.4 |
| Queda de rede após 200 | Estado persistido; token revogado; reenvio falha 401 | CA-02.1 |
| Família revogada concorrentemente | `IF-01.2` false; rollback BD e 401 `ERR-AUTH-002` | CA-03.3 |

**Pós-condições:** 1. RT ativo atualizado (`IF-01.2`). 2. Token revogado no Redis (`TTL = max(1, exp − agora)`). 3. Auditoria `CONTEXT_SWITCHED` gravada. 4. Novo JWT emitido (`exp = 900s`, mesmo `fid`). 5. Em rejeição, BD/Redis inalterados.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-50 | Validação | **SE** JSON raiz for objeto com `view_mode` (`SINGLE` com `target_tenant_id` UUID formato D ou `MACRO_GROUP` com `target_group_id` UUID e `target_tenants` array JSON de UUIDs formato D) **ENTÃO** aceita **SENÃO** 400 `ERR-VAL-001` | Formato estrito | `ERR-VAL-001` | RF-02, RF-03 | `40` §4.1 |
| RN-51 | Autorização | **SE** token tem claim `act` **ENTÃO** 403 `ERR-CTX-007` **SENÃO** prossegue | – | `ERR-CTX-007` | RF-02, RF-03 | ADR-08 |
| RN-52 | Autorização | **SE** `SINGLE` **ENTÃO** valida utilizador, tenant, vínculo e papéis via `IF-09` **SENÃO** 403 (`ERR-CTX-001`..`003`) | – | `ERR-CTX-001`..`003` | RF-02 | `10` §3.6 |
| RN-53 | Autorização | **SE** `MACRO_GROUP` **ENTÃO** valida via `IF-09.5` RG-11 (`target_tenants` 1 a 50 UUIDs sem duplicados, grupo, `can_view_group`, status) **SENÃO** 403 | `can_view_group` | `ERR-CTX-004`, `002`, `003` | RF-03 | `10` §3.6 |
| RN-54 | Integridade | **SE** `UpdateActiveContextAsync` retorna `false` **ENTÃO** rollback e 401 `ERR-AUTH-002` **SENÃO** atualiza contexto | – | `ERR-AUTH-002` | RF-02, RF-03 | `40` §4.1 |
| RN-55 | Integridade | **SE** aprovado **ENTÃO** grava no Redis `denylist:jti:{jti}` (`TTL = max(1, exp − agora)`) antes do commit **SENÃO** 503 `ERR-SYS-002` | TTL max(1, exp-agora) | `ERR-SYS-002` | RF-02, RF-03 | ADR-06 |
| RN-56 | Integridade | **SE** concluído **ENTÃO** emite novo JWT com mesmo `fid` e validade 900 s via `IF-06.1` **SENÃO** nenhum token emitido | `expires_in = 900` | – | RF-02, RF-03 | `10` §3.5 |
| RN-57 | Integridade | **SE** aprovado **ENTÃO** grava `CONTEXT_SWITCHED` em `auth_audit_log` na mesma transação BD **SENÃO** nada persiste | – | – | RF-02, RF-03 | AUD.M03 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-02 e RF-03 — Troca de contexto

Contexto:
  Dado grupo "GRP1", tenant "TEN1", utilizador "USR1" e família "FAM1" com RT ativo

@CA-02.1 @RF-02 @RN-50 @RN-52 @RN-54 @RN-55 @RN-56 @RN-57 @MUST @borda
Cenário: Troca para SINGLE no último segundo do token
  Dado token em NONE com jti "JTI-1", fid "FAM1" e exp em 1s
  E vínculo ativo de "USR1" no tenant "TEN1" em BUYER
  Quando POST "/api/v1/auth/switch-context" com SINGLE, BUYER e "TEN1"
  Então status 200, expires_in 900, "JTI-1" em denylist e RT atualizado

@CA-02.2 @RF-02 @RN-51 @MUST @critico @borda
Cenário: Rejeição de token sob impersonation
  Dado token sob impersonation com claim act
  Quando POST "/api/v1/auth/switch-context" com SINGLE
  Então status 403 com code "ERR-CTX-007" e sem escrita BD/Redis

@CA-02.3 @RF-02 @RN-50 @MUST @borda
Esquema do Cenário: Rejeição de payload inválido
  Quando POST "/api/v1/auth/switch-context" com corpo <corpo>
  Então status 400 com code "ERR-VAL-001" e sem escrita BD/Redis
  Exemplos:
    | corpo |
    | {"view_mode":"NONE"} |
    | {"view_mode":"SINGLE","target_tenants":["01900000-0000-7000-8000-000000000010"]} |
    | {"view_mode":"MACRO_GROUP","target_tenant_id":"01900000-0000-7000-8000-000000000010"} |
    | {"view_mode":"MACRO_GROUP","target_group_id":"01900000-0000-7000-8000-000000000001","target_tenants":"invalido"} |

@CA-02.4 @RF-02 @RN-52 @MUST @borda
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

@CA-03.1 @RF-03 @RN-50 @RN-53 @RN-56 @MUST @borda
Cenário: Troca para MACRO_GROUP no limite de 50 tenants
  Dado token em SINGLE com jti "JTI-2", fid "FAM1" e 50 tenants válidos em "GRP1"
  Quando POST "/api/v1/auth/switch-context" com MACRO_GROUP e os 50 tenants
  Então status 200, 50 tenants ordenados, "JTI-2" em denylist e roles vazio

@CA-03.2 @RF-03 @RN-53 @MUST @critico @borda
Esquema do Cenário: Inelegibilidade em MACRO_GROUP
  Dado token com jti "JTI-6" e fid "FAM1"
  Quando POST "/api/v1/auth/switch-context" para MACRO_GROUP com <motivo>
  Então status <status> com code "<cod>" e token de entrada válido
  Exemplos:
    | motivo | status | cod |
    | array vazio | 403 | ERR-CTX-004 |
    | tenant outro grupo | 403 | ERR-CTX-004 |
    | can_view_group falso | 403 | ERR-CTX-004 |
    | permissões vazias | 403 | ERR-CTX-003 |
    | tenant duplicado | 403 | ERR-CTX-004 |

@CA-03.3 @RF-02 @RF-03 @RN-54 @MUST @critico @borda
Cenário: Rejeição 401 sem RT ativo
  Dado token em NONE com fid "FAM-REVOGADA" sem RT ativo
  Quando POST "/api/v1/auth/switch-context" para SINGLE
  Então status 401 com code "ERR-AUTH-002" e sem escrita BD/Redis

@CA-03.4 @RF-02 @RF-03 @RN-55 @RN-56 @MUST @critico @borda
Cenário: Concorrência com mesmo token
  Dado token com jti "JTI-8" e fid "FAM1"
  Quando duas requisições POST "/api/v1/auth/switch-context" concorrem
  Então uma obtém 200, a outra falha 401 "ERR-AUTH-002" e "JTI-8" entra na denylist
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-02 | MUST | RN-50, RN-51, RN-52, RN-54 a RN-57 | EP-02 | EX-02.1 a EX-02.6 | CA-02.1 a CA-02.4 | F-01 |
| RF-03 | MUST | RN-50, RN-51, RN-53 a RN-57 | EP-02 | EX-03.1 a EX-03.6 | CA-03.1 a CA-03.4 | F-01 |

**Totais:** RF = 2 · RN = 8 · EP = 1 · EV = 0 · CA = 8 (`@critico` = 4) · Σw = 24

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-130 | `IF-04.1 RevokeJtiAsync` executado antes do commit BD, permitindo rollback | ADR-06, PRS-15 | PO, 2026-11-02 (D-23) |
